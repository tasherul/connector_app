# Modern Compact Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the four separated dashboard cards with the approved compact adaptive status rail, eliminate white-on-white text, and add distinct modern accessible action buttons.

**Architecture:** Introduce platform-light status presentation records and one mapper consumed by MainWindowViewModel. Keep visual state in WPF resource dictionaries: semantic brushes, vector geometries, reusable control templates, and a width-to-column converter; add source-level cross-platform tests plus Windows-only STA rendering tests.

**Tech Stack:** C# 14, .NET 10 Windows, WPF, CommunityToolkit.Mvvm, XAML resource dictionaries, xUnit.

## Global Constraints

- Execute after the PLU/referential plan, directly on main.
- Keep visible branding Hybrid Edge Connector Agent and technical Retwho names/storage paths.
- Preserve tray close/minimize/restore/exit behavior and explicit shutdown mode.
- Preserve virtualized sanitized activity history with the newest 1,000 entries.
- Use vector Path geometries, not emoji glyphs.
- Use green for healthy, yellow for transitional/degraded, and red for missing/disconnected/error.
- Never communicate status by color alone; retain text, icon, automation name, and keyboard focus.
- Every foreground/background combination must be explicit and readable, including disabled and validation states.
- Keep code-behind limited to window lifecycle, secret transfer, and dialog delegation.
- Every task follows red-green-refactor, runs focused checks, and commits.

---

### Task 1: Add semantic dashboard-status presentation

**Files:**
- Create: src/RetwhoConnector.App/ViewModels/DashboardStatusModels.cs
- Modify: src/RetwhoConnector.App/ViewModels/MainWindowViewModel.cs
- Modify: tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj
- Create: tests/RetwhoConnector.Tests/DashboardStatusMappingTests.cs

**Interfaces:**
- Produces: DashboardSignal enum with Healthy, Warning, Error
- Produces: DashboardStatusItem(string Title, string Status, string Description, DashboardSignal Signal, string IconKey)
- Produces: DashboardStatusSnapshot with Configuration, Server, Agent, Logs
- Produces: DashboardStatusMapper.Map(ConnectorStatus, LogPipelineHealth)
- Consumes: ConnectorStatus and LogPipelineHealth

- [ ] **Step 1: Link the platform-light source and write failing mapping tests**

Add DashboardStatusModels.cs to the test project as a linked Compile item so
the current cross-platform test project exercises the same source without
referencing WPF:

    <Compile Include="..\..\src\RetwhoConnector.App\ViewModels\DashboardStatusModels.cs"
             Link="Linked\DashboardStatusModels.cs" />

Test these exact mappings:

    DashboardStatusSnapshot result = DashboardStatusMapper.Map(
        new ConnectorStatus {
            PosConfiguration = PosConfigurationState.Configured,
            BridgeTransport = BridgeTransportState.Connected,
            AgentRegistration = AgentRegistrationState.Registered
        },
        new LogPipelineHealth(LoggingHealthState.Healthy, 0, "Healthy"));

    Assert.Equal(DashboardSignal.Healthy, result.Configuration.Signal);
    Assert.Equal("Connected", result.Server.Status);
    Assert.Equal("Active", result.Agent.Status);
    Assert.Equal("Healthy", result.Logs.Status);

Add theories for missing/invalid configuration; connecting/reconnecting;
offline/authentication failed/session replaced; registering/idle/refreshing;
failed agent; degraded logs with dropped count; and stopped logs. Server is
Healthy only when transport is Connected and registration is Registered; it is
Warning while either layer is connecting/registering/reconnecting and Error
when offline or permanently rejected. Assert titles, descriptions, and IconKey
values as well as signals.

- [ ] **Step 2: Run mapping tests and verify red**

Run:

    dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj --filter FullyQualifiedName~DashboardStatusMappingTests

Expected: compilation fails because the status models do not exist.

- [ ] **Step 3: Implement the pure mapper**

DashboardStatusModels.cs may depend only on RetwhoConnector.Core.Models and
System. It must not reference System.Windows, Brush, ResourceDictionary, or
CommunityToolkit. Use fixed safe descriptions such as POS and license ready,
Registered with Retwho, Waiting for cloud commands, and Local logs healthy.

- [ ] **Step 4: Expose status items from MainWindowViewModel**

Add observable ConfigurationIndicator, ServerIndicator, AgentIndicator, and
LoggingIndicator properties. Keep ConnectionActionText and BannerMessage.
Replace duplicated string-switch code in ApplyStatus/ApplyLoggingHealth with a
single ApplyDashboardSnapshot method fed by the latest ConnectorStatus and
LogPipelineHealth. Preserve current command enablement and UI-thread dispatch.

- [ ] **Step 5: Run focused and existing startup tests**

