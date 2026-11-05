using System.Text;
namespace PTenoLauncher.Infrastructure.Minecraft;
public static class LegacyArguments
{
    public static IReadOnlyList<string> Split(string arguments)
    {
        if (arguments is null || arguments.Length > 65536 || arguments.Contains('\0')) throw new InvalidDataException("Invalid legacy arguments.");
        var result = new List<string>(); var current = new StringBuilder(); bool quoted = false, started = false;
        foreach (var c in arguments)
        {
            if (c == '"') { quoted = !quoted; started = true; }
            else if (char.IsWhiteSpace(c) && !quoted) { if (started) { result.Add(current.ToString()); current.Clear(); started = false; } }
            else { current.Append(c); started = true; }
        }
        if (quoted) throw new InvalidDataException("Незавершені лапки в legacy arguments.");
        if (started) result.Add(current.ToString());
        return result;
    }
}
