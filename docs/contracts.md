# Request handler contracts

## Processing Requests input

The Lambda consumes one JSON event per SQS message:

```json
{
  "eventVersion": 1,
  "eventType": "TransactionRequested",
  "eventId": "9b8f1777-6e52-4a93-a824-f5d438ae4d86",
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
  },
  "externalReference": "manual-test-0001"
}
```

Unknown fields and unsupported event versions are rejected. The SQS message is
reported as a partial batch failure so it can follow the queue redrive policy.

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
| other `4xx` | conditionally set `FAILED` | acknowledge |
| `429`, `5xx`, timeout or network failure | no status update | retry |
| invalid SQS event | no status update | retry, then DLQ |

Both DynamoDB transitions require the current status to be `PENDING`. A conditional
failure is treated as an already-resolved race and does not overwrite the existing
state.