Run:

    dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj --filter "FullyQualifiedName~DashboardStatusMappingTests|FullyQualifiedName~WpfStartupTests"

Expected: all selected tests pass with zero warnings.

- [ ] **Step 6: Commit**

    git add src/RetwhoConnector.App/ViewModels/DashboardStatusModels.cs src/RetwhoConnector.App/ViewModels/MainWindowViewModel.cs tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj tests/RetwhoConnector.Tests/DashboardStatusMappingTests.cs
    git commit -m "feat: model dashboard status signals"

---

### Task 2: Build contrast-safe resources and modern button templates

**Files:**
- Create: src/RetwhoConnector.App/Styles/Icons.xaml
- Modify: src/RetwhoConnector.App/App.xaml
- Modify: src/RetwhoConnector.App/Styles/Colors.xaml
- Modify: src/RetwhoConnector.App/Styles/Controls.xaml
- Modify: tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj
- Create: tests/RetwhoConnector.Tests/WpfThemeContractTests.cs

**Interfaces:**
- Produces: ConfigurationIconGeometry, CloudIconGeometry, AgentIconGeometry, LogsIconGeometry, SettingsIconGeometry, FolderIconGeometry, ConnectIconGeometry, DisconnectIconGeometry, ExitIconGeometry
- Produces: semantic brush resources consumed by the converter in Task 3
- Produces: SettingsButtonStyle, ConnectionButtonStyle, LogsButtonStyle, DangerButtonStyle, DialogPrimaryButtonStyle, DialogSecondaryButtonStyle

- [ ] **Step 1: Copy style dictionaries as test fixtures and add failing tests**

Link Colors.xaml, Controls.xaml, and Icons.xaml into the existing test output.
Parse them with XDocument. Assert:

- every SolidColorBrush key has a non-transparent value;
- BackgroundBrush differs from PrimaryTextBrush and SecondaryTextBrush;
- TextBox/PasswordBox foreground differs from input background;
- disabled text differs from disabled background;
- validation text differs from window/dialog backgrounds;
- all nine vector geometry keys exist;
- all six named button styles exist;
- each modern button template exposes hover, pressed, disabled, and keyboard
  focus triggers; and
- App.xaml merges Icons.xaml before Controls.xaml.

- [ ] **Step 2: Run theme tests and verify red**

Run:

    dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj --filter FullyQualifiedName~WpfThemeContractTests

Expected: missing icon dictionary, styles, and states fail.

- [ ] **Step 3: Define semantic colors and explicit control colors**

Retain the selected dark navy direction. Add explicit brushes for:

    WindowBackground #0B1220
    Surface          #111D2F
    SurfaceRaised    #17243A
    PrimaryText      #F8FAFC
    SecondaryText    #CBD5E1
    MutedText        #94A3B8
    Success          #22C55E
    Warning          #F59E0B
    Error            #EF4444
    Info             #38BDF8

Add dedicated disabled, focus-ring, input, validation, and outlined-button
brushes. Do not rely on Windows default foreground inheritance.

- [ ] **Step 4: Add vector geometries and reusable templates**

Icons.xaml contains frozen Geometry resources for configuration/wrench, cloud,
shield/agent, terminal/logs, settings, folder, connect/disconnect, and exit.

Controls.xaml defines:

- explicit Window, TextBlock, Label, CheckBox, TextBox, PasswordBox, ToolTip,
  and validation styles;
- a rounded ModernButtonBaseStyle ControlTemplate with Border,
  ContentPresenter, focus ring, hover/pressed/disabled triggers;
- unique settings, connection, logs, danger, and dialog styles; and
- a ConnectionButtonStyle DataTrigger that changes icon/foreground/background
  when ConnectionActionText is Disconnect.

The base template reads its vector geometry from Button.Tag. Named styles set
Tag to the matching Geometry resource; ConnectionButtonStyle changes Tag from
ConnectIconGeometry to DisconnectIconGeometry in the same DataTrigger.

- [ ] **Step 5: Run theme tests and build App**

Run:

    dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj --filter FullyQualifiedName~WpfThemeContractTests
    dotnet build src/RetwhoConnector.App/RetwhoConnector.App.csproj -c Debug

Expected: tests pass and XAML compiles with zero warnings.

- [ ] **Step 6: Commit**

    git add src/RetwhoConnector.App/App.xaml src/RetwhoConnector.App/Styles/Colors.xaml src/RetwhoConnector.App/Styles/Controls.xaml src/RetwhoConnector.App/Styles/Icons.xaml tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj tests/RetwhoConnector.Tests/WpfThemeContractTests.cs
    git commit -m "feat: add modern contrast-safe WPF theme"

---

### Task 3: Replace dashboard cards with the compact adaptive status rail

