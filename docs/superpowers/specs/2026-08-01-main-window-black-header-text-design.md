# Main Window Black Header Text Design

## Goal

Make the main dashboard title and subtitle readable on the current light
background by rendering both labels in black.

## Design

In `src/RetwhoConnector.App/MainWindow.xaml`, set `Foreground="Black"`
directly on these two `TextBlock` elements:

- `Hybrid Edge Connector Agent`
- `Secure local POS to Retwho cloud bridge`

Direct values are intentional: these labels must remain black regardless of
the active theme or the value of `SecondaryTextBrush`.

## Scope

- Keep the existing font sizes, font weight, margins, text, and layout.
- Do not change other text colors or shared theme resources.
- Add a focused XAML contract test covering both header labels.
- Run the focused WPF contract tests, full Release tests, and WPF build.

