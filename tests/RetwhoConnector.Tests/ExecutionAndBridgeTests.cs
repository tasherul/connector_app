using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Configuration;
using RetwhoConnector.Core.Exceptions;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Services;

namespace RetwhoConnector.Tests;

public sealed class ExecutionAndBridgeTests
{
    [Fact]
    public async Task Registry_DuplicateActionIdsShareExecution()
    {
        var registry = new ActionExecutionRegistry(
            TimeProvider.System,
            CancellationToken.None);
        var calls = 0;

        async Task<BridgeAcknowledgement> Factory(CancellationToken token)
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(25, token);
            return BridgeAcknowledgement.Success(new { value = 1 });
        }

        BridgeAcknowledgement[] results = await Task.WhenAll(
            registry.ExecuteAsync("same-id", Factory, CancellationToken.None),
            registry.ExecuteAsync("same-id", Factory, CancellationToken.None));

        Assert.Equal(1, calls);
        Assert.All(results, result => Assert.True(result.Ok));
    }

    [Fact]
    public async Task Coordinator_ExpiredCookieLogsInAndRetriesOnce()
    {
        var settings = CreateSettings() with { PosCookie = "FAKE_EXPIRED_COOKIE" };
        var settingsService = new FakeSettingsService(settings);
        var authentication = new FakeAuthenticationService();
        var data = new FakeDataService { FailFirstWithAuthError = true };
        var bridge = new FakeBridgeClient { Registered = true };
        var coordinator = new ConnectorCoordinator(
            settingsService,
            authentication,
            data,
            bridge,
            new ActionExecutionRegistry(
                TimeProvider.System,
                CancellationToken.None),
            new BridgeOptions(),
            TimeProvider.System,
            NullLogger<ConnectorCoordinator>.Instance,
            CancellationToken.None);
        BridgeAcknowledgement? sent = null;
        BridgeActionContext context = CreateActionContext(
            acknowledgement =>
            {
                sent = acknowledgement;
                return Task.CompletedTask;
            });

        await coordinator.HandleActionAsync(context, CancellationToken.None);

        Assert.True(sent!.Ok);
        Assert.Equal(1, authentication.Calls);
        Assert.Equal(2, data.Calls);
        Assert.Equal("FAKE_NEW_COOKIE", settingsService.Settings!.PosCookie);
    }

    [Fact]
    public async Task Coordinator_UnsupportedCommandAcknowledgesFailure()
    {
        var bridge = new FakeBridgeClient { Registered = true };
        var coordinator = new ConnectorCoordinator(
            new FakeSettingsService(CreateSettings()),
            new FakeAuthenticationService(),
            new FakeDataService(),
            bridge,
            new ActionExecutionRegistry(
                TimeProvider.System,
                CancellationToken.None),
            new BridgeOptions(),
            TimeProvider.System,
            NullLogger<ConnectorCoordinator>.Instance,
            CancellationToken.None);
        BridgeAcknowledgement? sent = null;
        BridgeActionContext context = CreateActionContext(
            acknowledgement =>
            {
                sent = acknowledgement;
                return Task.CompletedTask;
            },
            command: "receive_web_data");

        await coordinator.HandleActionAsync(context, CancellationToken.None);

        Assert.False(sent!.Ok);
        Assert.StartsWith("UNSUPPORTED_COMMAND:", sent.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bridge_ConnectRegistersLocalhostAgent()
    {
        var adapter = new FakeSocketAdapter
        {
            RegistrationResponse = new BridgeEnvelope<RegistrationResponse>
            {
                Ok = true,
                Code = "REGISTERED",
                Data = new RegistrationResponse
                {
                    Room = "room_FAKE-LICENSE-001",
                    ClientType = "localhost_agent",
                },
            },
        };
        var client = new BridgeSocketClient(
            new FakeSocketAdapterFactory(adapter),
            new BridgeOptions(),
            NullLogger<BridgeSocketClient>.Instance);

        await client.ConnectAsync(
            "FAKE-LICENSE-001",
            CancellationToken.None);

        Assert.True(client.IsRegistered);
        Assert.Equal("register_client", adapter.LastEventName);
        Assert.Equal("localhost_agent", adapter.LastPayloadClientType);
    }

    [Fact]
    public async Task Bridge_CommandIsAcknowledgedExactlyOnceWhenHandlerThrows()
    {
        var adapter = new FakeSocketAdapter
        {
            RegistrationResponse = new BridgeEnvelope<RegistrationResponse>
            {
                Ok = true,
                Code = "REGISTERED",
                Data = new RegistrationResponse
                {
                    Room = "room_FAKE-LICENSE-001",
                    ClientType = "localhost_agent",
                },
            },
        };
        var client = new BridgeSocketClient(
            new FakeSocketAdapterFactory(adapter),
            new BridgeOptions(),
            NullLogger<BridgeSocketClient>.Instance);
        client.ActionReceived += async (context, _) =>
        {
            await context.AcknowledgeOnceAsync(
                BridgeAcknowledgement.Success(new { value = 1 }));
            throw new InvalidOperationException("Test handler failure.");
        };
        await client.ConnectAsync("FAKE-LICENSE-001", CancellationToken.None);
        using JsonDocument document = JsonDocument.Parse("{}");
        var socketContext = new FakeSocketEventContext(new BridgeAction
        {
            ActionId = "action-1",
            Command = "get_current_data",
            Params = document.RootElement.Clone(),
            Timestamp = DateTimeOffset.UtcNow,
        });

        await adapter.RaiseAsync("execute_local_action", socketContext);

        Assert.Equal(1, socketContext.SendCount);
    }

    private static BridgeActionContext CreateActionContext(
        Func<BridgeAcknowledgement, Task> acknowledge,
        string command = "get_current_data")
    {
        using JsonDocument document = JsonDocument.Parse("{}");
        return new BridgeActionContext(
            new BridgeAction
            {
                ActionId = Guid.NewGuid().ToString(),
                Command = command,
                Params = document.RootElement.Clone(),
                Timestamp = DateTimeOffset.UtcNow,
            },
            (value, _) => acknowledge(value),
            CancellationToken.None);
    }

    private static ConnectorSettings CreateSettings() =>
        new()
        {
            PosBaseUrl = "https://pos.example.test",
            PosUsername = "FAKE_USER",
            PosPassword = "FAKE_PASSWORD",
            LicenseKey = "FAKE-LICENSE-001",
            PosCookie = "FAKE_COOKIE",
        };

    private sealed class FakeSettingsService(ConnectorSettings? settings)
        : ISecureSettingsService
    {
        public ConnectorSettings? Settings { get; private set; } = settings;

        public Task<ConnectorSettings?> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Settings);

        public Task SaveAsync(
            ConnectorSettings value,
            CancellationToken cancellationToken)
        {
            Settings = value;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            Settings = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuthenticationService : IPosAuthenticationService
    {
        public int Calls { get; private set; }

        public Task<PosSession> LoginAsync(
            ConnectorSettings settings,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new PosSession
            {
                Cookie = "FAKE_NEW_COOKIE",
                SiteId = "6720",
                ObtainedAtUtc = DateTimeOffset.UtcNow,
            });
        }
    }

    private sealed class FakeDataService : IPosDataService
    {
        public int Calls { get; private set; }
        public bool FailFirstWithAuthError { get; init; }

        public Task<VdatetimeResult> GetVdatetimeAsync(
            ConnectorSettings settings,
            string cookie,
            CancellationToken cancellationToken)
        {
            Calls++;
            if (FailFirstWithAuthError && Calls == 1)
            {
                throw new PosAuthenticationException(
                    "POS_AUTH_EXPIRED",
                    "Expired.");
            }

            return Task.FromResult(new VdatetimeResult
            {
                SiteId = "6720",
                SystemDateTime = "2026-07-31T12:00:00Z",
                SystemTimeZoneId = "UTC",
                TimeZones = [],
                RawXml = "<sysDateTime />",
                FetchedAtUtc = DateTimeOffset.UtcNow,
            });
        }
    }

    private sealed class FakeBridgeClient : IBridgeSocketClient
    {
        public bool Registered { get; init; }
        public bool IsTransportConnected => Registered;
        public bool IsRegistered => Registered;

        public event EventHandler<BridgeConnectionStateChangedEventArgs>? StateChanged;
        public event Func<BridgeActionContext, CancellationToken, Task>? ActionReceived;
        public event EventHandler? SessionReplaced;

        public Task ConnectAsync(string licenseKey, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DisconnectAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<AgentDataPushResponse> PushAgentDataAsync(
            object payload,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void TouchEvents()
        {
            _ = StateChanged;
            _ = ActionReceived;
            _ = SessionReplaced;
        }
    }

    private sealed class FakeSocketAdapterFactory(ISocketIoClientAdapter adapter)
        : ISocketIoClientAdapterFactory
    {
        public ISocketIoClientAdapter Create(string licenseKey) => adapter;
    }

    private sealed class FakeSocketAdapter : ISocketIoClientAdapter
    {
        private readonly Dictionary<string, Func<ISocketEventContext, Task>> _handlers = [];
        public bool Connected { get; private set; }
        public BridgeEnvelope<RegistrationResponse>? RegistrationResponse { get; init; }
        public string? LastEventName { get; private set; }
        public string? LastPayloadClientType { get; private set; }

        public event Func<Task>? ConnectedEvent;
        public event Func<string, Task>? DisconnectedEvent;
        public event Func<string, Task>? ErrorEvent;
        public event Func<int, Task>? ReconnectAttemptEvent;

        public void On(string eventName, Func<ISocketEventContext, Task> handler) =>
            _handlers[eventName] = handler;

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            Connected = true;
            if (ConnectedEvent is not null)
            {
                await ConnectedEvent.Invoke();
            }
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            Connected = false;
            return Task.CompletedTask;
        }

        public Task<TAck> EmitWithAckAsync<TAck>(
            string eventName,
            object payload,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            LastEventName = eventName;
            LastPayloadClientType = payload.GetType()
                .GetProperty("ClientType")?.GetValue(payload)?.ToString();
            return Task.FromResult((TAck)(object)RegistrationResponse!);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task RaiseAsync(
            string eventName,
            ISocketEventContext context) =>
            _handlers[eventName](context);

        public void TouchEvents()
        {
            _ = DisconnectedEvent;
            _ = ErrorEvent;
            _ = ReconnectAttemptEvent;
        }
    }

    private sealed class FakeSocketEventContext(object value)
        : ISocketEventContext
    {
        public int SendCount { get; private set; }

        public T? GetValue<T>(int index) => (T)value;

        public Task SendAckDataAsync(
            object response,
            CancellationToken cancellationToken)
        {
            SendCount++;
            return Task.CompletedTask;
        }
    }
}
