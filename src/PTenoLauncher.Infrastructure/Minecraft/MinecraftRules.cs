using System.Text.Json;
using System.Text.RegularExpressions;
namespace PTenoLauncher.Infrastructure.Minecraft;

public static class MinecraftRules
{
    public static bool Allowed(JsonElement element)
    {
        if (!element.TryGetProperty("rules", out var rules)) return true;
        bool allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            bool match = true;
            if (rule.TryGetProperty("os", out var os))
            {
                if (os.TryGetProperty("name", out var name)) match &= name.GetString() == "windows";
                if (os.TryGetProperty("arch", out var arch)) match &= arch.GetString() is "x86_64" or "amd64";
                if (os.TryGetProperty("version", out var version))
                    match &= Regex.IsMatch(Environment.OSVersion.Version.ToString(), version.GetString()!, RegexOptions.None, TimeSpan.FromSeconds(1));
            }
            if (rule.TryGetProperty("features", out var features))
                foreach (var feature in features.EnumerateObject()) match &= !feature.Value.GetBoolean();
            if (match) allowed = rule.GetProperty("action").GetString() == "allow";
        }
        return allowed;
    }
    public static IReadOnlyList<string> Arguments(JsonElement args)
    {
        var list = new List<string>();
        foreach (var arg in args.EnumerateArray())
        {
            if (arg.ValueKind == JsonValueKind.String) list.Add(arg.GetString()!);
            else if (Allowed(arg))
            {
                var value = arg.GetProperty("value");
                if (value.ValueKind == JsonValueKind.Array) list.AddRange(value.EnumerateArray().Select(v => v.GetString()!));
                else list.Add(value.GetString()!);
            }
        }
        return list;
    }
}