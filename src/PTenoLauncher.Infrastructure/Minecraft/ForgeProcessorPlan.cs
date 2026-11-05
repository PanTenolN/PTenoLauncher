using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Security;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

internal sealed record ForgeOutput(string Coordinate, string Path, string Sha1);
internal sealed record ForgeProcessorStep(string Coordinate, string MainClass, IReadOnlyList<string> Classpath,
    IReadOnlyList<string> Arguments, IReadOnlyList<ForgeOutput> Outputs);
internal sealed record ForgeProcessorPlan(IReadOnlyList<ForgeProcessorStep> Steps, IReadOnlyDictionary<string, string> LibraryPaths);

/// <summary>Strict spec-1 subset: explicit SHA-1 outputs, declared executable inputs and ordered producers.
/// A working directory is not a sandbox for Java code. Only verified official inputs may reach this adapter.</summary>
internal static class ForgeProcessorPlanning
{
    internal static ForgeProcessorPlan Build(ForgeInstallPlan plan, string stage, IReadOnlyDictionary<string, string>? executableSources = null)
    {
        SafePath.RejectLinks(stage);
        if (plan.ClientProcessors.Count is 0 or > 32) throw new NotSupportedException("Supported installers require 1–32 client processors with explicit outputs.");
        var libraries = plan.Libraries.ToDictionary(l => l.Coordinate, StringComparer.Ordinal);
        var paths = libraries.ToDictionary(l => l.Key, l => SafePath.Join(stage, "libraries/" + l.Value.MavenPath), StringComparer.Ordinal);
        foreach (var raw in plan.ClientData.Values.Where(v => v.StartsWith('[') && v.EndsWith(']')))
        {
            var c = ForgeCoordinate.Parse(raw[1..^1]);
            paths.TryAdd(raw[1..^1], SafePath.Join(stage, "libraries/" + c.Path));
        }
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in paths) if (!aliases.TryAdd(p.Value, p.Key)) throw new InvalidDataException("Processor Maven path collision.");
        var data = new Dictionary<string, string>(StringComparer.Ordinal) {
            ["ROOT"] = stage, ["LIBRARY_DIR"] = SafePath.Join(stage, "libraries"), ["SIDE"] = "client",
            ["MINECRAFT_JAR"] = SafePath.Join(stage, "minecraft.jar"), ["MINECRAFT_VERSION"] = plan.MinecraftVersion,
            ["INSTALLER"] = SafePath.Join(stage, "installer.jar")
        };
        foreach (var p in plan.ClientData)
        {
            var v = p.Value;
            data.Add(p.Key, v.StartsWith('[') && v.EndsWith(']') ? paths[v[1..^1]] :
                v.Length >= 2 && v.StartsWith('\'') && v.EndsWith('\'') ? Literal(v[1..^1]) :
                SafePath.Join(stage, "data/" + (v.StartsWith('/') ? v[1..] : v)));
        }
        string Expand(string raw)
        {
            if (raw.Length > 8192 || raw.Any(char.IsControl)) throw new InvalidDataException("Invalid processor argument.");
            var value = Regex.Replace(raw, @"\{([A-Z0-9_]+)\}|\[([^\[\]]+)\]", m =>
                m.Groups[1].Success ? data.TryGetValue(m.Groups[1].Value, out var d) ? d : throw new NotSupportedException("Unknown processor variable.") :
                paths.TryGetValue(m.Groups[2].Value, out var p) ? p : throw new NotSupportedException("Undeclared processor coordinate."), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (value.IndexOfAny(['{', '}', '[', ']']) >= 0 || value.Length > 32768) throw new NotSupportedException("Ambiguous processor substitution.");
            return value;
        }
        var available = libraries.Where(l => !l.Value.Generated).Select(l => l.Key).ToHashSet(StringComparer.Ordinal);
        var produced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var steps = new List<ForgeProcessorStep>();
        foreach (var processor in plan.ClientProcessors)
        {
            var executable = ForgeCoordinate.Parse(processor.Jar);
            if (!(executable.Group is "net.minecraftforge" or "net.neoforged.installertools") ||
                executable.Artifact is not ("installertools" or "binarypatcher" or "jarsplitter" or "ForgeAutoRenamingTool" or "AutoRenamingTool") ||
                executable.Extension != "jar" || executable.Classifier is not (null or "all" or "shrunk" or "fatjar"))
                throw new NotSupportedException("Processor artifact is outside the supported official tool families.");
            if (processor.Outputs.Count is 0 or > 64) throw new NotSupportedException("Every supported client processor must declare SHA-1 outputs.");
            var outputs = new List<ForgeOutput>();
            foreach (var output in processor.Outputs)
            {
                var path = Expand(output.Key);
                if (!aliases.TryGetValue(path, out var coordinate) || available.Contains(coordinate) || !produced.Add(path))
                    throw new InvalidDataException("Output must uniquely produce an absent declared Maven artifact, without overwriting inputs.");
                var hash = Expand(output.Value);
                if (hash.StartsWith('\'') && hash.EndsWith('\'') && hash.Length >= 2) hash = hash[1..^1];
                if (hash.Length != 40 || !hash.All(Uri.IsHexDigit)) throw new NotSupportedException("Processor outputs require an explicit SHA-1.");
                if (libraries.TryGetValue(coordinate, out var library) && !string.IsNullOrEmpty(library.Sha1) && !hash.Equals(library.Sha1, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Conflicting generated artifact checksums.");
                outputs.Add(new(coordinate, path, hash));
            }
            var args = processor.Arguments.Select(Expand).ToArray();
            if (executable.Artifact == "installertools")
            {
                var tasks = args.Select((a, i) => (a, i)).Where(p => p.a == "--task").Select(p => p.i).ToArray();
                if (tasks.Length != 1 || tasks[0] + 1 >= args.Length || args[tasks[0] + 1] is not
                    ("MCP_DATA" or "SRG_TO_MCP" or "EXTRACT_INHERITANCE" or "CHAIN_MAPPINGS" or "MERGE_MAPPING" or "MERGE_MAPPINGS" or "EXTRACT_FILES" or "BUNDLER_EXTRACT"))
                    throw new NotSupportedException("Unknown/nonlocal installertools task. A specific verified adapter is required.");
            }
            // Reject filesystem literals and suffix traversal. Paths must originate in verified variables/coordinates.
            for (int i = 0; i < args.Length; i++)
            {
                var raw = processor.Arguments[i]; var arg = args[i];
                var literalPart = Regex.Replace(raw, @"\{[A-Z0-9_]+\}|\[[^\[\]]+\]", "TOKEN", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                if (arg.Contains("..", StringComparison.Ordinal) || arg.Contains("@", StringComparison.Ordinal) || arg.Contains("file:", StringComparison.OrdinalIgnoreCase) ||
                    literalPart.IndexOfAny([':', '/', '\\']) >= 0)
                    throw new NotSupportedException("Unsupported processor argument path or indirection.");
                foreach (var reference in Regex.Matches(raw, @"\[([^\[\]]+)\]|\{([A-Z0-9_]+)\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Cast<Match>())
                {
                    var value = Expand(reference.Value);
                    if (aliases.TryGetValue(value, out var coord) && !available.Contains(coord) && !outputs.Any(o => o.Coordinate == coord))
                        throw new InvalidDataException("Processor references a generated input before its producer.");
                }
                if (arg.Equals("DOWNLOAD_MOJMAPS", StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException("Processor network tasks require a separate verified downloader adapter.");
                // Any expanded absolute path must stay in this owned operation. No arbitrary user paths are accepted.
                if (Path.IsPathRooted(arg)) _ = SafePath.Join(stage, Path.GetRelativePath(stage, arg).Replace('\\', '/'));
            }
            var cpCoordinates = processor.Classpath.Prepend(processor.Jar).Distinct(StringComparer.Ordinal).ToArray();
            var cp = cpCoordinates.Select(c =>
                available.Contains(c) && libraries.TryGetValue(c, out var lib) && !lib.Generated ? paths[c] : throw new InvalidDataException("Unverified processor classpath.")).ToArray();
            foreach (var coordinate in cpCoordinates) ValidateClasspathJar(executableSources is null ? paths[coordinate] : executableSources[coordinate], stage);
            var main = MainClass(executableSources is null ? paths[processor.Jar] : executableSources[processor.Jar], stage);
            steps.Add(new(processor.Jar, main, cp, args, outputs));
            foreach (var o in outputs) available.Add(o.Coordinate);
        }
        if (libraries.Values.Any(l => l.Generated && !available.Contains(l.Coordinate))) throw new NotSupportedException("Generated libraries are not covered by explicit processor outputs.");
        return new(steps, paths);
    }
    private static string Literal(string value) => value.IndexOfAny(['{', '}', '[', ']', ':', '/', '\\']) < 0 && !value.Any(char.IsControl)
        ? value : throw new NotSupportedException("Unsupported processor literal.");

    private static string MainClass(string file, string stage)
    {
        using var zip = ZipFile.OpenRead(file); SafeArchive.Validate(zip, stage, 512L * 1024 * 1024, 30000);
        var entry = zip.GetEntry("META-INF/MANIFEST.MF") ?? throw new InvalidDataException("Processor manifest is missing.");
        if (entry.Length > 65536) throw new InvalidDataException("Processor manifest is too large.");
        var text = ReadManifest(entry).Replace("\r\n", "\n");
        var lines = text.Replace("\n ", "", StringComparison.Ordinal).Split("\n\n", 2)[0].Split('\n');
        var main = lines.Where(l => l.StartsWith("Main-Class:", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (main.Length != 1 || lines.Any(l => l.StartsWith("Class-Path:", StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException("Processor requires one explicit Main-Class and no implicit manifest classpath.");
        var name = main[0][11..].Trim();
        if (!Regex.IsMatch(name, @"^[A-Za-z_$][A-Za-z0-9_$.]*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) || zip.GetEntry(name.Replace('.', '/') + ".class") is null)
            throw new InvalidDataException("Processor main class is absent or invalid.");
        return name;
    }
    private static void ValidateClasspathJar(string file, string stage)
    {
        using var zip = ZipFile.OpenRead(file); SafeArchive.Validate(zip, stage, 512L * 1024 * 1024, 30000);
        if (zip.GetEntry("META-INF/MANIFEST.MF") is not { } manifest) return;
        if (manifest.Length > 65536) throw new InvalidDataException("Classpath manifest exceeds its size limit.");
        if (ReadManifest(manifest).Replace("\r\n", "\n").Replace("\n ", "", StringComparison.Ordinal).Split('\n')
            .Any(l => l.StartsWith("Class-Path:", StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException("Implicit processor dependency classpaths require an explicit adapter.");
    }
    private static string ReadManifest(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, true);
        var text = new StringBuilder(); var buffer = new char[2048]; int count;
        while ((count = reader.Read(buffer)) > 0)
        { if (text.Length + count > 65536) throw new InvalidDataException("Manifest expanded beyond its limit."); text.Append(buffer, 0, count); }
        return text.ToString();
    }
}
