using RetwhoConnector.Core.Models;

namespace RetwhoConnector.App.ViewModels;

public enum DashboardSignal
{
    Healthy,
    Warning,
    Error,
}

public sealed record DashboardStatusItem(
    string Title,
    string Status,
    string Description,
    DashboardSignal Signal,
    string IconKey);

public sealed record DashboardStatusSnapshot(
    DashboardStatusItem Configuration,
    DashboardStatusItem Server,
    DashboardStatusItem Agent,
    DashboardStatusItem Logs);

public static class DashboardStatusMapper
{
    public static DashboardStatusSnapshot Map(
        ConnectorStatus status,
        LogPipelineHealth loggingHealth)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(loggingHealth);

        return new DashboardStatusSnapshot(
            MapConfiguration(status.PosConfiguration),
            MapServer(status.BridgeTransport, status.AgentRegistration),
            MapAgent(status.AgentRegistration, status.PosAuthentication),
            MapLogs(loggingHealth));
    }

    private static DashboardStatusItem MapConfiguration(
        PosConfigurationState configuration) => configuration switch
        {
            PosConfigurationState.Configured => new(
                "Configuration",
                "Configured",
                "POS and license ready",
                DashboardSignal.Healthy,
                "ConfigurationIconGeometry"),
            PosConfigurationState.Invalid => new(
                "Configuration",
                "Invalid",
                "Review POS and license settings.",
                DashboardSignal.Error,
                "ConfigurationIconGeometry"),
            _ => new(
                "Configuration",
                "Missing configuration",
                "Open Settings to configure the local POS and license.",
                DashboardSignal.Error,
                "ConfigurationIconGeometry"),
        };

    private static DashboardStatusItem MapServer(
        BridgeTransportState transport,
        AgentRegistrationState registration)
    {
        if (transport == BridgeTransportState.AuthenticationFailed)
        {
            return new DashboardStatusItem(
                "Server",
                "Authentication failed",
                "Review Retwho connection settings",
                DashboardSignal.Error,
                "CloudIconGeometry");
        }

        if (transport == BridgeTransportState.SessionReplaced)
        {
            return new DashboardStatusItem(
                "Server",
                "Session replaced",
                "A newer Retwho session is active",
                DashboardSignal.Error,
                "CloudIconGeometry");
        }

        if (transport is
            BridgeTransportState.Connecting or
            BridgeTransportState.Reconnecting or
            BridgeTransportState.Stopping)
        {
            return transport switch
            {
                BridgeTransportState.Connecting => new DashboardStatusItem(
                    "Server",
                    "Connecting",
                    "Connecting to Retwho",
                    DashboardSignal.Warning,
                    "CloudIconGeometry"),
                BridgeTransportState.Reconnecting => new DashboardStatusItem(
                    "Server",
                    "Reconnecting",
                    "Reconnecting to Retwho",
                    DashboardSignal.Warning,
                    "CloudIconGeometry"),
                _ => new DashboardStatusItem(
                    "Server",
                    "Disconnecting",
                    "Disconnecting from Retwho",
                    DashboardSignal.Warning,
                    "CloudIconGeometry"),
            };
        }

        if (registration == AgentRegistrationState.Failed)
        {
            return new DashboardStatusItem(
                "Server",
                "Registration failed",
                "Agent registration failed",
                DashboardSignal.Error,
                "CloudIconGeometry");
        }

        if (registration == AgentRegistrationState.SessionReplaced)
        {
            return new DashboardStatusItem(
                "Server",
                "Session replaced",
                "A newer Retwho session is active",
                DashboardSignal.Error,
                "CloudIconGeometry");
        }

        return transport switch
        {
            BridgeTransportState.Connected when registration ==
                AgentRegistrationState.Registered => new(
                "Server",
                "Connected",
                "Registered with Retwho",
                DashboardSignal.Healthy,
                "CloudIconGeometry"),
            BridgeTransportState.Connected when registration ==
                AgentRegistrationState.Registering => new(
                "Server",
                "Registering",
                "Registering with Retwho",
                DashboardSignal.Warning,
                "CloudIconGeometry"),
            BridgeTransportState.Connected => new(
                "Server",
                "Connected",
                "Waiting for Retwho registration",
                DashboardSignal.Warning,
                "CloudIconGeometry"),
            _ => new(
                "Server",
                "Offline",
                "Connect to Retwho to receive commands",
                DashboardSignal.Error,
                "CloudIconGeometry"),
        };
    }

    private static DashboardStatusItem MapAgent(
        AgentRegistrationState registration,
        PosAuthenticationState authentication)
    {
        if (authentication == PosAuthenticationState.RefreshingSession)
        {
            return new DashboardStatusItem(
                "Agent",
                "Refreshing",
                "Refreshing the POS session",
                DashboardSignal.Warning,
                "AgentIconGeometry");
        }

        if (authentication == PosAuthenticationState.AuthenticationFailed)
        {
            return new DashboardStatusItem(
                "Agent",
                "Error",
                "POS authentication failed",
                DashboardSignal.Error,
                "AgentIconGeometry");
        }

        if (registration == AgentRegistrationState.Registered)
        {
            return new DashboardStatusItem(
                "Agent",
                "Active",
                "Waiting for cloud commands",
                DashboardSignal.Healthy,
                "AgentIconGeometry");
        }

        if (registration == AgentRegistrationState.Registering)
        {
            return new DashboardStatusItem(
                "Agent",
                "Registering",
                "Registering with Retwho",
                DashboardSignal.Warning,
                "AgentIconGeometry");
        }

        if (registration == AgentRegistrationState.Failed)
        {
            return new DashboardStatusItem(
                "Agent",
                "Error",
                "Agent registration failed",
                DashboardSignal.Error,
                "AgentIconGeometry");
        }

        if (registration == AgentRegistrationState.SessionReplaced)
        {
            return new DashboardStatusItem(
                "Agent",
                "Inactive",
                "Session replaced by another agent",
                DashboardSignal.Error,
                "AgentIconGeometry");
        }

        return new DashboardStatusItem(
            "Agent",
            "Idle",
            "Waiting to connect to Retwho",
            DashboardSignal.Warning,
            "AgentIconGeometry");
    }

    private static DashboardStatusItem MapLogs(LogPipelineHealth health) =>
        health.State switch
        {
            LoggingHealthState.Healthy => new(
                "Logs",
                "Healthy",
                "Local logs healthy",
                DashboardSignal.Healthy,
                "LogsIconGeometry"),
            LoggingHealthState.Degraded when health.DroppedEntries > 0 => new(
                "Logs",
                $"Degraded ({health.DroppedEntries} dropped)",
                $"{health.DroppedEntries} log entries were dropped",
                DashboardSignal.Warning,
                "LogsIconGeometry"),
            LoggingHealthState.Degraded => new(
                "Logs",
                "Degraded",
                "Log delivery needs attention",
                DashboardSignal.Warning,
                "LogsIconGeometry"),
            _ => new(
                "Logs",
                "Stopped",
                "Local logging is stopped",
                DashboardSignal.Error,
                "LogsIconGeometry"),
        };
}
