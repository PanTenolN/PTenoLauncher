using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
namespace PTenoLauncher.Desktop.Localization;

public static partial class TextCatalog
{
    private static readonly Dictionary<string, Dictionary<string, string>> Additional = new(StringComparer.Ordinal);
    public static IReadOnlyCollection<string> Keys => new ReadOnlyCollection<string>(Texts.Keys.ToArray());
    public static IReadOnlyDictionary<string, string> Catalog(string language)
    {
        if (language is "uk" or "en") return new ReadOnlyDictionary<string, string>(Texts.ToDictionary(p => p.Key, p => language == "uk" ? p.Value.Uk : p.Value.En));
        return new ReadOnlyDictionary<string, string>(Additional[language]);
    }
    private static void LoadAdditionalLanguages()
    {
        foreach (var language in Languages.Where(l => l.Code is not ("uk" or "en")))
        {
            using var stream = typeof(TextCatalog).Assembly.GetManifestResourceStream($"PTenoLauncher.Localization.{language.Code}.json")
                ?? throw new InvalidDataException("Missing embedded locale: " + language.Code);
            var catalog = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? throw new InvalidDataException("Invalid locale: " + language.Code);
            ValidateCatalog(language.Code, catalog);
            Additional.Add(language.Code, catalog);
        }
    }
    public static void ValidateCatalog(string language, IReadOnlyDictionary<string, string> catalog)
    {
        if (catalog.Count != Texts.Count || Texts.Keys.Any(key => !catalog.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)))
            throw new InvalidDataException("Incomplete locale: " + language);
        foreach (var (key, value) in catalog)
        {
            try { _ = CompositeFormat.Parse(value); }
            catch (FormatException ex) { throw new InvalidDataException("Invalid format: " + language + "/" + key, ex); }
            var expected = Placeholders(Texts[key].En);
            if (!expected.SequenceEqual(Placeholders(value)))
                throw new InvalidDataException("Invalid placeholders: " + language + "/" + key);
        }
    }
    private static string[] Placeholders(string value) => Regex.Matches(value, @"(?<!\{)\{\d+(?:[^{}]*)\}(?!\})")
        .Select(match => match.Value).Order(StringComparer.Ordinal).ToArray();
}
