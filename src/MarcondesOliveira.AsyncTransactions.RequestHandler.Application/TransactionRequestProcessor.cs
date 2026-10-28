namespace MarcondesOliveira.AsyncTransactions.RequestHandler.Application;

public sealed class TransactionRequestProcessor(
    IExternalProcessorClient externalProcessor,
    ITransactionStatusRepository transactionRepository,
    TimeProvider timeProvider) : ITransactionRequestProcessor
{
    public async Task<RequestHandlingOutcome> HandleAsync(
        TransactionRequested transaction,
        CancellationToken cancellationToken)
    {
        TransactionRequestedValidator.Validate(transaction);

        try
        {
            var submission = await externalProcessor.SubmitAsync(
                transaction,
                cancellationToken);

            await transactionRepository.MarkProcessingAsync(
                transaction.TransactionId,
                submission.ProcessorTransactionId,
                timeProvider.GetUtcNow(),
                cancellationToken);

            return RequestHandlingOutcome.Processing;
        }
        catch (ExternalProcessorRejectedException exception)
        {
            await transactionRepository.MarkFailedAsync(
                transaction.TransactionId,
                exception.Code,
                exception.Message,
                timeProvider.GetUtcNow(),
                cancellationToken);

            return RequestHandlingOutcome.Failed;
        }
    }
}
