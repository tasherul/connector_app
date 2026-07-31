using System.Diagnostics;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Configuration;
using RetwhoConnector.Core.Models;

namespace RetwhoConnector.Core.Services;

public sealed class PosHttpClient(
    HttpClient httpClient,
    IPosResponseReader responseReader,
    PosOptions options,
    IAgentLog agentLog) : IPosHttpClient
{
    public async Task<PosHttpResponse> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string command = request.Options.TryGetValue(
            PosHttpRequestFactory.CommandKey,
            out string? value)
            ? value
            : "unknown";
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            PosHttpResponse result = await responseReader.ReadAsync(
                response,
                options.MaximumResponseBytes,
                cancellationToken).ConfigureAwait(false);
            agentLog.TryWrite(
                AgentLogLevel.Information,
                response.IsSuccessStatusCode
                    ? AgentLogCategory.Success
                    : AgentLogCategory.General,
                $"POS {command} completed with HTTP " +
                $"{(int)response.StatusCode} in " +
                $"{stopwatch.ElapsedMilliseconds} ms.");
            return result;
        }
        catch (OperationCanceledException)
        {
            agentLog.TryWrite(
                AgentLogLevel.Warning,
                AgentLogCategory.Error,
                $"POS {command} was cancelled after " +
                $"{stopwatch.ElapsedMilliseconds} ms.");
            throw;
        }
        catch (Exception exception)
        {
            agentLog.TryWrite(
                AgentLogLevel.Error,
                AgentLogCategory.Error,
                $"POS {command} failed after " +
                $"{stopwatch.ElapsedMilliseconds} ms.",
                exception.GetType().FullName);
            throw;
        }
    }
}

internal sealed class NullAgentLog : IAgentLog
{
    public static NullAgentLog Instance { get; } = new();

    public LogPipelineHealth CurrentHealth { get; } =
        new(LoggingHealthState.Stopped, 0, "Logging is disabled.");

    public event EventHandler<LogPipelineHealth>? HealthChanged
    {
        add { }
        remove { }
    }

    public bool TryWrite(
        AgentLogLevel level,
        AgentLogCategory category,
        string message,
        string? details = null,
        string? correlationId = null) => true;
}
