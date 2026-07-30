namespace RetwhoConnector.Core.Configuration;

public sealed class BridgeOptions
{
    public Uri Url { get; init; } = new("https://connector.retwho.com");
    public string Path { get; init; } = "/socket.io";
    public TimeSpan RegistrationTimeout { get; init; } = TimeSpan.FromSeconds(8);
    public TimeSpan ActionAcknowledgementTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan CommandDeadline { get; init; } = TimeSpan.FromSeconds(8);
    public int MaximumPayloadBytes { get; init; } = 1024 * 1024;
}
