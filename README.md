# Async Transaction Request Handler

AWS Lambda that consumes transaction requests from SQS and submits them to the
simulated external processor in the Async Transaction Processing portfolio project.

## Responsibilities

- consume the versioned `TransactionRequested` event;
- validate the event before performing side effects;
- call `POST /v1/transactions` on the external processor;
- propagate the transaction correlation ID;
- send `transaction:{transactionId}:submit:v1` as the external idempotency key;
- update the DynamoDB transaction from `PENDING` to `PROCESSING` after `202 Accepted`;
- update the transaction from `PENDING` to `FAILED` after a permanent processor rejection;
- return partial SQS batch failures for timeouts, `429`, `5xx` and invalid messages.

The conditional DynamoDB update only accepts the `PENDING` source status. This
prevents a late request-handler update from overwriting a final callback result.

## Projects

- `Application`: input contract, validation and orchestration;
- `Infrastructure`: external HTTP client and DynamoDB status repository;
- `Host`: AWS Lambda SQS entry point with partial batch response;
- `UnitTests`: orchestration and failure classification behavior;
- `IntegrationTests`: external HTTP contract and headers;
- `ContractTests`: versioned SQS event compatibility.

## Configuration

| Environment variable | Purpose |
| --- | --- |
| `ExternalProcessor__BaseUrl` | Base URL of the simulated external processor |
| `Callback__Url` | Public callback URL sent to the processor |
| `DynamoDb__TableName` | Transaction table name |

The Lambda handler is:

```text
AsyncTransaction.Handler::MarcondesOliveira.AsyncTransactions.RequestHandler.Host.RequestHandlerFunction::FunctionHandler
```

## Operational logs

The handler emits `RequestHandled` after a submission is accepted or permanently
rejected, and `RequestProcessingFailed` when a message must be retried. Logs contain
the SQS message ID and, when deserialization succeeds, event, transaction and
correlation IDs. Malformed bodies remain traceable through the SQS message ID.

`SubmissionAccepted` describes the processor response, not a guarantee that the
transaction is still `PROCESSING`: a callback may already have finalized it.
Permanent rejections are handled outcomes, not SQS batch failures.

Payloads, amounts, account IDs, external references and exception messages are not
logged. Failures include exception and root-cause types plus the stack trace;
exception messages can contain external response bodies or invalid input.
Named log parameters use the native `ILambdaLogger` API. Structured JSON output
requires the Lambda logging format to be configured in the infrastructure repository;
this code change alone does not enable it in AWS.

After deployment, manually create a transaction with Bruno or Swagger and search
the request-handler CloudWatch log group for its `transactionId`. Check for
`RequestHandled` and the same `correlationId`. A controlled timeout should produce
`RequestProcessingFailed` for each attempt, without exposing the request body.

## Local validation

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-build
```

No AWS resource is created by building or testing this repository. SQS, EventBridge
Pipes, IAM and deployment configuration belong to the infrastructure repository and
are introduced in the next project stage.
