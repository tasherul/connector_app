using System.Globalization;
using System.Windows;
using System.Windows.Media;
using RetwhoConnector.App.Converters;
using RetwhoConnector.App.ViewModels;

namespace RetwhoConnector.Tests;

public sealed class DashboardPresentationConverterTests
{
    [Theory]
    [InlineData(DashboardSignal.Healthy, "SuccessBrush")]
    [InlineData(DashboardSignal.Warning, "WarningBrush")]
    [InlineData(DashboardSignal.Error, "ErrorBrush")]
    public void DashboardSignalConverter_UsesOnlySemanticSignalValues(
        DashboardSignal signal,
        string expectedResourceKey)
    {
        var application = new Application();
        var expectedBrush = new Brush();
        application.SetResource(expectedResourceKey, expectedBrush);
        Application.Current = application;

        try
        {
            var converter = new DashboardSignalToBrushConverter();

            Assert.Same(
                expectedBrush,
                converter.Convert(
                    signal,
                    typeof(Brush),
                    null!,
                    CultureInfo.InvariantCulture));
        }
        finally
        {
            Application.Current = null;
        }
    }

    [Theory]
    [InlineData(919d, 2)]
    [InlineData(920d, 4)]
    public void WidthConverter_UsesTheApprovedBreakpoint(
        double width,
        int expectedColumns)
    {
        Assert.Equal(
            expectedColumns,
            WidthToStatusColumnCountConverter.GetColumnCount(width));
    }

    [Fact]
    public void WidthConverter_UsesTwoColumnsForNonFiniteAndNonDoubleInputs()
    {
        var converter = new WidthToStatusColumnCountConverter();

        object[] values = [double.NaN, double.PositiveInfinity, double.NegativeInfinity, "920"];

        Assert.All(values, value =>
            Assert.Equal(
                2,
                converter.Convert(
                    value,
                    typeof(int),
                    null!,
                    CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void DashboardSignalConverter_RejectsConvertBack()
    {
        var converter = new DashboardSignalToBrushConverter();

        Assert.Throws<NotSupportedException>(() =>
            converter.ConvertBack(
                "SuccessBrush",
                typeof(DashboardSignal),
                null!,
                CultureInfo.InvariantCulture));
    }

    [Fact]
    public void WidthConverter_RejectsConvertBack()
    {
        var converter = new WidthToStatusColumnCountConverter();

        Assert.Throws<NotSupportedException>(() =>
            converter.ConvertBack(
                4,
                typeof(double),
                null!,
                CultureInfo.InvariantCulture));
    }
}
