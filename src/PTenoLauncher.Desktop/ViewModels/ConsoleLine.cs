using System.Text.RegularExpressions;

namespace PTenoLauncher.Desktop.ViewModels;

public enum ConsoleLevel { Normal, Verbose, Warning, Error }
public sealed record ConsoleLine(string Text, ConsoleLevel Level)
{
    public bool Warning => Level == ConsoleLevel.Warning;
    public bool Error => Level == ConsoleLevel.Error;
    public bool Verbose => Level == ConsoleLevel.Verbose;
}

internal sealed partial class ConsoleLineClassifier
{
    private ConsoleLevel previous;

    [GeneratedRegex(@"^\d\d:\d\d:\d\d \[(?:out|err)\] ")]
    private static partial Regex RunnerPrefix();

    [GeneratedRegex(@"^(?:\[(?:\d{4}-\d\d-\d\d[ T])?\d\d:\d\d:\d\d(?:[.,]\d+)?\]\s+\[[^\r\n\]]+/(?<level>TRACE|DEBUG|INFO|WARN|ERROR|FATAL)\]\s*(?:\[[^\]]+\]\s*)?:|\d{4}-\d\d-\d\d[ T]\d\d:\d\d:\d\d(?:[.,]\d+)?\s+\[[^\]]+\]\s+(?<level>TRACE|DEBUG|INFO|WARN|ERROR|FATAL)\s+\S+\s+-\s|\[(?<level>TRACE|DEBUG|INFO|WARN|ERROR|FATAL)\] )")]
    private static partial Regex LogHeader();

    [GeneratedRegex(@"^(?:\s+at\s+\S+\(.*\)|\s*Caused by:\s+\S|\s*Suppressed:\s+\S|\s+\.\.\. \d+ (?:more|common frames omitted)|(?:[\w$]+\.)*[\w$]*(?:Exception|Error|Throwable)(?::|$))")]
    private static partial Regex StackLine();

    public ConsoleLine Parse(string line)
    {
        var content = RunnerPrefix().Replace(line, "", 1);
        var header = LogHeader().Match(content);
        previous = header.Success ? header.Groups["level"].Value switch
        {
            "TRACE" or "DEBUG" => ConsoleLevel.Verbose,
            "WARN" => ConsoleLevel.Warning,
            "ERROR" or "FATAL" => ConsoleLevel.Error,
            _ => ConsoleLevel.Normal
        } : previous == ConsoleLevel.Error && StackLine().IsMatch(content) ? ConsoleLevel.Error : ConsoleLevel.Normal;
        return new(line, previous);
    }
}
