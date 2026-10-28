using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using MarcondesOliveira.AsyncTransactions.RequestHandler.Application;
using MarcondesOliveira.AsyncTransactions.RequestHandler.Infrastructure;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace MarcondesOliveira.AsyncTransactions.RequestHandler.Host;

public sealed class RequestHandlerFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly ITransactionRequestProcessor processor;

    public RequestHandlerFunction()
        : this(CreateProcessor())
    {
    }

    public RequestHandlerFunction(ITransactionRequestProcessor processor)
    {
        this.processor = processor;
    }

    public async Task<SQSBatchResponse> FunctionHandler(
        SQSEvent sqsEvent,
        ILambdaContext context)
    {
        var failures = new List<SQSBatchResponse.BatchItemFailure>();

        foreach (var record in sqsEvent.Records)
        {
            try
            {
                var transaction = JsonSerializer.Deserialize<TransactionRequested>(
                    record.Body,
                    JsonOptions) ?? throw new JsonException("SQS message body is empty.");

                await processor.HandleAsync(transaction, CancellationToken.None);
            }
            catch (Exception exception)
            {
                context.Logger.LogError(
                    "Failed to process SQS message {MessageId}: {Exception}",
                    record.MessageId,
                    exception);
                failures.Add(new SQSBatchResponse.BatchItemFailure
                {
                    ItemIdentifier = record.MessageId
                });
            }
        }

        return new SQSBatchResponse(failures);
    }

    private static TransactionRequestProcessor CreateProcessor()
    {
        var processorBaseUrl = RequiredUri("ExternalProcessor__BaseUrl");
        var callbackUrl = RequiredUri("Callback__Url");
        var tableName = RequiredValue("DynamoDb__TableName");
        var httpClient = new HttpClient
        {
            BaseAddress = processorBaseUrl,
            Timeout = TimeSpan.FromSeconds(10)
        };
        var externalProcessor = new ExternalProcessorClient(
            httpClient,
            new ExternalProcessorOptions(processorBaseUrl, callbackUrl));
        var repository = new DynamoDbTransactionStatusRepository(
            new AmazonDynamoDBClient(),
            new DynamoDbOptions(tableName));

        return new TransactionRequestProcessor(
            externalProcessor,
            repository,
            TimeProvider.System);
    }

    private static Uri RequiredUri(string name) =>
        Uri.TryCreate(RequiredValue(name), UriKind.Absolute, out var uri)
            ? uri
            : throw new InvalidOperationException(
                $"Environment variable {name} must be an absolute URI.");

    private static string RequiredValue(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"Environment variable {name} is required.");
}
