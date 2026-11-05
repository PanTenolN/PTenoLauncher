using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PTenoLauncher.Infrastructure.Diagnostics;

internal sealed class MinecraftLogEvents
{
    internal const int EventLimit = 128 * 1024;
    private const int LineLimit = 512;
    private readonly StringBuilder pending = new();
    private int pendingLines;
    private static readonly XNamespace Log4j = "http://logging.apache.org/log4j/2.0/events";

    public IEnumerable<string> Accept(string line)
    {
        var beginsEvent = line.TrimStart().StartsWith("<log4j:Event", StringComparison.Ordinal);
        if (pending.Length != 0 && (beginsEvent || pendingLines >= LineLimit || pending.Length + line.Length + 1 > EventLimit))
            foreach (var previous in Finish()) yield return previous;
        if (pending.Length == 0 && !beginsEvent)
        {
            yield return line;
            yield break;
        }
        if (line.Length > EventLimit)
        {
            yield return line;
            yield break;
        }
        if (pending.Length != 0) pending.Append('\n');
        pending.Append(line);
        pendingLines++;
        if (!line.TrimEnd().EndsWith("</log4j:Event>", StringComparison.Ordinal)) yield break;

        var xml = TakePending();
        var parsed = Parse(xml);
        foreach (var result in (parsed ?? xml).Split('\n')) yield return result;
    }

    public string[] Finish() => pending.Length == 0 ? [] : TakePending().Split('\n');

    private string TakePending()
    {
        var text = pending.ToString();
        pending.Clear();
        pendingLines = 0;
        return text;
    }

    private static string? Parse(string xml)
    {
        try
        {
            // Minecraft commonly emits a prefixed fragment without a namespace declaration.
            using var input = new StringReader("<root xmlns:log4j=\"" + Log4j + "\">" + xml + "</root>");
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = EventLimit + 1024
            });
            var entry = XElement.Load(reader, LoadOptions.PreserveWhitespace).Elements().SingleOrDefault();
            if (entry is null || entry.Name != Log4j + "Event" || entry.Element(Log4j + "Message") is not { } message)
                return null;
            var level = (string?)entry.Attribute("level");
            if (level is not ("TRACE" or "DEBUG" or "INFO" or "WARN" or "ERROR" or "FATAL")) return null;
            var time = long.TryParse((string?)entry.Attribute("timestamp"), NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds)
                ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)
                : DateTimeOffset.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            var thread = (string?)entry.Attribute("thread") ?? "log4j";
            thread = string.Concat(thread.Where(c => !char.IsControl(c) && c != ']').Take(128));
            var throwable = entry.Element(Log4j + "Throwable")?.Value;
            var text = throwable is { Length: > 0 } ? message.Value + "\n" + throwable : message.Value;
            var lines = text.Split('\n');
            for (var i = 1; i < lines.Length; i++) lines[i] = $"[{level}] {lines[i]}";
            return $"[{time}] [{thread}/{level}]: {string.Join('\n', lines)}";
        }
        catch (Exception ex) when (ex is XmlException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            return null;
        }
    }
}
