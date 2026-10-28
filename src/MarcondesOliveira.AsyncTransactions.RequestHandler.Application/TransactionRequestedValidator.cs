using System.Globalization;

namespace MarcondesOliveira.AsyncTransactions.RequestHandler.Application;

public static class TransactionRequestedValidator
{
    public static void Validate(TransactionRequested transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        if (transaction.EventVersion != 1 ||
            !string.Equals(transaction.EventType, "TransactionRequested", StringComparison.Ordinal) ||
            transaction.EventId == Guid.Empty ||
            transaction.TransactionId == Guid.Empty ||
            transaction.CorrelationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(transaction.TransactionType) ||
            transaction.Amount is null ||
            !decimal.TryParse(
                transaction.Amount.Value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var amount) ||
            amount <= 0 ||
            transaction.Amount.Currency.Length != 3 ||
            transaction.Destination is null ||
            string.IsNullOrWhiteSpace(transaction.Destination.AccountId))
        {
            throw new InvalidTransactionEventException(
                "The TransactionRequested event is invalid or uses an unsupported version.");
        }
    }
}
