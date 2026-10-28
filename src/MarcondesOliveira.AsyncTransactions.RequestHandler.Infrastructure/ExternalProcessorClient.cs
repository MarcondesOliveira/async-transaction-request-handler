using System.Net;
using System.Net.Http.Json;
using MarcondesOliveira.AsyncTransactions.RequestHandler.Application;

namespace MarcondesOliveira.AsyncTransactions.RequestHandler.Infrastructure;

public sealed class ExternalProcessorClient(
    HttpClient httpClient,
    ExternalProcessorOptions options) : IExternalProcessorClient
{
    public async Task<ProcessorSubmission> SubmitAsync(
        TransactionRequested transaction,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/transactions")
        {
            Content = JsonContent.Create(new ExternalProcessorRequest(
                transaction.TransactionId,
                transaction.CorrelationId,
                transaction.TransactionType,
                transaction.Amount,
                transaction.Destination,
                transaction.ExternalReference,
                options.CallbackUrl))
        };

        request.Headers.TryAddWithoutValidation(
            "Idempotency-Key",
            $"transaction:{transaction.TransactionId}:submit:v1");
        request.Headers.TryAddWithoutValidation(
            "X-Correlation-Id",
            transaction.CorrelationId.ToString());

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException)
        {
            throw new ExternalProcessorTransientException(
                "The external processor is temporarily unavailable.",
                exception);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                var accepted = await response.Content.ReadFromJsonAsync<ExternalProcessorResponse>(
                    cancellationToken);

                return accepted is null || accepted.ProcessorTransactionId == Guid.Empty
                    ? throw new ExternalProcessorTransientException(
                        "The external processor returned an invalid acceptance response.")
                    : new ProcessorSubmission(
                        accepted.ProcessorTransactionId,
                        accepted.AcceptedAt);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests ||
                (int)response.StatusCode >= 500)
            {
                throw new ExternalProcessorTransientException(
                    $"The external processor returned HTTP {(int)response.StatusCode}.");
            }

            throw new ExternalProcessorRejectedException(
                $"PROCESSOR_HTTP_{(int)response.StatusCode}",
                string.IsNullOrWhiteSpace(body)
                    ? "The external processor rejected the transaction."
                    : body);
        }
    }

    private sealed record ExternalProcessorRequest(
        Guid TransactionId,
        Guid CorrelationId,
        string TransactionType,
        Money Amount,
        Destination Destination,
        string? ExternalReference,
        Uri CallbackUrl);

    private sealed record ExternalProcessorResponse(
        Guid ProcessorTransactionId,
        Guid TransactionId,
        string Status,
        DateTimeOffset AcceptedAt);
}
