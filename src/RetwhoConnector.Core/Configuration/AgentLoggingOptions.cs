namespace RetwhoConnector.Core.Configuration;

public sealed class AgentLoggingOptions
{
    public int IngressCapacity { get; init; } = 4_096;
    public int SinkCapacity { get; init; } = 4_096;
    public int MaximumMessageCharacters { get; init; } = 4_096;
    public int MaximumDetailsCharacters { get; init; } = 32_768;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(IngressCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(SinkCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            MaximumMessageCharacters,
            2);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            MaximumDetailsCharacters,
            2);
    }
}
