using System.Diagnostics;
using System.Text.Json;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Configuration;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Serialization;

namespace RetwhoConnector.Core.Services;

public sealed class PosHttpClient(
    HttpClient httpClient,
    IPosResponseReader responseReader,
    PosOptions options,
    IAgentLog agentLog,
    ILogSanitizer sanitizer) : IPosHttpClient
{
    public async Task<PosHttpResponse> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequestDiagnostic requestDiagnostic =
            CreateRequestDiagnostic(request);
        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage? response = null;
        try
        {
            response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            PosHttpResponse result = await responseReader.ReadAsync(
                response,
                options.MaximumResponseBytes,
                cancellationToken).ConfigureAwait(false);
            PosXmlFaultDetails? fault = null;
            PosXmlFaultInspector.TryInspect(
                result.Body,
                out fault);
            string classification = Classify(
                response.IsSuccessStatusCode,
                fault);
            WriteCompleted(
                requestDiagnostic,
                result,
                fault,
                classification,
                stopwatch.ElapsedMilliseconds);
            return result;
        }
        catch (OperationCanceledException exception)
        {
            WriteFailure(
                requestDiagnostic,
                response,
                "cancelled",
                stopwatch.ElapsedMilliseconds,
                exception);
            throw;
        }
        catch (Exception exception)
        {
            WriteFailure(
                requestDiagnostic,
                response,
                "transportError",
                stopwatch.ElapsedMilliseconds,
                exception);
            throw;
        }
        finally
        {
            response?.Dispose();
        }
    }

    private void WriteCompleted(
        RequestDiagnostic request,
        PosHttpResponse result,
        PosXmlFaultDetails? fault,
        string classification,
        long elapsedMilliseconds)
    {
        bool isFault = fault?.FaultCode is not null;
        AgentLogLevel level = classification == "success"
            ? AgentLogLevel.Information
            : AgentLogLevel.Warning;
        AgentLogCategory category = classification switch
        {
            "success" => AgentLogCategory.Success,
            "sessionExpired" => AgentLogCategory.Session,
            _ => AgentLogCategory.Error,
        };
        string message = isFault
            ? $"POS {request.Command} returned an XML fault in " +
              $"{elapsedMilliseconds} ms."
            : $"POS {request.Command} completed with HTTP " +
              $"{result.Metadata.StatusCode} in " +
              $"{elapsedMilliseconds} ms.";
        agentLog.TryWrite(
            level,
            category,
            message,
            SerializeDiagnostic(new OperationDiagnostic(
                request,
                CreateResponseDiagnostic(
                    result.Metadata,
                    result.Body.Length,
                    fault),
                classification,
                elapsedMilliseconds,
                null)));
    }

    private void WriteFailure(
        RequestDiagnostic request,
        HttpResponseMessage? response,
        string classification,
        long elapsedMilliseconds,
        Exception exception)
    {
        agentLog.TryWrite(
            exception is OperationCanceledException
                ? AgentLogLevel.Warning
                : AgentLogLevel.Error,
            AgentLogCategory.Error,
            exception is OperationCanceledException
                ? $"POS {request.Command} was cancelled after " +
                  $"{elapsedMilliseconds} ms."
                : $"POS {request.Command} failed after " +
                  $"{elapsedMilliseconds} ms.",
            SerializeDiagnostic(new OperationDiagnostic(
                request,
                response is null
                    ? null
                    : CreateResponseDiagnostic(response),
                classification,
                elapsedMilliseconds,
                exception.GetType().FullName)));
    }

    private RequestDiagnostic CreateRequestDiagnostic(
        HttpRequestMessage request)
    {
        string command = request.Options.TryGetValue(
            PosHttpRequestFactory.CommandKey,
            out string? value)
            ? value
            : "unknown";
        return new RequestDiagnostic(
            Normalize(command, 32) ?? "unknown",
            Normalize(request.Method.Method, 16) ?? "unknown",
            Normalize(request.Version.ToString(), 16) ?? "unknown",
            request.Content?.Headers.ContentLength,
            request.Options.TryGetValue(
                PosHttpRequestFactory.CertificatePinKey,
                out string? pin) &&
            !string.IsNullOrWhiteSpace(pin));
    }

    private ResponseDiagnostic CreateResponseDiagnostic(
        PosResponseMetadata metadata,
        int responseCharacters,
        PosXmlFaultDetails? fault) =>
        new(
            metadata.StatusCode,
            Normalize(metadata.ReasonPhrase, 256),
            Normalize(metadata.ContentType, 256),
            metadata.ContentLength,
            metadata.ContentEncodings
                .Select(value => Normalize(value, 64) ?? string.Empty)
                .ToArray(),
            metadata.Date,
            Normalize(metadata.Server, 256),
            Normalize(metadata.Connection, 256),
            metadata.RetryAfter,
            metadata.HasSetCookieHeader,
            metadata.HasWwwAuthenticateHeader,
            responseCharacters,
            Normalize(fault?.RootName, 128),
            Normalize(fault?.FaultCode, 512),
            Normalize(fault?.FaultString, 512),
            Normalize(fault?.Message, 1_024));

    private ResponseDiagnostic CreateResponseDiagnostic(
        HttpResponseMessage response) =>
        new(
            (int)response.StatusCode,
            Normalize(response.ReasonPhrase, 256),
            Normalize(
                response.Content.Headers.ContentType?.ToString(),
                256),
            response.Content.Headers.ContentLength,
            response.Content.Headers.ContentEncoding
                .Select(value => Normalize(value, 64) ?? string.Empty)
                .ToArray(),
            response.Headers.Date,
            Normalize(
                string.Join(" ", response.Headers.Server),
                256),
            Normalize(
                string.Join(", ", response.Headers.Connection),
                256),
            response.Headers.RetryAfter?.Delta,
            response.Headers.Contains("Set-Cookie"),
            response.Headers.WwwAuthenticate.Count > 0,
            null,
            null,
            null,
            null,
            null);

    private string SerializeDiagnostic(
        OperationDiagnostic diagnostic) =>
        JsonSerializer.Serialize(
            diagnostic,
            ConnectorJson.Options);

    private string? Normalize(string? value, int maximumCharacters)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string safe = sanitizer.Sanitize(value);
        string normalized = new(
            safe.Where(character =>
                !char.IsControl(character) ||
                character == '\t').ToArray());
        if (normalized.Length > maximumCharacters)
        {
            normalized = string.Concat(
                normalized.AsSpan(0, maximumCharacters - 1),
                "…");
        }

        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized;
    }

    private static string Classify(
        bool isHttpSuccess,
        PosXmlFaultDetails? fault)
    {
        if (fault?.IsLoginRequired == true)
        {
            return "sessionExpired";
        }

        if (fault?.FaultCode is not null)
        {
            return "xmlFault";
        }

        return isHttpSuccess ? "success" : "httpError";
    }

    private sealed record RequestDiagnostic(
        string Command,
        string Method,
        string Version,
        long? ContentLength,
        bool HasCertificatePin);

    private sealed record ResponseDiagnostic(
        int StatusCode,
        string? ReasonPhrase,
        string? ContentType,
        long? ContentLength,
        IReadOnlyList<string> ContentEncodings,
        DateTimeOffset? Date,
        string? Server,
        string? Connection,
        TimeSpan? RetryAfter,
        bool HasSetCookieHeader,
        bool HasWwwAuthenticateHeader,
        int? ResponseCharacters,
        string? RootName,
        string? FaultCode,
        string? FaultString,
        string? Message);

    private sealed record OperationDiagnostic(
        RequestDiagnostic Request,
        ResponseDiagnostic? Response,
        string Classification,
        long ElapsedMilliseconds,
        string? ExceptionType);
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
