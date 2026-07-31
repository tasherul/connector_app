using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Security;

namespace RetwhoConnector.Tests;

public sealed class LogSanitizerTests
{
    [Theory]
    [InlineData(
        "cmd=validate&user=FAKE_USER&passwd=FAKE_PASSWORD",
        "cmd=validate&user=<redacted>&passwd=<redacted>")]
    [InlineData(
        """{"licenseKey":"FAKE-LICENSE-001","cookie":"FAKE_COOKIE"}""",
        """{"licenseKey":"<redacted>","cookie":"<redacted>"}""")]
    [InlineData(
        "<credential><cookie>FAKE_COOKIE</cookie></credential>",
        "<credential><cookie><redacted></cookie></credential>")]
    [InlineData(
        "Authorization: Bearer FAKE_TOKEN",
        "Authorization: <redacted>")]
    [InlineData(
        "encryptedPosPassword=RkFLRV9QQVNTV09SRA==",
        "encryptedPosPassword=<redacted>")]
    [InlineData(
        "license key: FAKE-LICENSE-001; username: FAKE_USER; password: FAKE_PASSWORD",
        "license key: <redacted>; username: <redacted>; password: <redacted>")]
    [InlineData(
        "https://FAKE_USER:FAKE_PASSWORD@pos.example.test/cgi-bin/NAXML",
        "https://<redacted>@pos.example.test/cgi-bin/NAXML")]
    public void Sanitize_RemovesSecretValuesCompletely(
        string input,
        string expected)
    {
        ILogSanitizer sanitizer = new LogSanitizer();

        string result = sanitizer.Sanitize(input);

        Assert.Equal(expected, result);
        Assert.DoesNotContain("FAKE_", result, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "RkFLRV9QQVNTV09SRA",
            result,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_DoesNotDamageSafeOperationalStatus()
    {
        const string message =
            "POS validate request completed with status 200 in 42 ms.";
        ILogSanitizer sanitizer = new LogSanitizer();

        string result = sanitizer.Sanitize(message);

        Assert.Equal(message, result);
    }

    [Fact]
    public void Sanitize_NullReturnsEmptyString()
    {
        ILogSanitizer sanitizer = new LogSanitizer();

        Assert.Equal(string.Empty, sanitizer.Sanitize(null));
    }

    [Fact]
    public void CompatibilityRedactor_UsesCompleteSanitizer()
    {
        const string input =
            """{"password":"FAKE_PASSWORD","cookie":"FAKE_COOKIE"}""";

        string result = SensitiveDataRedactor.Redact(input);

        Assert.DoesNotContain("FAKE_PASSWORD", result, StringComparison.Ordinal);
        Assert.DoesNotContain("FAKE_COOKIE", result, StringComparison.Ordinal);
    }
}
