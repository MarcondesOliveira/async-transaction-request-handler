namespace MarcondesOliveira.AsyncTransactions.RequestHandler.Infrastructure;

public sealed record ExternalProcessorOptions(
    Uri BaseUrl,
    Uri CallbackUrl);