**Files:**
- Create: src/RetwhoConnector.App/Converters/DashboardSignalToBrushConverter.cs
- Create: src/RetwhoConnector.App/Converters/WidthToStatusColumnCountConverter.cs
- Modify: src/RetwhoConnector.App/MainWindow.xaml
- Modify: src/RetwhoConnector.App/ConfigurationWindow.xaml
- Modify: tests/RetwhoConnector.Tests/WpfStartupTests.cs

**Interfaces:**
- Consumes: four DashboardStatusItem properties from Task 1
- Consumes: semantic brushes, vector geometries, and styles from Task 2
- Produces: four-segment rail with four columns at width >= 920 and two columns below 920

- [ ] **Step 1: Write failing XAML contract tests**

Update MainWindow_ExposesApprovedDashboardAndActivityFeed to require one
AutomationProperties.Name=Connector status rail container and four status
segments bound to ConfigurationIndicator, ServerIndicator, AgentIndicator, and
LoggingIndicator. For each segment assert a Path icon, Ellipse signal,
Status/Description TextBlocks, and automation name.

Assert no emoji literals remain, button style keys are applied to Settings,
Connect/Disconnect, Open Logs, and Exit, the terminal ListBox remains
virtualized, and WidthToStatusColumnCountConverter is used.

Update configuration tests to assert title/description/validation foregrounds
are explicit resources and Clear/Cancel/Save buttons use their approved named
styles.

- [ ] **Step 2: Run startup/XAML tests and verify red**

Run:

    dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj --filter FullyQualifiedName~WpfStartupTests

Expected: old four-card XAML fails the new rail/style assertions.

- [ ] **Step 3: Implement typed converters**

DashboardSignalToBrushConverter maps Healthy to SuccessBrush, Warning to
WarningBrush, and Error to ErrorBrush; ConvertBack throws
NotSupportedException. WidthToStatusColumnCountConverter returns 4 for finite
width >= 920 and 2 otherwise. Neither converter inspects localized status text.

- [ ] **Step 4: Build the compact rail**

Replace the four independent Borders with one rounded outer Border containing
an adaptive UniformGrid. Each segment uses:

- a 28x28 tinted icon tile with Path geometry;
- uppercase title;
- signal Ellipse bound through DashboardSignalToBrushConverter;
- bold Status;
- one-line Description with trimming and tooltip; and
- a separator except after the final segment.

Bind UniformGrid.Columns to MainWindow.ActualWidth through
WidthToStatusColumnCountConverter. Reduce MinWidth enough to exercise 2x2
layout without breaking the command bar; use WrapPanel for command actions.

Use vector icon plus text content inside each action button. Preserve command,
automation name, IsDefault/IsCancel behavior, and no new code-behind.

- [ ] **Step 5: Fix configuration-window surfaces**

Give the configuration window an explicit raised surface, contrasting title,
description, labels, inputs, validation block, checkbox, and disabled state.
Apply Danger/Secondary/DialogPrimary styles to Clear, Cancel, and Save/Test.
Keep PasswordBox masking and PasswordChanged code-behind unchanged.

- [ ] **Step 6: Run tests and build**

Run:

    dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj --filter "FullyQualifiedName~WpfStartupTests|FullyQualifiedName~WpfThemeContractTests|FullyQualifiedName~DashboardStatusMappingTests"
    dotnet build src/RetwhoConnector.App/RetwhoConnector.App.csproj -c Debug

Expected: all tests pass and all XAML compiles.

- [ ] **Step 7: Commit**

    git add src/RetwhoConnector.App/MainWindow.xaml src/RetwhoConnector.App/ConfigurationWindow.xaml src/RetwhoConnector.App/Converters/DashboardSignalToBrushConverter.cs src/RetwhoConnector.App/Converters/WidthToStatusColumnCountConverter.cs tests/RetwhoConnector.Tests/WpfStartupTests.cs
    git commit -m "feat: build compact connector status rail"

---

### Task 4: Add Windows STA smoke tests and complete acceptance

**Files:**
- Create: tests/RetwhoConnector.App.Tests/RetwhoConnector.App.Tests.csproj
- Create: tests/RetwhoConnector.App.Tests/StaTestRunner.cs
- Create: tests/RetwhoConnector.App.Tests/UiTestHarness.cs
- Create: tests/RetwhoConnector.App.Tests/WindowRenderingTests.cs
- Modify: RetwhoConnector.sln
- Modify: README.md
- Modify: tests/RetwhoConnector.Tests/DocumentationTests.cs

**Interfaces:**
- Produces: Windows-only tests that construct, measure, arrange, and template MainWindow and ConfigurationWindow on an STA thread
- Preserves: Linux restore/build and core test execution without a WindowsDesktop runtime

