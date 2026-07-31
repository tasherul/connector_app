using RetwhoConnector.App.ViewModels;
using RetwhoConnector.Core.Models;

namespace RetwhoConnector.Tests;

public sealed class DashboardStatusMappingTests
{
    [Fact]
    public void Map_ReportsHealthyConfiguredRegisteredConnector()
    {
        DashboardStatusSnapshot result = DashboardStatusMapper.Map(
            new ConnectorStatus
            {
                PosConfiguration = PosConfigurationState.Configured,
                BridgeTransport = BridgeTransportState.Connected,
                AgentRegistration = AgentRegistrationState.Registered,
            },
            new LogPipelineHealth(LoggingHealthState.Healthy, 0, "Healthy"));

        Assert.Equal("Configuration", result.Configuration.Title);
        Assert.Equal("Configured", result.Configuration.Status);
        Assert.Equal("POS and license ready", result.Configuration.Description);
        Assert.Equal(DashboardSignal.Healthy, result.Configuration.Signal);
        Assert.Equal("ConfigurationIconGeometry", result.Configuration.IconKey);

        Assert.Equal("Server", result.Server.Title);
        Assert.Equal("Connected", result.Server.Status);
        Assert.Equal("Registered with Retwho", result.Server.Description);
        Assert.Equal(DashboardSignal.Healthy, result.Server.Signal);
        Assert.Equal("CloudIconGeometry", result.Server.IconKey);

        Assert.Equal("Agent", result.Agent.Title);
        Assert.Equal("Active", result.Agent.Status);
        Assert.Equal("Waiting for cloud commands", result.Agent.Description);
        Assert.Equal(DashboardSignal.Healthy, result.Agent.Signal);
        Assert.Equal("AgentIconGeometry", result.Agent.IconKey);

        Assert.Equal("Logs", result.Logs.Title);
        Assert.Equal("Healthy", result.Logs.Status);
        Assert.Equal("Local logs healthy", result.Logs.Description);
        Assert.Equal(DashboardSignal.Healthy, result.Logs.Signal);
        Assert.Equal("LogsIconGeometry", result.Logs.IconKey);
    }

    [Theory]
    [InlineData(
        PosConfigurationState.NotConfigured,
        "Missing configuration",
        "Open Settings to configure the local POS and license.",
        DashboardSignal.Error)]
    [InlineData(
        PosConfigurationState.Invalid,
        "Invalid",
        "Review POS and license settings.",
        DashboardSignal.Error)]
    public void Map_ReportsConfigurationProblems(
        PosConfigurationState configuration,
        string expectedStatus,
        string expectedDescription,
        DashboardSignal expectedSignal)
    {
        DashboardStatusSnapshot result = Map(status =>
            status with { PosConfiguration = configuration });

        Assert.Equal("Configuration", result.Configuration.Title);
        Assert.Equal(expectedStatus, result.Configuration.Status);
        Assert.Equal(expectedDescription, result.Configuration.Description);
        Assert.Equal(expectedSignal, result.Configuration.Signal);
        Assert.Equal("ConfigurationIconGeometry", result.Configuration.IconKey);
    }

    [Theory]
    [InlineData(
        BridgeTransportState.Connecting,
        AgentRegistrationState.NotRegistered,
        "Connecting",
        "Connecting to Retwho",
        DashboardSignal.Warning)]
    [InlineData(
        BridgeTransportState.Reconnecting,
        AgentRegistrationState.NotRegistered,
        "Reconnecting",
        "Reconnecting to Retwho",
        DashboardSignal.Warning)]
    [InlineData(
        BridgeTransportState.Connected,
        AgentRegistrationState.Registering,
        "Registering",
        "Registering with Retwho",
        DashboardSignal.Warning)]
    [InlineData(
        BridgeTransportState.Disconnected,
        AgentRegistrationState.NotRegistered,
        "Offline",
        "Connect to Retwho to receive commands",
        DashboardSignal.Error)]
    [InlineData(
        BridgeTransportState.AuthenticationFailed,
        AgentRegistrationState.NotRegistered,
        "Authentication failed",
        "Review Retwho connection settings",
        DashboardSignal.Error)]
    [InlineData(
        BridgeTransportState.SessionReplaced,
        AgentRegistrationState.SessionReplaced,
        "Session replaced",
        "A newer Retwho session is active",
        DashboardSignal.Error)]
    public void Map_ReportsServerTransportAndRegistrationState(
        BridgeTransportState transport,
        AgentRegistrationState registration,
        string expectedStatus,
        string expectedDescription,
        DashboardSignal expectedSignal)
    {
        DashboardStatusSnapshot result = Map(status => status with
        {
            BridgeTransport = transport,
            AgentRegistration = registration,
        });

        Assert.Equal("Server", result.Server.Title);
        Assert.Equal(expectedStatus, result.Server.Status);
        Assert.Equal(expectedDescription, result.Server.Description);
        Assert.Equal(expectedSignal, result.Server.Signal);
        Assert.Equal("CloudIconGeometry", result.Server.IconKey);
    }

