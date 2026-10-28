using System.Text.Json;
using System.Text.Json.Serialization;
using MarcondesOliveira.AsyncTransactions.RequestHandler.Application;

namespace MarcondesOliveira.AsyncTransactions.RequestHandler.ContractTests;

public sealed class TransactionRequestedContractTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    [Fact]
    public void DeserializeAcceptsVersionOneContract()
    {
        var transactionId = Guid.NewGuid();
        var json = $$"""
        {
          "eventVersion": 1,
          "eventType": "TransactionRequested",
          "eventId": "9b8f1777-6e52-4a93-a824-f5d438ae4d86",
          "occurredAt": "2026-10-28T17:00:00Z",
          "transactionId": "{{transactionId}}",
          "correlationId": "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee",
          "transactionType": "TRANSFER",
          "amount": {
            "value": "250.75",
            "currency": "BRL"
          },
          "destination": {
            "accountId": "portfolio-account-001"
          },
          "externalReference": "order-0001"
        }
        """;

        var result = JsonSerializer.Deserialize<TransactionRequested>(json, Options);

        Assert.NotNull(result);
        Assert.Equal(transactionId, result.TransactionId);
        Assert.Equal("250.75", result.Amount.Value);
        Assert.Equal("portfolio-account-001", result.Destination.AccountId);
    }

    [Fact]
    public void DeserializeRejectsUnknownFields()
    {
        const string json = """
        {
          "eventVersion": 1,
          "unsupportedField": true
        }
        """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<TransactionRequested>(json, Options));
    }
}
