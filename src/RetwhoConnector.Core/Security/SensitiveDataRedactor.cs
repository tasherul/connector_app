namespace RetwhoConnector.Core.Security;

public static class SensitiveDataRedactor
{
    private static readonly LogSanitizer Sanitizer = new();

    public static string Redact(string? value) => Sanitizer.Sanitize(value);
}
