namespace PTenoLauncher.Core;

public static class ProfileRules
{
    public static void Validate(GameProfile profile)
    {
        if (profile.Id == Guid.Empty) throw new ArgumentException("Порожній ідентифікатор профілю.");
        if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 80 || profile.Name.Any(char.IsControl))
            throw new ArgumentException("Назва має містити від 1 до 80 символів без керівних знаків.");
        if (string.IsNullOrWhiteSpace(profile.MinecraftVersion) || profile.MinecraftVersion.Length > 100 ||
            profile.MinecraftVersion.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-' and not '_'))
            throw new ArgumentException("Некоректний ідентифікатор версії Minecraft.");
        if (profile.MemoryMb is < 512 or > 65536 || profile.MemoryMb % 256 != 0)
            throw new ArgumentException("RAM: 512–65536 MB, крок 256 MB.");
        if (!Enum.IsDefined(profile.Loader)) throw new ArgumentException("Невідомий завантажувач модів.");
    }
}