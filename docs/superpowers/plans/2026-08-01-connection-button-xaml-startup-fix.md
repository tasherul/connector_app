# Connection Button XAML Startup Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `MainWindow` load successfully by replacing invalid property-only conditions inside `ConnectionButtonStyle` multi-data triggers with self bindings.

**Architecture:** Keep the existing WPF style, button-state palettes, ViewModel contract, and code-behind unchanged. Add a cross-platform XML contract test that enforces WPF's `MultiDataTrigger` condition rule, then make the two minimal XAML edits required by that test.

**Tech Stack:** C# 14, .NET 10, WPF XAML, LINQ to XML, xUnit.

## Global Constraints

- Work directly on `main`, as requested by the user.
- Follow red-green-refactor and observe the regression test fail before editing production XAML.
- Change only `Controls.xaml` and `WpfThemeContractTests.cs`.
- Preserve Connect and Disconnect normal, hover, pressed, disabled, icon, focus, command, text, and automation behavior.
- Do not add a converter, ViewModel property, code-behind, package, or runtime logging.
- Actual WPF launch remains a Windows acceptance gate; Linux verification must not be described as a Windows launch.

---

### Task 1: Repair the invalid connection-button multi-data triggers

**Files:**
- Modify: `tests/RetwhoConnector.Tests/WpfThemeContractTests.cs`
- Modify: `src/RetwhoConnector.App/Styles/Controls.xaml:209-220`

**Interfaces:**
- Consumes: `ConnectionActionText`, `Button.IsMouseOver`, `Button.IsPressed`, and the existing Disconnect palette resources.
- Produces: a loadable `ConnectionButtonStyle` with valid `MultiDataTrigger.Condition.Binding` values.

- [ ] **Step 1: Add the failing XAML contract test**

Add this test to `WpfThemeContractTests`:

```csharp
[Fact]
public void MultiDataTriggers_UseBindingsForEveryCondition()
{
    XDocument controls = LoadFixture("Controls.xaml");
    XElement[] triggers = controls
        .Descendants(Presentation + "MultiDataTrigger")
        .ToArray();

    Assert.NotEmpty(triggers);
    Assert.All(triggers, trigger =>
    {
        XElement[] conditions = trigger
            .Descendants(Presentation + "Condition")
            .ToArray();

        Assert.NotEmpty(conditions);
        Assert.All(conditions, condition =>
        {
            Assert.False(
                string.IsNullOrWhiteSpace(condition.Attribute("Binding")?.Value),
                "Every MultiDataTrigger condition must provide a Binding.");
            Assert.Null(condition.Attribute("Property"));
        });
    });
}
```

This tests the actual copied `Controls.xaml` fixture used by the application.

- [ ] **Step 2: Run the focused test and verify red**

Run:

```bash
dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj \
  --filter FullyQualifiedName~MultiDataTriggers_UseBindingsForEveryCondition
```

Expected: FAIL because the current `IsMouseOver` and `IsPressed` conditions
have `Property` attributes and no `Binding` attributes.

- [ ] **Step 3: Make the minimal XAML repair**

In the Disconnect hover trigger, replace:

```xml
<Condition Property="IsMouseOver" Value="True" />
```

with:

```xml
<Condition Binding="{Binding IsMouseOver, RelativeSource={RelativeSource Self}}"
           Value="True" />
```

In the Disconnect pressed trigger, replace:

```xml
<Condition Property="IsPressed" Value="True" />
```

with:

```xml
<Condition Binding="{Binding IsPressed, RelativeSource={RelativeSource Self}}"
           Value="True" />
```

Do not change the `ConnectionActionText` conditions or any setters.

- [ ] **Step 4: Run focused tests and verify green**

Run:

```bash
dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj \
  --filter "FullyQualifiedName~WpfThemeContractTests|FullyQualifiedName~WpfStartupTests"
```

Expected: all selected tests pass, including existing Disconnect palette,
icon, automation, contrast, and startup contracts.

- [ ] **Step 5: Compile the actual WPF project**

Run:

```bash
dotnet build src/RetwhoConnector.App/RetwhoConnector.App.csproj -c Debug
```

Expected: WPF XAML compilation succeeds with zero warnings and errors.

- [ ] **Step 6: Run complete cross-platform gates**

Run:

```bash
dotnet restore RetwhoConnector.sln --locked-mode
dotnet build RetwhoConnector.sln -c Debug --no-restore
dotnet build RetwhoConnector.sln -c Release --no-restore
dotnet test RetwhoConnector.sln -c Release --no-build
dotnet format RetwhoConnector.sln --verify-no-changes --no-restore
git diff --check
```

Expected: restore succeeds, both builds have zero warnings/errors, the complete
test suite passes, and format/diff checks are clean.

- [ ] **Step 7: Compile the Windows-conditional STA test project**

Run the required conditional restore before the no-restore build:

```bash
dotnet restore tests/RetwhoConnector.App.Tests/RetwhoConnector.App.Tests.csproj \
  -p:OS=Windows_NT
dotnet build tests/RetwhoConnector.App.Tests/RetwhoConnector.App.Tests.csproj \
  -c Release --no-restore -p:OS=Windows_NT
```

Expected: the real Windows-only test sources compile with zero warnings and
errors. Do not claim that they executed on Linux.

- [ ] **Step 8: Republish the Windows executable**

Run:

```bash
dotnet publish src/RetwhoConnector.App/RetwhoConnector.App.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:PublishTrimmed=false
```

Expected: a PE32+ x64 GUI executable is written under
`src/RetwhoConnector.App/bin/Release/net10.0-windows/win-x64/publish/`.

- [ ] **Step 9: Scan and commit**

Run:

```bash
git diff --check
git status --short
git diff -- src/RetwhoConnector.App/Styles/Controls.xaml \
  tests/RetwhoConnector.Tests/WpfThemeContractTests.cs
```

Confirm only the regression test and the two trigger conditions changed, then
commit:

```bash
git add src/RetwhoConnector.App/Styles/Controls.xaml \
  tests/RetwhoConnector.Tests/WpfThemeContractTests.cs
git commit -m "fix: repair connection button XAML trigger"
```

### Windows Acceptance

On Windows, open `RetwhoConnector.sln`, run `RetwhoConnector.App`, and confirm:

- `MainWindow.InitializeComponent()` completes without `XamlParseException`;
- the Connect button is green and uses its configured hover/pressed states;
- after connection, the Disconnect button is red and uses its configured
  hover/pressed states; and
- the dynamic accessible name announces Connect and Disconnect correctly.

If this Windows check has not been run, report it as unverified rather than
claiming startup success.
