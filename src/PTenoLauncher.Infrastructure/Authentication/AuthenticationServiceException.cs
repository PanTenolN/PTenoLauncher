using System.Net;

namespace PTenoLauncher.Infrastructure.Authentication;

/// <summary>Diagnostics contain only the fixed service host and HTTP code, never response bodies or tokens.</summary>
internal sealed class AuthenticationServiceException(string host, HttpStatusCode statusCode) : InvalidOperationException(
    $"Служба авторизації {host} повернула {(int)statusCode}. Перевірте реєстрацію застосунку, дозвіл Minecraft API та налаштування Xbox-акаунта.")
{
    public string Host { get; } = host;
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string SafeDiagnostic => $"host={Host}; http={(int)StatusCode}";
}
