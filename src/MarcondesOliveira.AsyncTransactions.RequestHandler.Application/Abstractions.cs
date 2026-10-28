namespace MarcondesOliveira.AsyncTransactions.RequestHandler.Application;

public interface IExternalProcessorClient
{
    Task<ProcessorSubmission> SubmitAsync(
        TransactionRequested transaction,
        CancellationToken cancellationToken);
}

public interface ITransactionStatusRepository
{
    Task<bool> MarkProcessingAsync(
        Guid transactionId,
        Guid processorTransactionId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken);

    Task<bool> MarkFailedAsync(
        Guid transactionId,
        string resultCode,
        string resultMessage,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);
}

public interface ITransactionRequestProcessor
{
    Task<RequestHandlingOutcome> HandleAsync(
        TransactionRequested transaction,
        CancellationToken cancellationToken);
}
