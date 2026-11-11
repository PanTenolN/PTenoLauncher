using System.Text.Json;
using PTenoLauncher.Infrastructure.Minecraft;
using PTenoLauncher.Infrastructure.Modpacks;
using PTenoLauncher.Infrastructure.Mods;

namespace PTenoLauncher.Infrastructure.Storage;

public sealed partial class ProfileFileRenamer
{
    private string ReferencePath(Guid profile, string kind) => kind switch
    {
        "pins" => SafePath.Join(paths.Profile(profile), "mod-pins.json"),
        "working" => SafePath.Join(paths.Profile(profile), "working-configuration.json"),
        "import" => SafePath.Join(paths.Profile(profile), MrpackImporter.ReceiptName),
        "options" => SafePath.Join(paths.Game(profile), "options.txt"),
        _ => throw new IOException("ProfileNameRecovery")
    };

    private static int ReferenceLimit(string kind) => (kind == "working" ? 8 : 2) * 1024 * 1024;

    private async Task<IReadOnlyList<Reference>> PreviewReferencesAsync(Journal journal, string category, CancellationToken ct)
    {
        string[] kinds = category switch
        {
            "mods" => ["pins", "working", "import"],
            "resourcepacks" => ["options", "import"],
            _ => ["import"]
        };
        var references = new List<Reference>();
        foreach (var kind in kinds)
        {
            var path = ReferencePath(journal.Profile, kind);
            if (!File.Exists(path)) continue;
            var reference = new Reference(kind, await AtomicFile.ReadBytesAsync(path, ReferenceLimit(kind), ct));
            if (!ReferenceEquals(Rebase(journal, reference), reference.Before)) references.Add(reference);
        }
        return references;
    }

    private static byte[] Rebase(Journal journal, Reference reference)
    {
        T Parse<T>() => JsonSerializer.Deserialize<T>(reference.Before, AtomicFile.JsonOptions)
            ?? throw new IOException("ProfileNameReferenced");
        static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, AtomicFile.JsonOptions);
        var from = journal.From;
        var to = journal.To;
        void Check(string sha256, string? sha512, long size)
        {
            if (!Same(sha256, journal.Hash) || sha512 is not null && !Same(sha512, journal.Sha512!) || size != journal.Size)
                throw new IOException("ProfileNameReferenced");
        }
        static void Unique(IEnumerable<string> names)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (names.Any(name => !seen.Add(name))) throw new IOException("ProfileNameReferenced");
        }
        switch (reference.Kind)
        {
            case "options":
                return ProfileContent.RebasePackOptions(reference.Before, Path.GetFileName(from), Path.GetFileName(to));
            case "pins":
            {
                var pins = Parse<ModPinSet>();
                if (pins.ProfileId != journal.Profile || pins.Schema != 1 || pins.Pins is null) throw new IOException("ProfileNameReferenced");
                if (!pins.Pins.Any(pin => Same(pin.File.Path, EnabledJar(from)))) return reference.Before;
                var next = pins.Pins.Select(pin =>
                {
                    if (!Same(pin.File.Path, EnabledJar(from))) return pin;
                    Check(pin.File.Sha256, pin.File.Sha512, pin.File.Size);
                    return pin with { File = pin.File with { Path = EnabledJar(to) } };
                }).ToArray();
                Unique(next.Select(pin => pin.File.Path));
                return Serialize(pins with { Pins = next });
            }
            case "working":
            {
                var working = Parse<WorkingConfiguration>();
                if (working.ProfileId != journal.Profile || working.Schema != 1 || working.Mods is null) throw new IOException("ProfileNameReferenced");
                if (!working.Mods.Any(mod => Same(EnabledJar(mod.File.Path), EnabledJar(from)))) return reference.Before;
                var next = working.Mods.Select(mod =>
                {
                    if (!Same(EnabledJar(mod.File.Path), EnabledJar(from))) return mod;
                    Check(mod.File.Sha256, mod.File.Sha512, mod.File.Size);
                    var name = EnabledJar(to) + (mod.Enabled ? "" : ".disabled");
                    return mod with { File = mod.File with { Path = name } };
                }).OrderBy(mod => mod.File.Path, StringComparer.Ordinal).ToArray();
                Unique(next.Select(mod => mod.File.Path));
                return Serialize(working with { Mods = next });
            }
            case "import":
            {
                var receipt = Parse<MrpackImportReceipt>();
                if (receipt.Id != journal.Profile || receipt.Schema != 1 || receipt.Files is null) throw new IOException("ProfileNameReferenced");
                if (!receipt.Files.Any(file => Same(file.RelativePath, from))) return reference.Before;
                var next = receipt.Files.Select(file =>
                {
                    if (!Same(file.RelativePath, from)) return file;
                    if (file.Algorithm != "SHA256") throw new IOException("ProfileNameReferenced");
                    Check(file.Hash, null, file.Size);
                    return file with { RelativePath = to };
                }).OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToArray();
                Unique(next.Select(file => file.RelativePath));
                return Serialize(receipt with { Files = next });
            }
            default: throw new IOException("ProfileNameRecovery");
        }
    }

    private static void ValidateReferences(Journal journal, string category)
    {
        if (journal.References is not { } references) return; // Older recovery records carry only the inventory.
        if (journal.Sha512 is not { Length: 128 } digest || !digest.All(Uri.IsHexDigit) || references.Count > 4 ||
            references.Select(reference => reference.Kind).Distinct(StringComparer.Ordinal).Count() != references.Count)
            throw new IOException("ProfileNameRecovery");
        foreach (var reference in references)
        {
            if (reference.Before is null || reference.Before.Length > ReferenceLimit(reference.Kind) ||
                !(reference.Kind == "import" || category == "mods" && reference.Kind is "pins" or "working" ||
                category == "resourcepacks" && reference.Kind == "options") ||
                ReferenceEquals(Rebase(journal, reference), reference.Before)) throw new IOException("ProfileNameRecovery");
        }
    }

    private async Task<List<(string Path, byte[] Bytes)>> CheckReferencesAsync(Journal journal, bool rollback)
    {
        var changes = new List<(string Path, byte[] Bytes)>();
        foreach (var reference in journal.References ?? [])
        {
            var path = ReferencePath(journal.Profile, reference.Kind);
            var after = Rebase(journal, reference);
            var current = await AtomicFile.ReadBytesAsync(path, ReferenceLimit(reference.Kind));
            var original = current.AsSpan().SequenceEqual(reference.Before);
            if (!original && (!rollback || !current.AsSpan().SequenceEqual(after))) throw new IOException("ProfileNameRecovery");
            if (!rollback || !original) changes.Add((path, rollback ? reference.Before : after));
        }
        return changes;
    }
}
