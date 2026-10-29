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

## Continuous integration

The `CI` workflow runs on pull requests targeting `main`, pushes to `main`, and
manual dispatch. It restores dependencies, verifies formatting, builds Release
with the configured .NET analyzers/warnings as errors, runs unit/integration/contract
tests and audits direct/transitive NuGet dependencies. Reported vulnerabilities
fail the job; the JSON report is printed in the job log for investigation.

The workflow follows the Transaction API's validated CI pattern: actions pinned
to commit SHAs, read-only repository permissions and no persisted checkout token.
The validation job requests no AWS credentials or OIDC token, publishes no
deployment package and does not modify AWS. Tests use controlled substitutes, so a manual Bruno/Swagger
test is not needed for this CI-only change. Branch protection is separate from
the workflow and is not enabled by adding this file.

### Versioned Lambda artifact

After validation succeeds on `main` (push or manual dispatch), a separate job
publishes the Host in Release for `linux-arm64`, framework-dependent, without
ReadyToRun or an apphost. The script explicitly disables the project's default
ReadyToRun setting for this portable build. Pull requests do not generate
deployment artifacts. GitHub Actions retains the bundle for seven days:

- `async-transaction-request-handler.zip`: Lambda package with assemblies at its root;
- `async-transaction-request-handler.zip.sha256`: SHA-256 verification file;
- `build-manifest.json`: full source commit, SDK, runtime/architecture, hexadecimal
  and Base64 ZIP hashes, and future S3 key `<service>/<commit>/<service>.zip`.

The artifact name includes commit, run ID and attempt without overwriting earlier
bundles. Identity is traceable; byte-for-byte reproducibility across builds is not
claimed. The Base64 `sourceCodeHash` refers to the inner Lambda ZIP, not GitHub's
outer download archive/digest. Extract the downloaded Actions bundle and run
`sha256sum --check async-transaction-request-handler.zip.sha256` inside it.

`.github/scripts/package.sh` validates the checkout SHA, refuses an existing output
directory and checks `AsyncTransaction.Handler.dll`, `.deps.json` and
`.runtimeconfig.json` before zipping. The existing Lambda handler remains unchanged.
The package job requests no AWS credentials/OIDC and performs no S3 upload or
deployment; it does not change the running handler. No new Bruno/Swagger test is
required until an actual deployment. The downstream delivery workflow now handles
publication and central Terraform deployment after a successful main push.

### Automated delivery through OIDC

`Deliver Request Handler` (`.github/workflows/publish.yml`) runs after successful
`CI` from a push to this repository's `main`. PRs, forks, failed/cancelled CI and
manual CI runs do not automatically deploy. It publishes the exact approved CI
bundle without rebuilding, then dispatches the central infrastructure workflow
with `service=async-transaction-request-handler` and the full source commit.
The central guard permits only the Request Handler package update and preserves
the other services' deployed versions. It verifies the Lambda hash/status and
tests the complete synthetic transaction flow.

The fine-grained `INFRA_WORKFLOW_TOKEN` secret is supplied only to dispatch/wait,
not stored in code or workflow inputs. It grants Actions access to infrastructure,
not AWS access. Delivery waits for the exact returned run ID; S3 publication or
dispatch alone is not success. Missing/expired tokens and failed/cancelled central
runs fail visibly. At most two minutes of initial missing metadata/title evaluation
are tolerated; wrong IDs or concrete identities fail immediately. Every completed
run must pass all identity checks. Diagnostics exclude tokens/full API payloads.

Delivery jobs queue without replacing other pending jobs. Central Terraform jobs
serialize per stack. Waiting is bounded to 40 minutes; timeout does not cancel
or redispatch a possibly active central run. Inspect its linked result before
retrying; no automatic rollback or overwrite is performed.

Repository variables hold deployment metadata outside source code:
`AWS_REGION`, `AWS_ACCOUNT_ID`, `ARTIFACT_BUCKET` and `AWS_ARTIFACT_ROLE_ARN`.
They are not AWS access keys. The existing IAM trust must match this repository's
verified immutable OIDC subject, audience STS and `main` branch. No environment
name is configured on this job because that would change its subject format.

1. Wait for the chosen `CI` run on `main` to succeed, including packaging.
2. Normally a main push starts delivery automatically. For explicit manual delivery,
   open **Actions → Deliver Request Handler → Run workflow**, select `main`, supply
   the numeric `ci_run_id` and type `PUBLISH` in `confirmation`.
3. The job checks the run's workflow path, repository, event, branch and conclusion,
   downloads its exact commit/run/attempt artifact, and verifies the fixed package
   name, target, ZIP layout, manifest and both checksum representations.
4. Only then it assumes the existing artifact-publisher role via OIDC, checks the
   AWS account and uploads the ZIP plus manifest/checksum to the commit prefix.
   Conditional `If-None-Match: *` writes refuse existing keys. The final ZIP
   checksum, encryption and source-commit metadata are checked against S3.

This job alone receives `id-token: write`; CI jobs remain without AWS credentials.
It requests a 15-minute role session, masks deployment identifiers in logs and
uses actions pinned by SHA. The role remains shared with other application
repositories: per-repository IAM isolation is not introduced here. Conditional
writes protect this workflow, not every possible client of the bucket.

An existing key produces a failure rather than replacement, including on rerun.
The three object writes are not atomic: a partial failure can leave an uploaded
ZIP without its companion files. Inspect the error, object/version and checksums
with the owner before any retry or cleanup; there is no automatic delete/rollback.
An expired GitHub artifact cannot be published with this workflow. Do not rebuild
the same commit and overwrite its existing S3 package to work around either case.

After publication the workflow waits for central deploy, Lambda verification and
smoke success, reporting that run's URL in logs/summary. After successful delivery,
you can test manually through the main Transaction API's Swagger or Bruno:
POST `/transactions` with synthetic data and a new `Idempotency-Key`, then GET
`/transactions/{transactionId}` until `SUCCEEDED/APPROVED`. The Request Handler
itself consumes SQS; it has no HTTP/Swagger endpoint. No manual test is required
before push because this change only adds automation; the first automatic real
delivery still needs verification after the owner pushes.

Run orchestration tests locally without GitHub/AWS requests:
`python3 -m unittest discover -s tests/delivery -v`. CI also runs them.

References: [AWS OIDC action](https://github.com/aws-actions/configure-aws-credentials),
[S3 conditional writes](https://docs.aws.amazon.com/AmazonS3/latest/userguide/conditional-writes.html).

## Local validation

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-build
```

No AWS resource is created by building or testing this repository. SQS, EventBridge
Pipes, IAM and deployment configuration belong to the infrastructure repository and
are managed by that repository; routine deployment now invokes its pipeline.
