using System.Net;
using System.Text;
using System.Text.Json;
using MarcondesOliveira.AsyncTransactions.RequestHandler.Application;
using MarcondesOliveira.AsyncTransactions.RequestHandler.Infrastructure;

namespace MarcondesOliveira.AsyncTransactions.RequestHandler.IntegrationTests;

public sealed class ExternalProcessorClientTests
{
    [Fact]
    public async Task SubmitAsyncSendsContractHeadersAndCallbackUrl()
    {
        var transaction = Transaction();
        HttpRequestMessage? captured = null;
        string? body = null;
        var processorTransactionId = Guid.NewGuid();
        using var handler = new DelegateHandler(async request =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync();
            return JsonResponse(
                HttpStatusCode.Accepted,
                $$"""
                {
                  "processorTransactionId": "{{processorTransactionId}}",
                  "transactionId": "{{transaction.TransactionId}}",
                  "status": "ACCEPTED",
                  "acceptedAt": "2026-10-28T17:01:00Z"
                }
                """);
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://processor.example/")
        };
        var sut = new ExternalProcessorClient(
            httpClient,
            new ExternalProcessorOptions(
                httpClient.BaseAddress,
                new Uri("https://callback.example/v1/callbacks/transactions")));

        var result = await sut.SubmitAsync(
            transaction,
            CancellationToken.None);

        Assert.Equal(processorTransactionId, result.ProcessorTransactionId);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("https://processor.example/v1/transactions", captured.RequestUri!.ToString());
        Assert.Equal(
            $"transaction:{transaction.TransactionId}:submit:v1",
            captured.Headers.GetValues("Idempotency-Key").Single());
        Assert.Equal(
            transaction.CorrelationId.ToString(),
            captured.Headers.GetValues("X-Correlation-Id").Single());
        using var json = JsonDocument.Parse(body!);
        Assert.Equal(
            "https://callback.example/v1/callbacks/transactions",
            json.RootElement.GetProperty("callbackUrl").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task SubmitAsyncClassifiesRetryableResponsesAsTransient(
        HttpStatusCode statusCode)
    {
        using var handler = new DelegateHandler(_ => Task.FromResult(
            new HttpResponseMessage(statusCode)));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://processor.example/")
        };
        var sut = new ExternalProcessorClient(
            httpClient,
            new ExternalProcessorOptions(
                httpClient.BaseAddress,
                new Uri("https://callback.example/transactions")));

        await Assert.ThrowsAsync<ExternalProcessorTransientException>(() =>
            sut.SubmitAsync(Transaction(), CancellationToken.None));
    }

    [Fact]
    public async Task SubmitAsyncClassifiesOther4xxAsPermanentRejection()
    {
        using var handler = new DelegateHandler(_ => Task.FromResult(
            JsonResponse(HttpStatusCode.UnprocessableEntity, "Invalid destination")));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://processor.example/")
        };
        var sut = new ExternalProcessorClient(
            httpClient,
            new ExternalProcessorOptions(
                httpClient.BaseAddress,
                new Uri("https://callback.example/transactions")));

        var exception = await Assert.ThrowsAsync<ExternalProcessorRejectedException>(() =>
            sut.SubmitAsync(Transaction(), CancellationToken.None));

        Assert.Equal("PROCESSOR_HTTP_422", exception.Code);
        Assert.Equal("Invalid destination", exception.Message);
    }

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

    private static HttpResponseMessage JsonResponse(
        HttpStatusCode statusCode,
        string content) =>
        new(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request);
    }
}
