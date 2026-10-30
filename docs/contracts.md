# Request handler contracts

See the [cross-service reference and state transitions](https://github.com/MarcondesOliveira/async-transaction-infrastructure/blob/main/docs/contracts.md)
for the complete flow, validation differences, callback and retention rules.

## Processing Requests input

The Lambda consumes one JSON event per SQS message:

```json
{
  "eventVersion": 1,
  "eventType": "TransactionRequested",
  "eventId": "2d85549f-3815-4915-8a93-5fdb60c92cef",
  "occurredAt": "2026-10-28T17:00:00Z",
  "transactionId": "2d85549f-3815-4915-8a93-5fdb60c92cef",
  "correlationId": "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee",
  "transactionType": "TRANSFER",
  "amount": {
    "value": "250.75",
    "currency": "BRL"
  },
  "destination": {
    "accountId": "portfolio-account-manual"
  }
}
```

Unknown fields and unsupported event versions are rejected. The SQS message is
reported as a partial batch failure so it can follow the queue redrive policy.

The deployed Pipe uses the transaction UUID as `eventId` and `createdAt` as
`occurredAt`. Although the DTO accepts optional `externalReference`, the current
Pipe omits it; normal submissions therefore carry a null external reference.
The handler's business validator is broader than the main API: it does not
independently enforce `TRANSFER`/`BRL`, the exact decimal format, API length
limits or a non-default UTC `occurredAt`.

## External processor request

```http
POST /v1/transactions
Idempotency-Key: transaction:{transactionId}:submit:v1
X-Correlation-Id: {correlationId}
Content-Type: application/json
```

The body repeats the transaction data and adds the public `callbackUrl`.

## Result classification

| Processor result | Handler action | SQS action |
| --- | --- | --- |
| `202 Accepted` | conditionally set `PROCESSING` | acknowledge |
| `429`, `5xx`, timeout or network failure | no status update | retry |
| any other non-`202` HTTP status | conditionally set `FAILED` | acknowledge |
| malformed/unusable `202` acceptance | no successful record completion | retry |
| invalid SQS event | no status update | retry, then DLQ |

Both DynamoDB transitions require the current status to be `PENDING`. A conditional
failure is treated as an already-resolved race and does not overwrite the existing
state.
