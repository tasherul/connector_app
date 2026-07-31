using RetwhoConnector.Core.Services;

namespace RetwhoConnector.Tests;

public sealed class StartupMarkerServiceTests
{
    [Fact]
    public void BeginSession_ReportsPreviousMarkerAndCleanExitDeletesIt()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"RetwhoConnectorMarker-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "agent.running");
        try
        {
            var first = new StartupMarkerService(
                path,
                new FixedTimeProvider());

            Assert.False(first.BeginSession());
            Assert.True(File.Exists(path));
            string marker = File.ReadAllText(path);
            Assert.DoesNotContain("FAKE_", marker, StringComparison.Ordinal);
            Assert.Contains("2026-07-31T08:20:01", marker, StringComparison.Ordinal);

            var restarted = new StartupMarkerService(
                path,
                new FixedTimeProvider());

            Assert.True(restarted.BeginSession());
            restarted.CompleteSession();
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void CompleteSession_WithoutBegin_DoesNotDeleteAnotherSessionMarker()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"RetwhoConnectorMarker-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "agent.running");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, "existing safe marker");
            var service = new StartupMarkerService(
                path,
                new FixedTimeProvider());

            service.CompleteSession();

            Assert.True(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            DateTimeOffset.Parse("2026-07-31T08:20:01.250Z");
    }
}
