using MarcondesOliveira.AsyncTransactions.RequestHandler.Application;

namespace MarcondesOliveira.AsyncTransactions.RequestHandler.UnitTests;

public sealed class TransactionRequestProcessorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 28, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsyncMarksTransactionAsProcessingWhenProcessorAccepts()
    {
        var processorTransactionId = Guid.NewGuid();
        var external = new StubExternalProcessor(
            new ProcessorSubmission(processorTransactionId, Now));
        var repository = new RecordingRepository();
        var sut = new TransactionRequestProcessor(
            external,
            repository,
            new FixedTimeProvider(Now));
        var transaction = ValidTransaction();

        var outcome = await sut.HandleAsync(transaction, CancellationToken.None);

        Assert.Equal(RequestHandlingOutcome.Processing, outcome);
        Assert.Equal(
            (transaction.TransactionId, processorTransactionId, Now),
            repository.ProcessingUpdate);
        Assert.Null(repository.FailedUpdate);
    }

    [Fact]
    public async Task HandleAsyncMarksTransactionAsFailedWhenProcessorRejects()
    {
        var external = new StubExternalProcessor(
            new ExternalProcessorRejectedException("INVALID_DESTINATION", "Invalid account"));
        var repository = new RecordingRepository();
        var sut = new TransactionRequestProcessor(
            external,
            repository,
            new FixedTimeProvider(Now));
        var transaction = ValidTransaction();

        var outcome = await sut.HandleAsync(transaction, CancellationToken.None);

        Assert.Equal(RequestHandlingOutcome.Failed, outcome);
        Assert.Equal(
            (transaction.TransactionId, "INVALID_DESTINATION", "Invalid account", Now),
            repository.FailedUpdate);
        Assert.Null(repository.ProcessingUpdate);
    }

    [Fact]
    public async Task HandleAsyncPropagatesTransientFailureForSqsRetry()
    {
        var external = new StubExternalProcessor(
            new ExternalProcessorTransientException("Timeout"));
        var sut = new TransactionRequestProcessor(
            external,
            new RecordingRepository(),
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<ExternalProcessorTransientException>(() =>
            sut.HandleAsync(ValidTransaction(), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsyncRejectsUnsupportedContractVersion()
    {
        var sut = new TransactionRequestProcessor(
            new StubExternalProcessor(new ProcessorSubmission(Guid.NewGuid(), Now)),
            new RecordingRepository(),
            new FixedTimeProvider(Now));
        var invalid = ValidTransaction() with { EventVersion = 2 };

        await Assert.ThrowsAsync<InvalidTransactionEventException>(() =>
            sut.HandleAsync(invalid, CancellationToken.None));
    }

    private static TransactionRequested ValidTransaction() =>
        new(
            1,
            "TransactionRequested",
            Guid.NewGuid(),
            Now,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "TRANSFER",
            new Money("250.75", "BRL"),
            new Destination("portfolio-account-001"),
            "order-0001");

    private sealed class StubExternalProcessor : IExternalProcessorClient
    {
        private readonly ProcessorSubmission? response;
        private readonly Exception? exception;

        public StubExternalProcessor(ProcessorSubmission response)
        {
            this.response = response;
        }

        public StubExternalProcessor(Exception exception)
        {
            this.exception = exception;
        }

        public Task<ProcessorSubmission> SubmitAsync(
            TransactionRequested transaction,
            CancellationToken cancellationToken) =>
            exception is null
                ? Task.FromResult(response!)
                : Task.FromException<ProcessorSubmission>(exception);
    }

    private sealed class RecordingRepository : ITransactionStatusRepository
    {
        public (Guid TransactionId, Guid ProcessorTransactionId, DateTimeOffset UpdatedAt)? ProcessingUpdate
        {
            get;
            private set;
        }

        public (Guid TransactionId, string Code, string Message, DateTimeOffset CompletedAt)? FailedUpdate
        {
            get;
            private set;
        }

        public Task<bool> MarkProcessingAsync(
            Guid transactionId,
            Guid processorTransactionId,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken)
        {
            ProcessingUpdate = (transactionId, processorTransactionId, updatedAt);
            return Task.FromResult(true);
        }

        public Task<bool> MarkFailedAsync(
            Guid transactionId,
            string resultCode,
            string resultMessage,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken)
        {
            FailedUpdate = (transactionId, resultCode, resultMessage, completedAt);
            return Task.FromResult(true);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
