using Microsoft.Extensions.Logging;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Models;

namespace RetwhoConnector.Core.Services;

public sealed class ChannelLoggerProvider(IAgentLog agentLog) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) =>
        new ChannelLogger(agentLog);

    public void Dispose()
    {
    }

    private sealed class ChannelLogger(IAgentLog log) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message))
            {
                message = exception?.GetType().Name ?? "Log event.";
            }

            log.TryWrite(
                MapLevel(logLevel),
                logLevel >= LogLevel.Error
                    ? AgentLogCategory.Error
                    : AgentLogCategory.General,
                message,
                exception?.ToString());
        }

        private static AgentLogLevel MapLevel(LogLevel level) =>
            level switch
            {
                LogLevel.Trace => AgentLogLevel.Trace,
                LogLevel.Debug => AgentLogLevel.Debug,
                LogLevel.Information => AgentLogLevel.Information,
                LogLevel.Warning => AgentLogLevel.Warning,
                LogLevel.Error => AgentLogLevel.Error,
                LogLevel.Critical => AgentLogLevel.Critical,
                _ => AgentLogLevel.Information,
            };
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
