# BKE.Desktop.Client

## Purpose

`BKE.Desktop.Client` is the low-level typed .NET desktop integration client for the machine-local BKE Licensing Agent.

Current package source targets **.NET 10** and exposes typed authorization and License Center operations without making the product responsible for Agent transport details.

## Contract model

```text
PRODUCT
  ↓
BKE.Desktop.Client
  ↓
127.0.0.1:43873
  ↓
BKE Licensing Agent
```

The product asks for a capability. The SDK translates that request into the local Agent protocol and translates the Agent response back into typed product-facing results.

The Licensing Agent remains the authority.

## WHAT I NEED

For authorization and License Center operations the SDK needs:

- `productId` — stable product identity
- `version` — current product version
- `installationId` — stable installation identity
- a running BKE Licensing Agent on `http://127.0.0.1:43873/`
- optional `CancellationToken`

All three identity values must be non-empty.

## WHAT I DO

### Authorization

`AuthorizeAsync(...)`:

1. validates product identity input
2. sends `POST /v1/authorize` to the machine-local Agent
3. waits up to 3 seconds
4. validates the returned authorization payload
5. maps Agent/protocol outcomes into `AuthorizationStatus`

### License Center

`OpenLicenseCenterAsync(...)`:

1. validates product identity input
2. creates a correlation ID
3. sends `POST /v1/license-center/open`
4. waits for the Agent-owned License Center flow to finish
5. verifies that the returned correlation ID matches
6. maps the result into `LicenseCenterStatus`

The SDK does not locate or directly execute the License Center binary.

## WHAT I GIVE

### Authorization output

```csharp
AuthorizationResult
```

Contains:

- `Status`
- `Reason`

Possible `AuthorizationStatus` values:

- `Authorized`
- `Denied`
- `ActivationRequired`
- `AgentUnavailable`
- `Timeout`
- `ProtocolRejected`
- `Unsupported`
- `InvalidRequest`
- `InvalidResponse`

### License Center output

```csharp
LicenseCenterResult
```

Contains:

- `Status`
- `Reason`

Possible `LicenseCenterStatus` values:

- `Completed`
- `AuthorizationRefreshed`
- `Cancelled`
- `AgentUnavailable`
- `Timeout`
- `ProtocolRejected`
- `InvalidProductContext`
- `IncompatibleProductVersion`
- `ActivationFailed`
- `Unsupported`
- `InvalidRequest`
- `InvalidResponse`
- `Failed`

## Capabilities

`BKE.Desktop.Client` currently provides two product-facing capabilities:

```csharp
Task<AuthorizationResult> AuthorizeAsync(
    string productId,
    string version,
    string installationId,
    CancellationToken cancellationToken = default);

Task<LicenseCenterResult> OpenLicenseCenterAsync(
    string productId,
    string version,
    string installationId,
    CancellationToken cancellationToken = default);
```

## Failure semantics

The contract separates common failure classes instead of returning a generic boolean:

- invalid consumer input → `InvalidRequest`
- Agent unreachable / server-side failure → `AgentUnavailable`
- local timeout → `Timeout`
- rejected HTTP/protocol request → `ProtocolRejected`
- malformed or incomplete Agent payload → `InvalidResponse`
- unsupported product/version/outcome → `Unsupported`

The product should decide what UI or shutdown behavior is appropriate for each typed result.

## Security boundary

This SDK does **not** own or expose:

- license signing keys
- entitlement authority
- activation authority
- trusted update policy
- privileged execution
- Agent executable paths
- License Center executable paths
- arbitrary remote authority URLs

Those remain outside the consumer-facing SDK boundary.

## Minimal usage

```csharp
using BKE.Desktop.Client;

using var client = BkeDesktopClient.Create();

var result = await client.AuthorizeAsync(
    productId: "bke-render-dock",
    version: "1.0.0",
    installationId: installationId);

if (result.Status == AuthorizationStatus.Authorized)
{
    // Start the product.
}
```

## Consumer responsibility

The consuming product owns:

- its stable `productId`
- its version
- its installation ID lifecycle
- product UI and startup behavior
- deciding how typed SDK results affect the application

It should not duplicate the Agent protocol or privileged authority logic inside the product.
