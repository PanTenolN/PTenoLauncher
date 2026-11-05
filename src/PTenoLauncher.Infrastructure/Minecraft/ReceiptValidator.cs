using System.Text.RegularExpressions;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

public static class ReceiptValidator
{
    public static void Validate(LauncherPaths paths, InstallationReceipt receipt)
    {
        AssetIndexPlan.ValidateId(receipt.AssetIndex);
        if (receipt.Files is null || receipt.Files.Count is 0 or > 100000 || receipt.Classpath is null || receipt.Classpath.Count == 0 ||
            receipt.NativeArchives is null || receipt.JvmArguments is null || receipt.GameArguments is null ||
            receipt.JavaMajor is < 8 or > 100 || string.IsNullOrWhiteSpace(receipt.MainClass) ||
            !Regex.IsMatch(receipt.MainClass, @"^[A-Za-z_$][A-Za-z0-9_$.]*$", RegexOptions.None, TimeSpan.FromSeconds(1)))
            throw new InvalidDataException("Некоректний локальний опис встановлення.");
        if (!Enum.IsDefined(receipt.Loader))
            throw new InvalidDataException("Непідтримуваний Mod Loader.");
        if (receipt.Loader != ModLoader.Vanilla && (string.IsNullOrWhiteSpace(receipt.LoaderVersion) || receipt.LoaderVersion.Length > 128 ||
            !receipt.LoaderVersion.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '+' or '-' or '_') ||
            (receipt.Loader == ModLoader.Fabric ? receipt.MainClass is not ("net.fabricmc.loader.impl.launch.knot.KnotClient" or "net.fabricmc.loader.launch.knot.KnotClient") :
             receipt.Loader == ModLoader.Quilt ? receipt.MainClass != "org.quiltmc.loader.impl.launch.knot.KnotClient" :
             receipt.MainClass is not ("cpw.mods.bootstraplauncher.BootstrapLauncher" or "cpw.mods.modlauncher.Launcher"))))
            throw new InvalidDataException("Некоректні метадані Mod Loader.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in receipt.Files)
        {
            _ = SafePath.Join(paths.Cache, file.RelativePath);
            if (!names.Add(file.RelativePath) || file.Size is < 0 or > 2L * 1024 * 1024 * 1024 ||
                file.Hash is null || file.Algorithm is not ("SHA1" or "SHA256" or "SHA512") ||
                file.Hash.Length != (file.Algorithm == "SHA1" ? 40 : file.Algorithm == "SHA512" ? 128 : 64) || !file.Hash.All(Uri.IsHexDigit))
                throw new InvalidDataException("Некоректний запис перевірки цілісності.");
        }
        if (receipt.Loader == ModLoader.Fabric && !receipt.Classpath.Contains(
            $"libraries/fabric/net/fabricmc/fabric-loader/{receipt.LoaderVersion}/fabric-loader-{receipt.LoaderVersion}.jar", StringComparer.Ordinal))
            throw new InvalidDataException("Відсутній JAR вибраної версії Fabric.");
        if (receipt.Loader == ModLoader.Quilt && !receipt.Classpath.Contains(
            $"libraries/quilt/org/quiltmc/quilt-loader/{receipt.LoaderVersion}/quilt-loader-{receipt.LoaderVersion}.jar", StringComparer.Ordinal))
            throw new InvalidDataException("Відсутній JAR вибраної версії Quilt.");
        if (receipt.Classpath.Concat(receipt.NativeArchives).Any(p => !names.Contains(p)) ||
            !names.Contains($"assets/indexes/{receipt.AssetIndex}.json") ||
            receipt.JvmArguments.Concat(receipt.GameArguments).Any(a => a is null || a.Contains('\0')))
            throw new InvalidDataException("Опис встановлення неповний.");
        if (receipt.Loader is ModLoader.Forge or ModLoader.NeoForge)
        {
            var info = receipt.Forge ?? throw new InvalidDataException("Forge processor installation is incomplete.");
            if (info.Schema != 1 || !Guid.TryParseExact(info.OperationId, "N", out _) || info.GeneratedFiles is null || info.GeneratedFiles.Count is 0 or > 2048)
                throw new InvalidDataException("Invalid Forge installation inventory.");
            ForgeMaven.Segment(info.VersionId);
            var prefix = "forge/installations/" + info.OperationId + "/";
            if (info.Installer is null || !receipt.Files.Contains(info.Installer) || !info.Installer.RelativePath.StartsWith("forge/installers/", StringComparison.Ordinal) ||
                info.GeneratedFiles.Select(f => f.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != info.GeneratedFiles.Count ||
                info.GeneratedFiles.Any(f => !f.RelativePath.StartsWith(prefix + "libraries/", StringComparison.Ordinal) || !receipt.Files.Contains(f)))
                throw new InvalidDataException("Forge generated file/installer inventory is incomplete.");
            if (!receipt.Classpath.Any(p => p.StartsWith(prefix + "libraries/", StringComparison.Ordinal))) throw new InvalidDataException("Forge runtime classpath is absent.");
            if (info.FmlCoordinate is { } fml)
            {
                var c = ForgeCoordinate.Parse(fml);
                if (c.Classifier is not null || c.Extension != "jar" ||
                    (receipt.Loader == ModLoader.Forge ? c.Group != "net.minecraftforge" || c.Artifact != "fmlloader" : c.Group != "net.neoforged.fancymodloader" || c.Artifact != "loader") ||
                    !receipt.Classpath.Contains(prefix + "libraries/" + c.Path, StringComparer.Ordinal)) throw new InvalidDataException("Invalid independent FML metadata.");
            }
        }
        else if (receipt.Forge is not null) throw new InvalidDataException("Unexpected Forge metadata in another loader receipt.");
        if (receipt.VersionType is not ("release" or "snapshot" or "old_alpha" or "old_beta"))
            throw new InvalidDataException("Invalid Minecraft version type.");
        if (receipt.LegacyAssets is { } layout)
        {
            if ((!layout.Virtual && !layout.MapToResources) || layout.IndexSha1 is null || layout.IndexSha1.Length != 40 || !layout.IndexSha1.All(Uri.IsHexDigit))
                throw new InvalidDataException("Invalid legacy asset layout.");
            AssetIndexPlan.ValidateEntries(paths.Cache, layout.Entries);
            var index = receipt.Files.Single(f => f.RelativePath == $"assets/indexes/{receipt.AssetIndex}.json");
            if (index.Algorithm != "SHA1" || !index.Hash.Equals(layout.IndexSha1, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Legacy asset layout does not match its verified index.");
            var objects = receipt.Files.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);
            foreach (var asset in layout.Entries)
                if (!objects.TryGetValue(AssetManager.ObjectPath(asset), out var stored) || stored.Algorithm != "SHA1" ||
                    !stored.Hash.Equals(asset.Hash, StringComparison.OrdinalIgnoreCase) || stored.Size != asset.Size)
                    throw new InvalidDataException("Legacy asset is absent from the verified object inventory.");
        }
    }
}
