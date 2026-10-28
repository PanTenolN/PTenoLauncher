namespace PTenoLauncher.Core;

public sealed record ForgeModDependency(string Id, string Range, string Kind, string Ordering, string Side);
public sealed record ForgeModDeclaration(string Id, string? Version, string Name,
    IReadOnlyList<ForgeModDependency> Dependencies, IReadOnlyDictionary<string, string> Features);
public sealed record ForgeModMetadata(string LanguageLoader, string LanguageRange, IReadOnlyList<ForgeModDeclaration> Mods);
