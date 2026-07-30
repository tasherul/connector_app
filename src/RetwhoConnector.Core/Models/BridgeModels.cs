using System.Text.Json;
using System.Text.Json.Serialization;

namespace RetwhoConnector.Core.Models;

public sealed record BridgeAction
{
    public required string ActionId { get; init; }
    public required string Command { get; init; }
    public JsonElement Params { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}

public sealed record BridgeAcknowledgement
{
    public required bool Ok { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Result { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }

    public static BridgeAcknowledgement Success(object result) =>
        new() { Ok = true, Result = result };

    public static BridgeAcknowledgement Failure(string error) =>
        new() { Ok = false, Error = error };
}

public sealed record BridgeEnvelope<TData>
{
    public required bool Ok { get; init; }
    public required string Code { get; init; }
    public string? Message { get; init; }
    public TData? Data { get; init; }
}

public sealed record RegistrationResponse
{
    public required string Room { get; init; }
    public required string ClientType { get; init; }
}

public sealed record AgentDataPushResponse
{
    public required long LogId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}
