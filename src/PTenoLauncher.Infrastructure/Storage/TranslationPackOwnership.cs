using PTenoLauncher.Infrastructure.AI;
namespace PTenoLauncher.Infrastructure.Storage;

/// <summary>Canonical profile-owned generated packs; English source retains the legacy filename.</summary>
public static class TranslationPackOwnership
{
    public static string RelativePath(LauncherPaths paths, Guid profile, string source, string target)
    {
        TranslationRules.Locale(source); TranslationRules.Locale(target);
        return $"{paths.ProfileRelative(profile)}/game/resourcepacks/pteno-translations-{(source == "en_us" ? target : source + "-to-" + target)}.zip";
    }
    public static bool IsOwned(LauncherPaths paths, Guid profile, string relative)
    {
        var folder = paths.ProfileRelative(profile);
        return relative.StartsWith(folder + "/game/resourcepacks/", StringComparison.Ordinal) &&
            TranslationRules.Locales.Any(source => TranslationRules.Locales.Any(target => string.Equals(relative,
                $"{folder}/game/resourcepacks/pteno-translations-{(source == "en_us" ? target : source + "-to-" + target)}.zip",
                StringComparison.Ordinal)));
    }
}
