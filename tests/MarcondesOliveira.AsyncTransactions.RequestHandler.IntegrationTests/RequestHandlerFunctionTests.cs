using System.Text.Json;
using Amazon.Lambda.SQSEvents;
using Amazon.Lambda.TestUtilities;
using MarcondesOliveira.AsyncTransactions.RequestHandler.Application;
using MarcondesOliveira.AsyncTransactions.RequestHandler.Host;

namespace MarcondesOliveira.AsyncTransactions.RequestHandler.IntegrationTests;

public sealed class RequestHandlerFunctionTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task FunctionHandlerReturnsOnlyFailedSqsMessages()
    {
        var successful = Transaction();
        var retryable = Transaction();
        var processor = new SelectiveProcessor(retryable.TransactionId);
        var sut = new RequestHandlerFunction(processor);
        var sqsEvent = new SQSEvent
        {
            Records =
            [
                Message("message-success", successful),
                Message("message-retry", retryable)
            ]
        };

        var result = await sut.FunctionHandler(sqsEvent, new TestLambdaContext());

        var failure = Assert.Single(result.BatchItemFailures);
        Assert.Equal("message-retry", failure.ItemIdentifier);
        Assert.Equal(2, processor.ProcessedTransactionIds.Count);
    }

    private static SQSEvent.SQSMessage Message(
        string messageId,
        TransactionRequested transaction) =>
        new()
        {
            MessageId = messageId,
            Body = JsonSerializer.Serialize(transaction, JsonOptions)
        };

    private static TransactionRequested Transaction() =>
        new(
            1,
            "TransactionRequested",
            Guid.NewGuid(),
            new DateTimeOffset(2026, 10, 28, 17, 0, 0, TimeSpan.Zero),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "TRANSFER",
            new Money("250.75", "BRL"),
            new Destination("portfolio-account-001"),
            "order-0001");

    private sealed class SelectiveProcessor(Guid transactionIdToFail)
        : ITransactionRequestProcessor
    {
        public List<Guid> ProcessedTransactionIds { get; } = [];

        public Task<RequestHandlingOutcome> HandleAsync(
            TransactionRequested transaction,
            CancellationToken cancellationToken)
        {
            ProcessedTransactionIds.Add(transaction.TransactionId);

            return transaction.TransactionId == transactionIdToFail
                ? Task.FromException<RequestHandlingOutcome>(
                    new ExternalProcessorTransientException("Temporary failure"))
                : Task.FromResult(RequestHandlingOutcome.Processing);
        }
    }
}
