namespace RetwhoConnector.Core.Exceptions;

public abstract class ConnectorException : Exception
{
    protected ConnectorException(
        string code,
        string safeMessage,
        Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        Code = code;
        SafeMessage = safeMessage;
    }

    public string Code { get; }
    public string SafeMessage { get; }
}

public sealed class PosAuthenticationException(
    string code,
    string safeMessage,
    Exception? innerException = null)
    : ConnectorException(code, safeMessage, innerException);

public sealed class PosCertificateException(
    string code,
    string safeMessage,
    Exception? innerException = null)
    : ConnectorException(code, safeMessage, innerException);

public sealed class PosResponseException(
    string code,
    string safeMessage,
    Exception? innerException = null)
    : ConnectorException(code, safeMessage, innerException);

public sealed class PosTimeoutException(
    string safeMessage,
    Exception? innerException = null)
    : ConnectorException("POS_TIMEOUT", safeMessage, innerException);

public sealed class SettingsException(
    string code,
    string safeMessage,
    Exception? innerException = null)
    : ConnectorException(code, safeMessage, innerException);
