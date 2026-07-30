namespace RetwhoConnector.Core.Configuration;

public sealed class PosOptions
{
    public string NaxmlPath { get; init; } = "/cgi-bin/NAXML";
    public string ConfigClientPath { get; init; } = "/ConfigClient.html";
    public TimeSpan SetupRequestTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public int MaximumResponseBytes { get; init; } = 2 * 1024 * 1024;
}
