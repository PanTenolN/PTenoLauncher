using PTenoLauncher.Core;

namespace PTenoLauncher.Desktop.ViewModels;

internal enum ContentSupport { Unsupported, Unknown, Supported }

internal static class ProfileContentCompatibility
{
    // Exact Minecraft release pairs from the official Iris and Oculus Modrinth projects.
    // New releases and snapshots stay unknown until a matching component is confirmed.
    private static readonly HashSet<string> FabricIris =
    [
        "1.16.5", "1.17", "1.17.1", "1.18", "1.18.1", "1.18.2",
        "1.19", "1.19.1", "1.19.2", "1.19.3", "1.19.4",
        "1.20", "1.20.1", "1.20.2", "1.20.3", "1.20.4", "1.20.5", "1.20.6",
        "1.21", "1.21.1", "1.21.2", "1.21.3", "1.21.4", "1.21.5", "1.21.6", "1.21.7", "1.21.8", "1.21.9", "1.21.10", "1.21.11",
        "26.1", "26.1.1", "26.1.2", "26.2", "26.3"
    ];
    private static readonly HashSet<string> QuiltIris =
    [
        "1.16.5", "1.17.1", "1.18.2", "1.19", "1.19.1", "1.19.2", "1.19.3", "1.19.4",
        "1.20", "1.20.1", "1.20.2", "1.20.3", "1.20.4", "1.20.5", "1.20.6",
        "1.21", "1.21.1", "1.21.2", "1.21.3", "1.21.4", "1.21.5", "1.21.6", "1.21.7", "1.21.8", "1.21.9", "1.21.10", "1.21.11"
    ];
    private static readonly HashSet<string> NeoForgeIris =
    [
        "1.21", "1.21.1", "1.21.2", "1.21.3", "1.21.4", "1.21.5", "1.21.6", "1.21.7", "1.21.8", "1.21.10", "1.21.11",
        "26.1", "26.1.1", "26.1.2", "26.2", "26.3"
    ];
    private static readonly HashSet<string> ForgeOculus =
        ["1.16.5", "1.18.2", "1.19", "1.19.1", "1.19.2", "1.19.4", "1.20", "1.20.1"];
    private static readonly HashSet<string> NeoForgeWithoutShaderComponent =
        ["1.20.5", "1.20.6"];

    private static readonly Version FabricMinimum = new(1, 14, 0);
    private static readonly Version NeoForgeMinimum = new(1, 20, 1);
    private static readonly Version ResourcePackMinimum = new(1, 6, 1);

    internal static ContentSupport Mods(GameProfile? profile)
    {
        if (profile is null || profile.Loader == ModLoader.Vanilla) return ContentSupport.Unsupported;
        var release = Release(profile.MinecraftVersion);
        if (release is null) return ContentSupport.Unknown;
        return (profile.Loader == ModLoader.Fabric && release < FabricMinimum ||
                profile.Loader == ModLoader.NeoForge && release < NeoForgeMinimum)
            ? ContentSupport.Unsupported : ContentSupport.Supported;
    }

    internal static ContentSupport Shaders(GameProfile? profile)
    {
        if (profile is null || Mods(profile) == ContentSupport.Unsupported) return ContentSupport.Unsupported;
        if (ShaderComponent(profile) is not null) return ContentSupport.Supported;
        // Neither managed component publishes a NeoForge build for these two Minecraft releases.
        return profile.Loader == ModLoader.NeoForge && NeoForgeWithoutShaderComponent.Contains(profile.MinecraftVersion)
            ? ContentSupport.Unsupported : ContentSupport.Unknown;
    }

    internal static string? ShaderComponent(GameProfile? profile) => profile switch
    {
        { Loader: ModLoader.Fabric, MinecraftVersion: var version } when FabricIris.Contains(version) => "Iris",
        { Loader: ModLoader.Quilt, MinecraftVersion: var version } when QuiltIris.Contains(version) => "Iris",
        { Loader: ModLoader.NeoForge, MinecraftVersion: var version } when NeoForgeIris.Contains(version) => "Iris",
        { Loader: ModLoader.NeoForge, MinecraftVersion: "1.20.1" } => "Oculus",
        { Loader: ModLoader.Forge, MinecraftVersion: var version } when ForgeOculus.Contains(version) => "Oculus",
        _ => null
    };

    internal static ContentSupport ResourcePacks(GameProfile? profile) => profile is null ? ContentSupport.Unsupported
        : Release(profile.MinecraftVersion) is not { } release ? ContentSupport.Unknown
        : release < ResourcePackMinimum ? ContentSupport.Unsupported : ContentSupport.Supported;

    private static Version? Release(string id) =>
        Version.TryParse(id, out var release) && release.Revision < 0 && release.ToString() == id
            ? new(release.Major, release.Minor, Math.Max(0, release.Build))
            : null;
}
