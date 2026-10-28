using System.Text;
namespace PTenoLauncher.Infrastructure.Minecraft;
public static class LegacyArguments
{
    public static IReadOnlyList<string> Split(string arguments)
    {
        var result = new List<string>(); var current = new StringBuilder(); bool quoted = false;
        foreach (var c in arguments)
        {
            if (c == '"') quoted = !quoted;
            else if (char.IsWhiteSpace(c) && !quoted) { if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); } }
            else current.Append(c);
        }
        if (quoted) throw new InvalidDataException("Незавершені лапки в legacy arguments.");
        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }
}