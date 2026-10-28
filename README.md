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
MarcondesOliveira.AsyncTransactions.RequestHandler.Host::MarcondesOliveira.AsyncTransactions.RequestHandler.Host.RequestHandlerFunction::FunctionHandler
```

## Local validation

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-build
```

No AWS resource is created by building or testing this repository. SQS, EventBridge
Pipes, IAM and deployment configuration belong to the infrastructure repository and
are introduced in the next project stage.
