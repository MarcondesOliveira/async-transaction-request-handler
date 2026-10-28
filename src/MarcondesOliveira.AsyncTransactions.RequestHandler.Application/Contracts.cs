namespace MarcondesOliveira.AsyncTransactions.RequestHandler.Application;

public sealed record TransactionRequested(
    int EventVersion,
    string EventType,
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid TransactionId,
    Guid CorrelationId,
    string TransactionType,
    Money Amount,
    Destination Destination,
    string? ExternalReference);

public sealed record Money(string Value, string Currency);

public sealed record Destination(string AccountId);

public sealed record ProcessorSubmission(
    Guid ProcessorTransactionId,
    DateTimeOffset AcceptedAt);

public enum RequestHandlingOutcome
{
    Processing,
    Failed
}
