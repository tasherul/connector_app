using System.Text.Json;
using RetwhoConnector.Core.Models;

namespace RetwhoConnector.Core.Validation;

public static class BridgeActionValidator
{
    public static void Validate(BridgeAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (string.IsNullOrWhiteSpace(action.ActionId) ||
            action.ActionId.Length > 128 ||
            action.ActionId.Any(char.IsControl))
        {
            throw new ArgumentException(
                "actionId must contain 1 to 128 safe characters.",
                nameof(action));
        }

        if (string.IsNullOrWhiteSpace(action.Command) || action.Command.Length > 64)
        {
            throw new ArgumentException(
                "command must contain 1 to 64 characters.",
                nameof(action));
        }

        if (action.Params.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                "params must be a JSON object.",
                nameof(action));
        }

        if (action.Timestamp == default)
        {
            throw new ArgumentException(
                "timestamp is required.",
                nameof(action));
        }
    }
}
