using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Configuration;
using RetwhoConnector.Core.Exceptions;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Serialization;
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
    public async Task Coordinator_CurrentDataStillRaisesResultReceivedOnce()
    {
        var data = new FakeDataService();
        var coordinator = CreateCoordinator(data: data);
        VdatetimeResult? received = null;
        var resultsReceived = 0;
        coordinator.ResultReceived += (_, result) =>
        {
            resultsReceived++;
            received = result;
        };
        BridgeAcknowledgement? sent = null;
        BridgeActionContext context = CreateActionContext(
            acknowledgement =>
            {
                sent = acknowledgement;
                return Task.CompletedTask;
            });

        await coordinator.HandleActionAsync(context, CancellationToken.None);

        Assert.True(sent!.Ok);
        Assert.Same(Assert.IsType<VdatetimeResult>(sent.Result), received);
        Assert.Equal(1, resultsReceived);
    }

    [Fact]
    public async Task Coordinator_GetPluPageDispatchesTypedResultWithSuppliedQuery()
    {
        var data = new FakeDataService();
        var coordinator = CreateCoordinator(data: data);
        BridgeAcknowledgement? sent = null;
        var acknowledgements = 0;
        var resultsReceived = 0;
        coordinator.ResultReceived += (_, _) => resultsReceived++;
        BridgeActionContext context = CreateActionContext(
            acknowledgement =>
            {
                acknowledgements++;
                sent = acknowledgement;
                return Task.CompletedTask;
            },
            command: "get_plu_page",
            parametersJson: """{"page":2,"pageSize":25}""");

        await coordinator.HandleActionAsync(context, CancellationToken.None);

        Assert.True(sent!.Ok);
        Assert.Same(data.PluPageResult, Assert.IsType<PluPageResult>(sent.Result));
        Assert.Equal(new PluPageQuery(2, 25), data.LastPluPageQuery);
        Assert.Equal("FAKE_COOKIE", data.LastCookie);
        Assert.Equal(CreateSettings(), data.LastSettings);
        Assert.Equal(1, data.PluPageCalls);
        Assert.Equal(0, data.VdatetimeCalls);
        Assert.Equal(1, acknowledgements);
        Assert.Equal(0, resultsReceived);
    }

    [Fact]
    public async Task Coordinator_GetPluDispatchesTypedResultWithSuppliedQuery()
    {
        var data = new FakeDataService();
        var coordinator = CreateCoordinator(data: data);
        BridgeAcknowledgement? sent = null;
        var acknowledgements = 0;
        var resultsReceived = 0;
        coordinator.ResultReceived += (_, _) => resultsReceived++;
        BridgeActionContext context = CreateActionContext(
            acknowledgement =>
            {
                acknowledgements++;
                sent = acknowledgement;
                return Task.CompletedTask;
            },
            command: "get_plu",
            parametersJson:
                """{"upc":"00000000000001","upcModifier":"123"}""");

        await coordinator.HandleActionAsync(context, CancellationToken.None);

        Assert.True(sent!.Ok);
        Assert.Same(data.PluLookupResult, Assert.IsType<PluLookupResult>(sent.Result));
        Assert.Equal(
            new PluLookupQuery("00000000000001", "123"),
            data.LastPluLookupQuery);
        Assert.Equal("FAKE_COOKIE", data.LastCookie);
        Assert.Equal(CreateSettings(), data.LastSettings);
        Assert.Equal(1, data.PluLookupCalls);
        Assert.Equal(0, data.VdatetimeCalls);
        Assert.Equal(1, acknowledgements);
        Assert.Equal(0, resultsReceived);
    }

    [Fact]
    public async Task Coordinator_GetReferentialIntegrityDispatchesTypedResult()
    {
        var data = new FakeDataService();
        var coordinator = CreateCoordinator(data: data);
        BridgeAcknowledgement? sent = null;
        var acknowledgements = 0;
        var resultsReceived = 0;
        coordinator.ResultReceived += (_, _) => resultsReceived++;
        BridgeActionContext context = CreateActionContext(
            acknowledgement =>
            {
                acknowledgements++;
                sent = acknowledgement;
                return Task.CompletedTask;
            },
            command: "get_referential_integrity",
            parametersJson: "{}");

        await coordinator.HandleActionAsync(context, CancellationToken.None);

        Assert.True(sent!.Ok);
        Assert.Same(
            data.ReferentialIntegrityResult,
            Assert.IsType<ReferentialIntegrityResult>(sent.Result));
        Assert.Equal(1, data.ReferentialIntegrityCalls);
        Assert.Equal("FAKE_COOKIE", data.LastCookie);
        Assert.Equal(CreateSettings(), data.LastSettings);
        Assert.Equal(0, data.VdatetimeCalls);
        Assert.Equal(1, acknowledgements);
        Assert.Equal(0, resultsReceived);
    }

    [Theory]
    [InlineData("get_plu_page", "{}")]
    [InlineData("get_plu", """{"upc":"00000000000001"}""")]
    public async Task Coordinator_NewCommandsApplyParameterDefaults(
        string command,
        string parametersJson)
    {
        var data = new FakeDataService();
        var coordinator = CreateCoordinator(data: data);
        BridgeAcknowledgement? sent = null;
        BridgeActionContext context = CreateActionContext(
            acknowledgement =>
            {
                sent = acknowledgement;
                return Task.CompletedTask;
            },
            command,
            parametersJson);

        await coordinator.HandleActionAsync(context, CancellationToken.None);

        Assert.True(sent!.Ok);
        if (command == "get_plu_page")
        {
            Assert.Equal(new PluPageQuery(1, 100), data.LastPluPageQuery);
        }
        else
        {
            Assert.Equal(
                new PluLookupQuery("00000000000001", "000"),
                data.LastPluLookupQuery);
        }
    }

    [Theory]
    [InlineData("get_plu_page", """{"page":0}""")]
    [InlineData("get_plu", "{}")]
    [InlineData("get_referential_integrity", """{"dataset":"fees"}""")]
    public async Task Coordinator_InvalidNewCommandParametersFailBeforeSettingsOrPosWork(
        string command,
        string parametersJson)
    {
        var settingsService = new FakeSettingsService(CreateSettings());
        var authentication = new FakeAuthenticationService();
        var data = new FakeDataService();
        var coordinator = CreateCoordinator(
            settingsService,
            authentication,
            data);
        BridgeAcknowledgement? sent = null;
        var acknowledgements = 0;
        BridgeActionContext context = CreateActionContext(
            acknowledgement =>
            {
                acknowledgements++;
                sent = acknowledgement;
                return Task.CompletedTask;
            },
            command,
            parametersJson);

        await coordinator.HandleActionAsync(context, CancellationToken.None);

        Assert.False(sent!.Ok);
        Assert.StartsWith("INVALID_ACTION:", sent.Error, StringComparison.Ordinal);
        Assert.Equal(0, settingsService.LoadCalls);
        Assert.Equal(0, authentication.Calls);
        Assert.Equal(0, data.OperationCalls);
        Assert.Equal(1, acknowledgements);
    }

    [Theory]
    [InlineData("get_plu_page", "{}")]
    [InlineData("get_plu", """{"upc":"00000000000001"}""")]
    [InlineData("get_referential_integrity", "{}")]
    public async Task Coordinator_NewCommandExpiredCookieLogsInAndRetriesOnce(
        string command,
        string parametersJson)
    {
        var settings = CreateSettings() with { PosCookie = "FAKE_EXPIRED_COOKIE" };
        var settingsService = new FakeSettingsService(settings);
        var authentication = new FakeAuthenticationService();
        var data = new FakeDataService { FailFirstWithAuthError = true };
        var coordinator = CreateCoordinator(
            settingsService,
            authentication,
            data);
        BridgeAcknowledgement? sent = null;
        BridgeActionContext context = CreateActionContext(
            acknowledgement =>
            {
                sent = acknowledgement;
                return Task.CompletedTask;
            },
            command,
            parametersJson);

        await coordinator.HandleActionAsync(context, CancellationToken.None);

        Assert.True(sent!.Ok);
        Assert.Equal(1, authentication.Calls);
        Assert.Equal(2, data.OperationCalls);
        Assert.Equal("FAKE_NEW_COOKIE", settingsService.Settings!.PosCookie);
    }

    [Theory]
    [InlineData("get_current_data", "{}")]
    [InlineData("get_plu_page", "{}")]
    [InlineData("get_plu", """{"upc":"00000000000001"}""")]
    [InlineData("get_referential_integrity", "{}")]
    public async Task Coordinator_SecondExpiryIsReturnedWithoutAnotherRefresh(
        string command,
        string parametersJson)
    {
        var settings = CreateSettings() with { PosCookie = "FAKE_EXPIRED_COOKIE" };
        var settingsService = new FakeSettingsService(settings);
        var authentication = new FakeAuthenticationService();
        var data = new FakeDataService { AlwaysFailWithAuthError = true };
        var coordinator = CreateCoordinator(
            settingsService,
            authentication,
            data);
        BridgeAcknowledgement? sent = null;
        BridgeActionContext context = CreateActionContext(
            acknowledgement =>
            {
                sent = acknowledgement;
                return Task.CompletedTask;
            },
            command,
            parametersJson);

        await coordinator.HandleActionAsync(context, CancellationToken.None);

        Assert.False(sent!.Ok);
        Assert.StartsWith("POS_AUTH_EXPIRED:", sent.Error, StringComparison.Ordinal);
        Assert.Equal(1, authentication.Calls);
        Assert.Equal(2, data.OperationCalls);
        Assert.Equal("FAKE_NEW_COOKIE", settingsService.Settings!.PosCookie);
    }

    [Theory]
    [InlineData((1024 * 1024) - 1, true)]
    [InlineData(1024 * 1024, false)]
    [InlineData((1024 * 1024) + 1, false)]
    public async Task Coordinator_EnforcesProspectiveAcknowledgementPayloadLimit(
        int acknowledgementBytes,
        bool expectedSuccess)
    {
        var data = new FakeDataService
        {
            PluPageResult = CreatePluPageResultWithAcknowledgementBytes(
                acknowledgementBytes),
        };
        var coordinator = CreateCoordinator(data: data);
        BridgeAcknowledgement? sent = null;
        var acknowledgements = 0;
        BridgeActionContext context = CreateActionContext(
            acknowledgement =>
            {
                acknowledgements++;
                sent = acknowledgement;
                return Task.CompletedTask;
            },
            command: "get_plu_page",
            parametersJson: "{}");

        await coordinator.HandleActionAsync(context, CancellationToken.None);

        Assert.Equal(expectedSuccess, sent!.Ok);
        if (expectedSuccess)
        {
            Assert.Equal(LastCommandState.Completed, coordinator.CurrentStatus.LastCommand);
        }
        else
        {
            Assert.StartsWith(
                "PAYLOAD_TOO_LARGE:",
                sent.Error,
                StringComparison.Ordinal);
            Assert.Equal(LastCommandState.Failed, coordinator.CurrentStatus.LastCommand);
        }

        Assert.Equal(1, data.PluPageCalls);
        Assert.Equal(1, acknowledgements);
    }

    [Fact]
    public async Task Coordinator_DuplicateNewActionsShareOnePosExecution()
    {
        var started = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var data = new FakeDataService
        {
            OperationStarted = started,
            ReleaseOperation = release,
        };
        var coordinator = CreateCoordinator(data: data);
        BridgeAcknowledgement? firstAcknowledgement = null;
        BridgeAcknowledgement? secondAcknowledgement = null;
        const string actionId = "duplicate-plu-action";
        BridgeActionContext firstContext = CreateActionContext(
            acknowledgement =>
            {
                firstAcknowledgement = acknowledgement;
                return Task.CompletedTask;
            },
            "get_plu_page",
            "{}",
            actionId);
        BridgeActionContext secondContext = CreateActionContext(
            acknowledgement =>
            {
                secondAcknowledgement = acknowledgement;
                return Task.CompletedTask;
            },
            "get_plu_page",
            "{}",
            actionId);

        Task first = coordinator.HandleActionAsync(firstContext, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Task second = coordinator.HandleActionAsync(secondContext, CancellationToken.None);
        release.SetResult(true);
        await Task.WhenAll(first, second);

        Assert.Equal(1, data.PluPageCalls);
        Assert.True(firstAcknowledgement!.Ok);
        Assert.True(secondAcknowledgement!.Ok);
    }

    [Fact]
    public async Task Coordinator_CallerCancellationDoesNotCancelSharedExecution()
    {
        var started = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var data = new FakeDataService
        {
            OperationStarted = started,
            ReleaseOperation = release,
        };
        var coordinator = CreateCoordinator(data: data);
        using var callerCancellation = new CancellationTokenSource();
        BridgeAcknowledgement? cancelledAcknowledgement = null;
        BridgeAcknowledgement? sharedAcknowledgement = null;
        const string actionId = "caller-cancelled-action";
        BridgeActionContext cancelledContext = CreateActionContext(
            acknowledgement =>
            {
                cancelledAcknowledgement = acknowledgement;
                return Task.CompletedTask;
            },
            "get_plu_page",
            "{}",
            actionId);
        BridgeActionContext sharedContext = CreateActionContext(
            acknowledgement =>
            {
                sharedAcknowledgement = acknowledgement;
                return Task.CompletedTask;
            },
            "get_plu_page",
            "{}",
            actionId);

        Task cancelled = coordinator.HandleActionAsync(
            cancelledContext,
            callerCancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Task shared = coordinator.HandleActionAsync(sharedContext, CancellationToken.None);
        callerCancellation.Cancel();
        await cancelled;
        release.SetResult(true);
        await shared;

        Assert.Equal(1, data.PluPageCalls);
        Assert.False(cancelledAcknowledgement!.Ok);
        Assert.True(sharedAcknowledgement!.Ok);
    }

    [Fact]
    public async Task Coordinator_NewCommandDeadlineCancelsPosWorkAndAcknowledgesOnce()
    {
        var data = new FakeDataService
        {
            ReleaseOperation = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var coordinator = CreateCoordinator(
            data: data,
            options: new BridgeOptions
            {
                CommandDeadline = TimeSpan.FromMilliseconds(20),
            });
        BridgeAcknowledgement? sent = null;
        var acknowledgements = 0;
        BridgeActionContext context = CreateActionContext(
            acknowledgement =>
            {
                acknowledgements++;
                sent = acknowledgement;
                return Task.CompletedTask;
            },
            "get_plu_page");

        await coordinator.HandleActionAsync(context, CancellationToken.None);

        Assert.False(sent!.Ok);
        Assert.StartsWith("POS_TIMEOUT:", sent.Error, StringComparison.Ordinal);
        Assert.Equal(1, data.PluPageCalls);
        Assert.Equal(1, acknowledgements);
    }

    [Fact]
    public async Task Coordinator_SecondLoginRequiredDoesNotStartAnotherRefresh()
    {
        var settings = CreateSettings() with
        {
            PosCookie = "FAKE_EXPIRED_COOKIE",
        };
        var settingsService = new FakeSettingsService(settings);
        var authentication = new FakeAuthenticationService();
        var data = new FakeDataService
        {
            AlwaysFailWithAuthError = true,
        };
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

        await coordinator.HandleActionAsync(
            context,
            CancellationToken.None);

        Assert.NotNull(sent);
        Assert.False(sent.Ok);
        Assert.StartsWith(
            "POS_AUTH_EXPIRED:",
            sent.Error,
            StringComparison.Ordinal);
        Assert.Equal(1, authentication.Calls);
        Assert.Equal(2, data.Calls);
        Assert.Equal(
            "FAKE_NEW_COOKIE",
            settingsService.Settings!.PosCookie);
    }

    [Fact]
    public async Task Coordinator_AutoConnectFailureKeepsStartupUsable()
    {
        var settings = CreateSettings() with { AutoConnect = true };
        var bridge = new FakeBridgeClient
        {
            ConnectException = new InvalidOperationException(
                "FAKE_LICENSE must never be shown."),
        };
        var coordinator = new ConnectorCoordinator(
            new FakeSettingsService(settings),
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

        await coordinator.InitializeAsync(CancellationToken.None);

        Assert.Equal(1, bridge.ConnectCalls);
        Assert.Equal(
            BridgeTransportState.Disconnected,
            coordinator.CurrentStatus.BridgeTransport);
        Assert.Equal(
            AgentRegistrationState.Failed,
            coordinator.CurrentStatus.AgentRegistration);
        Assert.Equal(
            "Automatic connection failed. Review the saved settings and try again.",
            coordinator.CurrentStatus.Message);
        Assert.DoesNotContain(
            "FAKE_LICENSE",
            coordinator.CurrentStatus.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Coordinator_UnsupportedCommandAcknowledgesFailure()
    {
        var settingsService = new FakeSettingsService(CreateSettings());
        var authentication = new FakeAuthenticationService();
        var data = new FakeDataService();
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
            },
            command: "receive_web_data");

        await coordinator.HandleActionAsync(context, CancellationToken.None);

        Assert.False(sent!.Ok);
        Assert.Equal(
            "UNSUPPORTED_COMMAND: The requested command is not supported.",
            sent.Error);
        Assert.Equal(0, settingsService.LoadCalls);
        Assert.Equal(0, authentication.Calls);
        Assert.Equal(0, data.OperationCalls);
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
        string command = "get_current_data",
        string parametersJson = "{}",
        string? actionId = null,
        CancellationToken sessionCancellationToken = default)
    {
        using JsonDocument document = JsonDocument.Parse(parametersJson);
        return new BridgeActionContext(
            new BridgeAction
            {
                ActionId = actionId ?? Guid.NewGuid().ToString(),
                Command = command,
                Params = document.RootElement.Clone(),
                Timestamp = DateTimeOffset.UtcNow,
            },
            (value, _) => acknowledge(value),
            sessionCancellationToken);
    }

    private static ConnectorCoordinator CreateCoordinator(
        FakeSettingsService? settingsService = null,
        FakeAuthenticationService? authentication = null,
        FakeDataService? data = null,
        BridgeOptions? options = null) =>
        new(
            settingsService ?? new FakeSettingsService(CreateSettings()),
            authentication ?? new FakeAuthenticationService(),
            data ?? new FakeDataService(),
            new FakeBridgeClient { Registered = true },
            new ActionExecutionRegistry(
                TimeProvider.System,
                CancellationToken.None),
            options ?? new BridgeOptions(),
            TimeProvider.System,
            NullLogger<ConnectorCoordinator>.Instance,
            CancellationToken.None);

    private static ConnectorSettings CreateSettings() =>
        new()
        {
            PosBaseUrl = "https://pos.example.test",
            PosUsername = "FAKE_USER",
            PosPassword = "FAKE_PASSWORD",
            LicenseKey = "FAKE-LICENSE-001",
            PosCookie = "FAKE_COOKIE",
        };

    private static PluPageResult CreatePluPageResultWithAcknowledgementBytes(
        int acknowledgementBytes)
    {
        var result = new PluPageResult
        {
            Page = 1,
            TotalPages = 1,
            RequestedPageSize = 1,
            ItemCount = 1,
            Products =
            [
                new PluProduct
                {
                    Upc = "1",
                    UpcModifier = "000",
                    Description = string.Empty,
                    DepartmentId = "1",
                },
            ],
            FetchedAtUtc = DateTimeOffset.UnixEpoch,
        };
        int baseAcknowledgementBytes = JsonSerializer.SerializeToUtf8Bytes(
            BridgeAcknowledgement.Success(result),
            ConnectorJson.Options).Length;
        result = result with
        {
            Products =
            [
                result.Products[0] with
                {
                    Description = new string(
                        'X',
                        acknowledgementBytes - baseAcknowledgementBytes),
                },
            ],
        };

        Assert.Equal(
            acknowledgementBytes,
            JsonSerializer.SerializeToUtf8Bytes(
                BridgeAcknowledgement.Success(result),
                ConnectorJson.Options).Length);
        return result;
    }

    private sealed class FakeSettingsService(ConnectorSettings? settings)
        : ISecureSettingsService
    {
        public ConnectorSettings? Settings { get; private set; } = settings;
        public int LoadCalls { get; private set; }

        public Task<ConnectorSettings?> LoadAsync(CancellationToken cancellationToken)
        {
            LoadCalls++;
            return Task.FromResult(Settings);
        }

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
        public int Calls => VdatetimeCalls;
        public int OperationCalls { get; private set; }
        public int VdatetimeCalls { get; private set; }
        public int PluPageCalls { get; private set; }
        public int PluLookupCalls { get; private set; }
        public int ReferentialIntegrityCalls { get; private set; }
        public bool FailFirstWithAuthError { get; init; }
        public bool AlwaysFailWithAuthError { get; init; }
        public PluPageQuery? LastPluPageQuery { get; private set; }
        public PluLookupQuery? LastPluLookupQuery { get; private set; }
        public ConnectorSettings? LastSettings { get; private set; }
        public string? LastCookie { get; private set; }
        public TaskCompletionSource<bool>? OperationStarted { get; init; }
        public TaskCompletionSource<bool>? ReleaseOperation { get; init; }
        public PluPageResult PluPageResult { get; init; } = new()
        {
            Page = 1,
            TotalPages = 1,
            RequestedPageSize = 100,
            ItemCount = 0,
            FetchedAtUtc = DateTimeOffset.UnixEpoch,
        };
        public PluLookupResult PluLookupResult { get; init; } = new()
        {
            RequestedUpc = "00000000000001",
            RequestedUpcModifier = "000",
            Found = false,
            FetchedAtUtc = DateTimeOffset.UnixEpoch,
        };
        public ReferentialIntegrityResult ReferentialIntegrityResult { get; init; } =
            new()
            {
                SiteId = "6720",
                Limits = new ReferentialIntegrityLimits
                {
                    MaxRecords = 100,
                    MaxFeesPerItem = 10,
                },
                FetchedAtUtc = DateTimeOffset.UnixEpoch,
            };

        public async Task<VdatetimeResult> GetVdatetimeAsync(
            ConnectorSettings settings,
            string cookie,
            CancellationToken cancellationToken)
        {
            VdatetimeCalls++;
            CaptureArguments(settings, cookie);
            return await CompleteOperationAsync(new VdatetimeResult
            {
                SiteId = "6720",
                SystemDateTime = "2026-07-31T12:00:00Z",
                SystemTimeZoneId = "UTC",
                TimeZones = [],
                RawXml = "<sysDateTime />",
                FetchedAtUtc = DateTimeOffset.UtcNow,
            }, cancellationToken);
        }

        public async Task<PluPageResult> GetPluPageAsync(
            ConnectorSettings settings,
            string cookie,
            PluPageQuery query,
            CancellationToken cancellationToken)
        {
            PluPageCalls++;
            LastPluPageQuery = query;
            CaptureArguments(settings, cookie);
            return await CompleteOperationAsync(PluPageResult, cancellationToken);
        }

        public async Task<PluLookupResult> GetPluAsync(
            ConnectorSettings settings,
            string cookie,
            PluLookupQuery query,
            CancellationToken cancellationToken)
        {
            PluLookupCalls++;
            LastPluLookupQuery = query;
            CaptureArguments(settings, cookie);
            return await CompleteOperationAsync(PluLookupResult, cancellationToken);
        }

        public async Task<ReferentialIntegrityResult> GetReferentialIntegrityAsync(
            ConnectorSettings settings,
            string cookie,
            CancellationToken cancellationToken)
        {
            ReferentialIntegrityCalls++;
            CaptureArguments(settings, cookie);
            return await CompleteOperationAsync(
                ReferentialIntegrityResult,
                cancellationToken);
        }

        private void CaptureArguments(ConnectorSettings settings, string cookie)
        {
            LastSettings = settings;
            LastCookie = cookie;
        }

        private async Task<T> CompleteOperationAsync<T>(
            T result,
            CancellationToken cancellationToken)
        {
            OperationCalls++;
            OperationStarted?.TrySetResult(true);
            if (AlwaysFailWithAuthError ||
                (FailFirstWithAuthError && OperationCalls == 1))
            {
                throw new PosAuthenticationException(
                    "POS_AUTH_EXPIRED",
                    "Expired.");
            }

            if (ReleaseOperation is not null)
            {
                await ReleaseOperation.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }

    private sealed class FakeBridgeClient : IBridgeSocketClient
    {
        public bool Registered { get; init; }
        public Exception? ConnectException { get; init; }
        public int ConnectCalls { get; private set; }
        public bool IsTransportConnected => Registered;
        public bool IsRegistered => Registered;

        public event EventHandler<BridgeConnectionStateChangedEventArgs>? StateChanged;
        public event Func<BridgeActionContext, CancellationToken, Task>? ActionReceived;
        public event EventHandler? SessionReplaced;

        public Task ConnectAsync(string licenseKey, CancellationToken cancellationToken)
        {
            ConnectCalls++;
            return ConnectException is null
                ? Task.CompletedTask
                : Task.FromException(ConnectException);
        }

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
