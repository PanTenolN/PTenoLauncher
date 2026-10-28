using System.Text.RegularExpressions;
using PTenoLauncher.Core;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

public static class ReceiptValidator
{
    public static void Validate(LauncherPaths paths, InstallationReceipt receipt)
    {
        if (receipt.Files is null || receipt.Files.Count is 0 or > 100000 || receipt.Classpath is null || receipt.Classpath.Count == 0 ||
            receipt.NativeArchives is null || receipt.JvmArguments is null || receipt.GameArguments is null ||
            receipt.JavaMajor is < 8 or > 100 || string.IsNullOrWhiteSpace(receipt.MainClass) ||
            !Regex.IsMatch(receipt.MainClass, @"^[A-Za-z_$][A-Za-z0-9_$.]*$", RegexOptions.None, TimeSpan.FromSeconds(1)))
            throw new InvalidDataException("Некоректний локальний опис встановлення.");
        if (receipt.Loader is not (ModLoader.Vanilla or ModLoader.Fabric or ModLoader.Quilt))
            throw new InvalidDataException("Непідтримуваний Mod Loader.");
        if (receipt.Loader != ModLoader.Vanilla && (string.IsNullOrWhiteSpace(receipt.LoaderVersion) || receipt.LoaderVersion.Length > 128 ||
            !receipt.LoaderVersion.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '+' or '-' or '_') ||
            (receipt.Loader == ModLoader.Fabric ? receipt.MainClass is not ("net.fabricmc.loader.impl.launch.knot.KnotClient" or "net.fabricmc.loader.launch.knot.KnotClient") :
             receipt.MainClass != "org.quiltmc.loader.impl.launch.knot.KnotClient")))
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
    }
}