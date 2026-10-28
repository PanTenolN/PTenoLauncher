using PTenoLauncher.Core;
namespace PTenoLauncher.Desktop.Localization;
public static partial class TextCatalog
{
    public static string RenderReadiness(ReadinessReport report)
    {
        if (Language == "uk") return report.Summary;
        if (report.Ready) return Format("ReadinessReady", report.VerifiedFiles, report.VerifiedMods);
        return string.Join(Environment.NewLine, report.Issues.Select(issue =>
            "• " + (Texts.ContainsKey("Readiness." + issue.Code)
                ? Get("Readiness." + issue.Code)
                : Format("ReadinessDiagnostic", issue.Code, issue.Message))));
    }
}