    [Theory]
    [InlineData(
        AgentRegistrationState.Registering,
        PosAuthenticationState.NotConfigured,
        "Registering",
        "Registering with Retwho",
        DashboardSignal.Warning)]
    [InlineData(
        AgentRegistrationState.NotRegistered,
        PosAuthenticationState.NotConfigured,
        "Idle",
        "Waiting to connect to Retwho",
        DashboardSignal.Warning)]
    [InlineData(
        AgentRegistrationState.NotRegistered,
        PosAuthenticationState.RefreshingSession,
        "Refreshing",
        "Refreshing the POS session",
        DashboardSignal.Warning)]
    [InlineData(
        AgentRegistrationState.Failed,
        PosAuthenticationState.NotConfigured,
        "Error",
        "Agent registration failed",
        DashboardSignal.Error)]
    [InlineData(
        AgentRegistrationState.SessionReplaced,
        PosAuthenticationState.NotConfigured,
        "Inactive",
        "Session replaced by another agent",
        DashboardSignal.Error)]
    public void Map_ReportsAgentState(
        AgentRegistrationState registration,
        PosAuthenticationState authentication,
        string expectedStatus,
        string expectedDescription,
        DashboardSignal expectedSignal)
    {
        DashboardStatusSnapshot result = Map(status => status with
        {
            AgentRegistration = registration,
            PosAuthentication = authentication,
        });

        Assert.Equal("Agent", result.Agent.Title);
        Assert.Equal(expectedStatus, result.Agent.Status);
        Assert.Equal(expectedDescription, result.Agent.Description);
        Assert.Equal(expectedSignal, result.Agent.Signal);
        Assert.Equal("AgentIconGeometry", result.Agent.IconKey);
    }

    [Theory]
    [InlineData(
        LoggingHealthState.Degraded,
        4,
        "Degraded (4 dropped)",
        "4 log entries were dropped",
        DashboardSignal.Warning)]
    [InlineData(
        LoggingHealthState.Degraded,
        0,
        "Degraded",
        "Log delivery needs attention",
        DashboardSignal.Warning)]
    [InlineData(
        LoggingHealthState.Stopped,
        0,
        "Stopped",
        "Local logging is stopped",
        DashboardSignal.Error)]
    public void Map_ReportsLoggingHealth(
        LoggingHealthState healthState,
        long droppedEntries,
        string expectedStatus,
        string expectedDescription,
        DashboardSignal expectedSignal)
    {
        DashboardStatusSnapshot result = DashboardStatusMapper.Map(
            HealthyConnectorStatus,
            new LogPipelineHealth(healthState, droppedEntries, "Pipeline"));

        Assert.Equal("Logs", result.Logs.Title);
        Assert.Equal(expectedStatus, result.Logs.Status);
        Assert.Equal(expectedDescription, result.Logs.Description);
        Assert.Equal(expectedSignal, result.Logs.Signal);
        Assert.Equal("LogsIconGeometry", result.Logs.IconKey);
    }

    private static ConnectorStatus HealthyConnectorStatus { get; } = new()
    {
        PosConfiguration = PosConfigurationState.Configured,
        BridgeTransport = BridgeTransportState.Connected,
        AgentRegistration = AgentRegistrationState.Registered,
    };

    private static DashboardStatusSnapshot Map(
        Func<ConnectorStatus, ConnectorStatus> update) =>
        DashboardStatusMapper.Map(
            update(HealthyConnectorStatus),
            new LogPipelineHealth(LoggingHealthState.Healthy, 0, "Healthy"));
}
