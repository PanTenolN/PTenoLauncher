using System.Text;
using PTenoLauncher.Infrastructure.Storage;

namespace PTenoLauncher.Infrastructure.Minecraft;

public sealed partial class VanillaServerManager
{
    private static async Task CheckProxyConfigurationAsync(string folder, LocalVanillaServer server, CancellationToken ct)
    {
        if (!server.HasPlugins) return;
        await CheckFileAsync("spigot.yml", ["settings", "bungeecord"]);
        await CheckFileAsync("config/paper-global.yml", ["proxies", "velocity", "enabled"], ["proxies", "proxy-protocol"]);
        await CheckFileAsync("paper.yml", ["settings", "velocity-support", "enabled"], ["settings", "use-proxy-protocol"]);

        async Task CheckFileAsync(string relative, params string[][] rules)
        {
            var path = SafePath.Join(folder, relative);
            if (!File.Exists(path)) return;
            try
            {
                var bytes = await AtomicFile.ReadBytesAsync(path, 1024 * 1024, ct);
                var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
                if (text.Any(ch => ch is '\u0085' or '\u2028' or '\u2029' or '\0'))
                    throw new InvalidDataException("ServerProxySettingsChanged");
                var lines = new List<(int Indent, string Text)>();
                using var reader = new StringReader(text);
                while (reader.ReadLine() is { } raw)
                {
                    var value = raw.TrimStart(' ');
                    if (!string.IsNullOrWhiteSpace(value) && value[0] != '#') lines.Add((raw.Length - value.Length, value));
                }
                if (lines.Count == 0 || lines is [(_, "{}")]) return;
                foreach (var rule in rules)
                    CheckDisabled(lines, 0, lines.Count, lines[0].Indent, rule, 0);
            }
            catch (Exception error) when (error is InvalidDataException or DecoderFallbackException)
            {
                throw new InvalidDataException("ServerProxySettingsChanged", error);
            }
        }
    }

    // Only the generated block-map syntax is accepted along protected paths. A merge, alias, quoted key,
    // duplicate mapping, or flow map in that part of a file must not bypass the loopback/authentication policy.
    private static void CheckDisabled(List<(int Indent, string Text)> lines, int start, int end, int indent,
        string[] rule, int level)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var i = start; i < end; i++)
        {
            var line = lines[i];
            if (line.Indent > indent) continue;
            var split = line.Text.IndexOf(':');
            if (line.Indent != indent || split < 1 ||
                !line.Text[..split].All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_') ||
                !keys.Add(line.Text[..split]))
                throw new InvalidDataException("ServerProxySettingsChanged");
            if (line.Text[..split] != rule[level]) continue;
            var value = line.Text[(split + 1)..].Trim();
            var comment = value.IndexOf('#');
            if (comment == 0 || comment > 0 && char.IsWhiteSpace(value[comment - 1])) value = value[..comment].TrimEnd();
            if (level == rule.Length - 1)
            {
                if (value != "false") throw new InvalidDataException("ServerProxySettingsChanged");
                continue;
            }
            if (value == "{}") continue;
            if (value.Length != 0) throw new InvalidDataException("ServerProxySettingsChanged");
            var child = i + 1;
            while (child < end && lines[child].Indent > indent) child++;
            if (child > i + 1) CheckDisabled(lines, i + 1, child, lines[i + 1].Indent, rule, level + 1);
        }
    }
}
