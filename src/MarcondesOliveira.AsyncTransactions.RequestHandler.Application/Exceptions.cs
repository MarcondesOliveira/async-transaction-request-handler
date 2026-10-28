namespace MarcondesOliveira.AsyncTransactions.RequestHandler.Application;

public sealed class InvalidTransactionEventException(string message)
    : Exception(message);

public sealed class ExternalProcessorTransientException(
    string message,
    Exception? innerException = null)
    : Exception(message, innerException);

public sealed class ExternalProcessorRejectedException(
    string code,
    string message)
    : Exception(message)
{
    public string Code { get; } = code;
}
