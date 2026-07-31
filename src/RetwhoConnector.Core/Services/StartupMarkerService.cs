using System.Text;

namespace RetwhoConnector.Core.Services;

public sealed class StartupMarkerService
{
    private readonly string _markerPath;
    private readonly TimeProvider _timeProvider;
    private string? _ownedMarker;
    private int _sessionStarted;

    public StartupMarkerService(
        string markerPath,
        TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markerPath);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _markerPath = Path.GetFullPath(markerPath);
        _timeProvider = timeProvider;
    }

    public bool BeginSession()
    {
        if (Interlocked.CompareExchange(
                ref _sessionStarted,
                1,
                0) != 0)
        {
            throw new InvalidOperationException(
                "The startup marker session has already begun.");
        }

        try
        {
            bool previousSessionWasUnclean = File.Exists(_markerPath);
            string? directory = Path.GetDirectoryName(_markerPath);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException(
                    "The startup marker path has no parent directory.");
            }

            Directory.CreateDirectory(directory);
            string marker =
                "Hybrid Edge Connector Agent startup marker v1" +
                Environment.NewLine +
                $"startedUtc={_timeProvider.GetUtcNow():O}" +
                Environment.NewLine +
                $"session={Guid.NewGuid():N}" +
                Environment.NewLine;
            using var stream = new FileStream(
                _markerPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4_096,
                FileOptions.WriteThrough);
            using var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 1_024,
                leaveOpen: true);
            writer.Write(marker);
            writer.Flush();
            stream.Flush(flushToDisk: true);
            _ownedMarker = marker;
            return previousSessionWasUnclean;
        }
        catch
        {
            Interlocked.Exchange(ref _sessionStarted, 0);
            throw;
        }
    }

    public void CompleteSession()
    {
        if (Interlocked.Exchange(ref _sessionStarted, 0) == 0)
        {
            return;
        }

        string? ownedMarker = _ownedMarker;
        _ownedMarker = null;
        if (ownedMarker is null || !File.Exists(_markerPath))
        {
            return;
        }

        string currentMarker = File.ReadAllText(_markerPath);
        if (string.Equals(
                currentMarker,
                ownedMarker,
                StringComparison.Ordinal))
        {
            File.Delete(_markerPath);
        }
    }
}
