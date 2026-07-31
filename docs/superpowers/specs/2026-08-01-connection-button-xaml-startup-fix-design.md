# Connection Button XAML Startup Fix Design

## Problem

`MainWindow.InitializeComponent()` fails while resolving
`ConnectionButtonStyle` with:

```text
XamlParseException: Provide value on StaticResourceExtension threw an exception.
InvalidOperationException: Must have non-null value for Binding.
```

The failure prevents the main window from being constructed.

## Root Cause

`ConnectionButtonStyle` uses two `MultiDataTrigger` instances to apply the
Disconnect hover and pressed palettes. Each trigger mixes a data-bound
condition for `ConnectionActionText` with a property-only condition:

```xml
<Condition Binding="{Binding ConnectionActionText}" Value="Disconnect" />
<Condition Property="IsMouseOver" Value="True" />
```

A `MultiDataTrigger` requires every condition to supply a non-null `Binding`.
The property-only condition is valid for `MultiTrigger`, not
`MultiDataTrigger`. WPF encounters the invalid condition when the static style
is resolved for the connection button and throws during XAML loading.

## Repair

Keep the existing `MultiDataTrigger` design and bind the interaction
conditions to the styled button itself:

```xml
<Condition Binding="{Binding IsMouseOver, RelativeSource={RelativeSource Self}}"
           Value="True" />
```

Use the equivalent self binding for `IsPressed`. This is the smallest repair
that preserves all current behavior:

- green Connect normal/hover/pressed states;
- red Disconnect normal/hover/pressed states;
- dynamic connection text and automation name;
- existing commands, icons, focus behavior, and disabled states; and
- no new converter, ViewModel property, or code-behind.

## Regression Test

Add a cross-platform XAML contract test that loads `Controls.xaml` and inspects
every `MultiDataTrigger`. Each direct `Condition` must contain a non-empty
`Binding` attribute and must not contain a `Property` attribute.

The test must fail against the current style because the `IsMouseOver` and
`IsPressed` conditions have no binding. It must pass after the two conditions
use `RelativeSource Self`.

Retain the existing theme tests that assert Disconnect-specific hover and
pressed brushes, ensuring the startup repair does not flatten the semantic
button states.

## Verification

Run:

1. the focused WPF theme/startup contract tests;
2. the Debug WPF application build;
3. Debug and Release solution builds with zero warnings;
4. the complete Release test suite;
5. formatting and diff checks;
6. explicit Windows-conditional test-project restore/build; and
7. the self-contained, single-file, untrimmed `win-x64` publish.

The current Linux environment can verify contracts, cross-target compilation,
and publishing. Actual WPF startup remains a Windows acceptance gate and must
not be reported as executed here.

## Scope

Change only:

- `src/RetwhoConnector.App/Styles/Controls.xaml`; and
- the corresponding WPF theme contract test.

Do not alter connection orchestration, Socket.IO behavior, status mappings,
visual palettes, or the approved web-app integration documentation design.
