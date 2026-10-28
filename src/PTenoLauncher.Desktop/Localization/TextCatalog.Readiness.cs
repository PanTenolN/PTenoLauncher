using PTenoLauncher.Core;
namespace PTenoLauncher.Desktop.Localization;

public static partial class TextCatalog
{
    public static string RenderReadiness(ReadinessReport report)
    {
        if (Language != "en") return report.Summary;
        if (report.Ready) return $"Ready for offline play: {report.VerifiedFiles} Minecraft files and {report.VerifiedMods} mods verified.";
        return string.Join(Environment.NewLine, report.Issues.Select(issue => "• " + (issue.Code switch
        {
            "loader.unsupported" => "Installing and launching this mod loader is not implemented yet.",
            "manifest.corrupt" => "The local installation record is corrupt.",
            "game.missing" => "Minecraft is not installed for this profile yet.",
            "loader.mismatch" => "Install the selected mod loader or Vanilla for this profile.",
            "version.mismatch" => "The installed version differs from the profile settings.",
            "file.invalid" => "A required game file is missing or corrupt.",
            "java.missing" => "Select 64-bit Java for this profile.",
            "java.corrupt" => "The managed Java runtime is corrupt. Repair Java.",
            "java.arch" => "64-bit Java is required.",
            "java.version" => "The selected Java version does not match the game's required version.",
            "java.invalid" => "Java verification failed.",
            "account.missing" => "Sign in online first and verify Minecraft Java Edition ownership.",
            // Preserve original technical detail and stable codes for findings whose
            // translated presentation has not been reviewed; never invent a diagnosis.
            _ => $"Diagnostic [{issue.Code}]: {issue.Message}"
        })));
    }
}
