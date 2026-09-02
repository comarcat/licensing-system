namespace LicensingCore.Configuration;

/// <summary>
/// Boot-time guard for the <c>LicensingDb</c> connection string. Both web apps
/// funnel the configured value through <see cref="Require"/> so a missing or blank
/// connection string fails loudly at startup instead of surfacing later as an
/// opaque database error.
/// </summary>
public static class ConnectionStringGuard
{
    /// <summary>
    /// Validates that a connection string value is present.
    /// </summary>
    /// <param name="value">The configured connection string, or <c>null</c>.</param>
    /// <returns>The exact same <paramref name="value"/> when it is non-blank.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="value"/> is <c>null</c>, empty or whitespace only.
    /// The message names both the <c>dotnet user-secrets</c> command and the
    /// <c>ConnectionStrings__LicensingDb</c> environment variable.
    /// </exception>
    public static string Require(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                "La cadena de conexión 'LicensingDb' no está configurada. " +
                "Defínela con 'dotnet user-secrets set ConnectionStrings:LicensingDb \"...\"' " +
                "o con la variable de entorno ConnectionStrings__LicensingDb.");
        }

        return value;
    }
}
