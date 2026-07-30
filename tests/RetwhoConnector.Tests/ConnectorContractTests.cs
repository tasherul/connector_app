using System.Text.Json;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Serialization;
using RetwhoConnector.Core.Validation;

namespace RetwhoConnector.Tests;

public sealed class ConnectorContractTests
{
    [Fact]
    public void SettingsValidation_NormalizesHttpsOrigin()
    {
        ConnectorSettings input = CreateSettings("https://pos.example.test:443/");

        ConnectorSettings result = ConnectorSettingsValidator.Validate(input);

        Assert.Equal("https://pos.example.test", result.PosBaseUrl);
    }

    [Theory]
    [InlineData("http://pos.example.test")]
    [InlineData("https://user@pos.example.test")]
    [InlineData("https://pos.example.test/path")]
    [InlineData("https://pos.example.test?query=value")]
    [InlineData("https://pos.example.test/#fragment")]
    public void SettingsValidation_RejectsUnsafePosUrls(string posBaseUrl)
    {
        Assert.Throws<ArgumentException>(
            () => ConnectorSettingsValidator.Validate(CreateSettings(posBaseUrl)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("contains space")]
    [InlineData("bad/slash")]
    public void SettingsValidation_RejectsInvalidLicense(string licenseKey)
    {
        ConnectorSettings input = CreateSettings() with { LicenseKey = licenseKey };

        Assert.Throws<ArgumentException>(
            () => ConnectorSettingsValidator.Validate(input));
    }

    [Fact]
    public void Acknowledgement_UsesCamelCaseAndOmitsNulls()
    {
        BridgeAcknowledgement acknowledgement =
            BridgeAcknowledgement.Success(new { Value = 42 });

        string json = JsonSerializer.Serialize(
            acknowledgement,
            ConnectorJson.Options);

        Assert.Equal("""{"ok":true,"result":{"value":42}}""", json);
    }

    [Fact]
    public void ActionValidation_RequiresObjectParams()
    {
        using JsonDocument document = JsonDocument.Parse("[]");
        var action = new BridgeAction
        {
            ActionId = "action-1",
            Command = "get_current_data",
            Params = document.RootElement.Clone(),
            Timestamp = DateTimeOffset.UtcNow,
        };

        Assert.Throws<ArgumentException>(
            () => BridgeActionValidator.Validate(action));
    }

    [Fact]
    public void ActionValidation_BoundsActionId()
    {
        using JsonDocument document = JsonDocument.Parse("{}");
        var action = new BridgeAction
        {
            ActionId = new string('a', 129),
            Command = "get_current_data",
            Params = document.RootElement.Clone(),
            Timestamp = DateTimeOffset.UtcNow,
        };

        Assert.Throws<ArgumentException>(
            () => BridgeActionValidator.Validate(action));
    }

    [Fact]
    public async Task ActionContext_AcknowledgesExactlyOnce()
    {
        using JsonDocument document = JsonDocument.Parse("{}");
        var action = new BridgeAction
        {
            ActionId = "action-1",
            Command = "get_current_data",
            Params = document.RootElement.Clone(),
            Timestamp = DateTimeOffset.UtcNow,
        };
        var calls = 0;
        var context = new BridgeActionContext(
            action,
            (_, _) =>
            {
                calls++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await context.AcknowledgeOnceAsync(
            BridgeAcknowledgement.Success(new { value = 1 }));
        await context.AcknowledgeOnceAsync(
            BridgeAcknowledgement.Failure("must not be sent"));

        Assert.Equal(1, calls);
        Assert.True(context.IsAcknowledged);
    }

    private static ConnectorSettings CreateSettings(
        string posBaseUrl = "https://pos.example.test") =>
        new()
        {
            PosBaseUrl = posBaseUrl,
            PosUsername = "FAKE_USER",
            PosPassword = "FAKE_PASSWORD",
            LicenseKey = "FAKE-LICENSE-001",
            AutoConnect = true,
        };
}
