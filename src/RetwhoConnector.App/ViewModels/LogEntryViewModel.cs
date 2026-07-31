using System.Globalization;
using RetwhoConnector.Core.Models;

namespace RetwhoConnector.App.ViewModels;

public sealed class LogEntryViewModel
{
    public LogEntryViewModel(LogEntry entry)
    {
        LocalTime = entry.TimestampUtc
            .ToLocalTime()
            .ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        Category = entry.Category;
        CategoryText = entry.Category.ToString();
        Message = entry.Message;
        Details = entry.Details;
        CorrelationId = entry.CorrelationId;
    }

    public string LocalTime { get; }
    public AgentLogCategory Category { get; }
    public string CategoryText { get; }
    public string Message { get; }
    public string? Details { get; }
    public string? CorrelationId { get; }
}
