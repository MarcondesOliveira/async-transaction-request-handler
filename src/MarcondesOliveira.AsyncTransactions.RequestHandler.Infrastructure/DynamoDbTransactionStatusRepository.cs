using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using MarcondesOliveira.AsyncTransactions.RequestHandler.Application;

namespace MarcondesOliveira.AsyncTransactions.RequestHandler.Infrastructure;

public sealed class DynamoDbTransactionStatusRepository(
    IAmazonDynamoDB dynamoDb,
    DynamoDbOptions options) : ITransactionStatusRepository
{
    public Task<bool> MarkProcessingAsync(
        Guid transactionId,
        Guid processorTransactionId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken) =>
        UpdateAsync(
            transactionId,
            "SET #status = :processing, processorTransactionId = :processorId, updatedAt = :updatedAt",
            new Dictionary<string, AttributeValue>
            {
                [":processing"] = Text("PROCESSING"),
                [":processorId"] = Text(processorTransactionId.ToString()),
                [":updatedAt"] = Text(FormatDate(updatedAt))
            },
            cancellationToken);

    public Task<bool> MarkFailedAsync(
        Guid transactionId,
        string resultCode,
        string resultMessage,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken) =>
        UpdateAsync(
            transactionId,
            "SET #status = :failed, resultCode = :resultCode, resultMessage = :resultMessage, completedAt = :completedAt, updatedAt = :completedAt",
            new Dictionary<string, AttributeValue>
            {
                [":failed"] = Text("FAILED"),
                [":resultCode"] = Text(resultCode),
                [":resultMessage"] = Text(resultMessage),
                [":completedAt"] = Text(FormatDate(completedAt))
            },
            cancellationToken);

    private async Task<bool> UpdateAsync(
        Guid transactionId,
        string updateExpression,
        Dictionary<string, AttributeValue> values,
        CancellationToken cancellationToken)
    {
        var key = $"TRANSACTION#{transactionId}";
        values[":pending"] = Text("PENDING");

        try
        {
            await dynamoDb.UpdateItemAsync(
                new UpdateItemRequest
                {
                    TableName = options.TableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["PK"] = Text(key),
                        ["SK"] = Text(key)
                    },
                    ConditionExpression = "#status = :pending",
                    UpdateExpression = updateExpression,
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        ["#status"] = "status"
                    },
                    ExpressionAttributeValues = values
                },
                cancellationToken);

            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    private static AttributeValue Text(string value) => new() { S = value };

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
