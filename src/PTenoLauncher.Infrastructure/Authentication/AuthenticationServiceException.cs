using System.Net;

namespace PTenoLauncher.Infrastructure.Authentication;

/// <summary>Diagnostics contain only the fixed service host and HTTP code, never response bodies or tokens.</summary>
public sealed class AuthenticationServiceException(string host, HttpStatusCode statusCode, AuthenticationStage stage = AuthenticationStage.Unknown) : InvalidOperationException(
    $"Служба авторизації {host} повернула {(int)statusCode}. Перевірте реєстрацію застосунку, дозвіл Minecraft API та налаштування Xbox-акаунта.")
{
    public string Host { get; } = host;
    public HttpStatusCode StatusCode { get; } = statusCode;
    public AuthenticationStage Stage { get; } = stage;
    public string SafeDiagnostic => $"host={Host}; http={(int)StatusCode}; stage={Stage}";
    internal static AuthenticationStage IdentifyStage(Uri uri) => (uri.Host, uri.AbsolutePath) switch
    {
        ("user.auth.xboxlive.com", "/user/authenticate") => AuthenticationStage.XboxUser,
        ("xsts.auth.xboxlive.com", "/xsts/authorize") => AuthenticationStage.Xsts,
        ("api.minecraftservices.com", "/authentication/login_with_xbox") => AuthenticationStage.MinecraftLogin,
        ("api.minecraftservices.com", "/entitlements/mcstore") => AuthenticationStage.Entitlements,
        ("api.minecraftservices.com", "/minecraft/profile") => AuthenticationStage.MinecraftProfile,
        _ => AuthenticationStage.Unknown
    };
}

public enum AuthenticationStage { Unknown, XboxUser, Xsts, MinecraftLogin, Entitlements, MinecraftProfile }
