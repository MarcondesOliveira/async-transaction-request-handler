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

        var logger = new TestLambdaLogger();
        var result = await sut.FunctionHandler(sqsEvent, new TestLambdaContext { Logger = logger });

        var failure = Assert.Single(result.BatchItemFailures);
        Assert.Equal("message-retry", failure.ItemIdentifier);
        Assert.Equal(2, processor.ProcessedTransactionIds.Count);
        var logs = logger.Buffer.ToString();
        Assert.Contains("RequestHandled", logs);
        Assert.Contains("SubmissionAccepted", logs);
        Assert.Contains(successful.TransactionId.ToString(), logs);
        Assert.Contains(successful.CorrelationId.ToString(), logs);
        Assert.Contains("RequestProcessingFailed", logs);
        Assert.Contains(retryable.EventId.ToString(), logs);
        Assert.Contains(retryable.TransactionId.ToString(), logs);
        Assert.Contains(retryable.CorrelationId.ToString(), logs);
        Assert.Contains(nameof(ExternalProcessorTransientException), logs);
        Assert.DoesNotContain("Temporary failure", logs);
        Assert.DoesNotContain("portfolio-account-001", logs);
        Assert.DoesNotContain("250.75", logs);
        Assert.DoesNotContain("order-0001", logs);
    }

    [Fact]
    public async Task MalformedMessageIsLoggedWithoutLeakingBodyAndDoesNotStopTheBatch()
    {
        var processor = new SelectiveProcessor(Guid.Empty);
        var sut = new RequestHandlerFunction(processor);
        var logger = new TestLambdaLogger();
        var result = await sut.FunctionHandler(new SQSEvent
        {
            Records =
            [
                new() { MessageId = "malformed", Body = "private-invalid-payload" },
                Message("valid", Transaction())
            ]
        }, new TestLambdaContext { Logger = logger });

        Assert.Equal("malformed", Assert.Single(result.BatchItemFailures).ItemIdentifier);
        Assert.Single(processor.ProcessedTransactionIds);
        var logs = logger.Buffer.ToString();
        Assert.Contains("RequestProcessingFailed", logs);
        Assert.Contains(nameof(JsonException), logs);
        Assert.Contains("malformed", logs);
        Assert.Contains("RequestHandled", logs);
        Assert.DoesNotContain("private-invalid-payload", logs);
    }

    [Fact]
    public async Task PermanentRejectionIsLoggedAsHandledAndIsNotRetried()
    {
        var logger = new TestLambdaLogger();
        var sut = new RequestHandlerFunction(new RejectedProcessor());
        var result = await sut.FunctionHandler(new SQSEvent
        {
            Records = [Message("rejected", Transaction())]
        }, new TestLambdaContext { Logger = logger });

        Assert.Empty(result.BatchItemFailures);
        Assert.Contains("SubmissionRejected", logger.Buffer.ToString());
        Assert.DoesNotContain("RequestProcessingFailed", logger.Buffer.ToString());
    }

    private sealed class RejectedProcessor : ITransactionRequestProcessor
    {
        public Task<RequestHandlingOutcome> HandleAsync(
            TransactionRequested transaction,
            CancellationToken cancellationToken) => Task.FromResult(RequestHandlingOutcome.Failed);
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