- [ ] **Step 1: Create a Windows-conditional WPF test project**

Use net10.0-windows and UseWPF=true. On Windows set IsTestProject=true and add
Microsoft.NET.Test.Sdk, xunit, xunit.runner.visualstudio, and a ProjectReference
to RetwhoConnector.App. On non-Windows set IsTestProject=false and remove
Compile items so solution restore/build succeeds without loading WPF test
runtime types.

Add the project to the tests solution folder with:

    dotnet sln RetwhoConnector.sln add tests/RetwhoConnector.App.Tests/RetwhoConnector.App.Tests.csproj --solution-folder tests

- [ ] **Step 2: Write the STA harness and failing rendering tests**

StaTestRunner is an xUnit collection fixture that starts one background Thread,
calls SetApartmentState(STA), creates exactly one WPF Application, and runs its
Dispatcher for the entire test collection. RunAsync invokes delegates through
that dispatcher and propagates exceptions. DisposeAsync shuts down the
Application/Dispatcher and joins the thread once; tests never try to create a
second WPF Application in the same process.

Before constructing a window, the fixture loads Colors.xaml, Icons.xaml, and
Controls.xaml from RetwhoConnector pack URIs into Application.Resources in
dependency order.

UiTestHarness supplies explicit fake implementations for
IAgentOrchestrationService, IAgentLog, IConfigurationDialogService,
IApplicationControlService, IUserDialogService, and the configuration
dependencies. It never opens sockets, files, databases, or dialogs.

WindowRenderingTests must:

    await StaTestRunner.RunAsync(() =>
    {
        using UiTestHarness harness = new();
        MainWindow window = harness.CreateMainWindow();
        window.Measure(new Size(1120, 760));
        window.Arrange(new Rect(0, 0, 1120, 760));
        window.UpdateLayout();
        Assert.NotNull(window.FindName("StatusRail"));
        Assert.Equal(4, harness.FindStatusSegments(window).Count);
        harness.AssertTextHasContrastingBackground(window);
        harness.RequestExit();
        window.Close();
    });

Add a narrow-width assertion for two columns, a button visual-state/resource
assertion, and a ConfigurationWindow test confirming titles/descriptions/
validation text have non-equal foreground/background and both secrets remain
PasswordBox controls.

- [ ] **Step 3: Run cross-platform gates**

Run on the current environment:

    dotnet restore RetwhoConnector.sln
    dotnet build RetwhoConnector.sln -c Debug --no-restore
    dotnet build RetwhoConnector.sln -c Release --no-restore
    dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj -c Release --no-build
    dotnet format RetwhoConnector.sln --verify-no-changes --no-restore

Expected: zero warnings, cross-platform tests pass, Windows-only project builds
or is safely inert according to OS condition.

- [ ] **Step 4: Run Windows-only automated and manual acceptance**

On Windows:

    dotnet test tests/RetwhoConnector.App.Tests/RetwhoConnector.App.Tests.csproj -c Release
    dotnet publish src/RetwhoConnector.App/RetwhoConnector.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false

Open RetwhoConnector.sln in Visual Studio, set RetwhoConnector.App as startup,
run it, and verify:

- title/description text is visible on every surface;
- all four buttons have distinct normal/hover/pressed/focus/disabled states;
- rail shows green/yellow/red lights plus text/icons for mapped states;
- rail is four columns wide and two-by-two when narrow;
- Settings dialog remains responsive while background logs update;
- tray close/minimize/restore/Exit behavior is unchanged; and
- published RetwhoConnector.exe starts without missing assemblies.

- [ ] **Step 5: Update documentation and tests**

Document compact rail state meanings, button behavior, keyboard focus,
high-contrast guidance, and screenshots/manual acceptance procedure. Add
DocumentationTests assertions for these subjects.

- [ ] **Step 6: Scan and commit**

Run:

    git grep -nE 'T[O]DO|T[B]D|NotImplementedExceptio[n]|3ba5d0[8]1|cookie=[0-9a-fA-F]{16,}'
    git status --short

Expected: no placeholders, copied runtime data, generated bin/obj/publish
content, or unsafe diagnostics.

Then commit:

    git add RetwhoConnector.sln README.md tests/RetwhoConnector.App.Tests/RetwhoConnector.App.Tests.csproj tests/RetwhoConnector.App.Tests/StaTestRunner.cs tests/RetwhoConnector.App.Tests/UiTestHarness.cs tests/RetwhoConnector.App.Tests/WindowRenderingTests.cs tests/RetwhoConnector.Tests/DocumentationTests.cs
    git commit -m "docs: verify modern connector dashboard"

Plan 2 is complete only when all automated gates pass. WPF launch, Visual
Studio, high-contrast, and publish acceptance remain explicitly unverified
until executed on Windows.
