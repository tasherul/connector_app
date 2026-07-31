# Main Window Black Header Text Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render the main dashboard title and subtitle in black regardless of theme resources.

**Architecture:** Keep the change local to `MainWindow.xaml` by assigning an explicit `Foreground="Black"` to the two existing header `TextBlock` elements. Protect the visual contract with the existing linked-XAML test fixture.

**Tech Stack:** WPF XAML, .NET 10, xUnit, LINQ to XML.

## Global Constraints

- Keep the existing title and subtitle text, typography, margins, and layout.
- Do not change shared theme resources or any unrelated text color.
- Both header labels must use the literal XAML value `Foreground="Black"`.
- Work directly on `main`, as previously requested by the user.

---

### Task 1: Make Both Dashboard Header Labels Black

**Files:**
- Modify: `tests/RetwhoConnector.Tests/WpfThemeContractTests.cs`
- Modify: `src/RetwhoConnector.App/MainWindow.xaml:139`

**Interfaces:**
- Consumes: the existing linked `Fixtures/MainWindow.xaml` test fixture.
- Produces: two header `TextBlock` elements whose `Foreground` attribute is exactly `Black`.

- [ ] **Step 1: Write the failing XAML contract test**

Add this test to `WpfThemeContractTests`:

```csharp
[Fact]
public void DashboardHeader_TitleAndSubtitleUseBlackForeground()
{
    XDocument mainWindow = LoadFixture("MainWindow.xaml");
    string[] headerLabels =
    [
        "Hybrid Edge Connector Agent",
        "Secure local POS to Retwho cloud bridge",
    ];

    foreach (string label in headerLabels)
    {
        XElement textBlock = Assert.Single(
            mainWindow.Descendants(Presentation + "TextBlock"),
            element => element.Attribute("Text")?.Value == label);
        Assert.Equal("Black", textBlock.Attribute("Foreground")?.Value);
    }
}
```

- [ ] **Step 2: Run the test and verify RED**

Run:

```bash
PATH=/tmp/retwho-dotnet-10:$PATH dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj --filter FullyQualifiedName~DashboardHeader_TitleAndSubtitleUseBlackForeground
```

Expected: FAIL because the title has no foreground and the subtitle uses `SecondaryTextBrush`.

- [ ] **Step 3: Implement the minimal XAML change**

Update only the two header elements:

```xml
<TextBlock Text="Hybrid Edge Connector Agent"
           Foreground="Black"
           FontSize="28"
           FontWeight="SemiBold" />
<TextBlock Text="Secure local POS to Retwho cloud bridge"
           Foreground="Black"
           FontSize="15"
           Margin="0,4,0,0" />
```

- [ ] **Step 4: Verify focused and full checks**

Run:

```bash
PATH=/tmp/retwho-dotnet-10:$PATH dotnet test tests/RetwhoConnector.Tests/RetwhoConnector.Tests.csproj --filter FullyQualifiedName~DashboardHeader_TitleAndSubtitleUseBlackForeground
PATH=/tmp/retwho-dotnet-10:$PATH dotnet build src/RetwhoConnector.App/RetwhoConnector.App.csproj -c Debug
PATH=/tmp/retwho-dotnet-10:$PATH dotnet test RetwhoConnector.sln -c Release
PATH=/tmp/retwho-dotnet-10:$PATH dotnet format RetwhoConnector.sln --verify-no-changes --no-restore
git diff --check
```

Expected: focused test passes, build succeeds with zero warnings/errors, full suite passes, format and diff checks pass.

- [ ] **Step 5: Commit**

```bash
git add src/RetwhoConnector.App/MainWindow.xaml tests/RetwhoConnector.Tests/WpfThemeContractTests.cs
git commit -m "fix: make dashboard header text black"
```

