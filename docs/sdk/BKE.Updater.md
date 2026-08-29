# BKE.Updater

## Purpose

`BKE.Updater` is the canonical product-facing update-check capability for BKE software.

It defines what a product asks for, what result it receives, and the typed failure vocabulary. Starting with package **0.4.0**, it also ships the default secured client that talks to the machine-local BKE Licensing Agent.

The Licensing Agent remains the trusted provider/authority boundary. `BKE.Updater` does not become the updater authority, installer, verifier, or privileged executor.

Current package source targets **.NET 10**.

## Capability identity

```text
Capability ID:      bke.updates.check
Contract version:   1
```

Exposed by:

```csharp
UpdateCapability.Id
UpdateCapability.ContractVersion
```

## Contract model

```text
PRODUCT
  ↓
BKE.Updater
  ↓
BkeUpdaterClient / IUpdateClient
  ↓
BKE Licensing Agent
  ↓
TRUSTED UPDATE AUTHORITY
```

A product depends on the SDK capability, not on Agent routes, updater executables, remote services, signed policies, download grants, or installer internals.

## WHAT I NEED

An update check needs an `UpdateCheckRequest` containing:

- `ProductId` — stable product identity
- `CurrentVersion` — version currently running
- optional `RequestedVersion` — an explicitly requested target version

Normal BKE desktop consumers create the default client with:

```csharp
using var updates = BkeUpdaterClient.Create();
```

For composition/testing, consumers may depend on the portable interface:

```csharp
IUpdateClient
```

## WHAT I DO

The public capability operation is:

```csharp
Task<UpdateCheckResult> CheckAsync(
    UpdateCheckRequest request,
    CancellationToken cancellationToken = default);
```

`BkeUpdaterClient` performs only product-to-provider integration:

1. accepts the typed SDK request
2. sends the canonical request to the fixed machine-local Licensing Agent boundary
3. validates capability identity and contract version
4. validates result invariants
5. maps the Agent result into the typed SDK result
6. preserves typed provider failures instead of collapsing them into a generic error

The current fixed provider boundary is:

```text
POST http://127.0.0.1:43873/v1/updates/check
```

The default transport disables proxy use and automatic redirects. The product does not choose the Agent address.

## WHAT I GIVE

The operation returns:

```csharp
UpdateCheckResult
```

### States

`UpdateCheckStatus`:

- `UpToDate`
- `UpdateAvailable`
- `Deferred`
- `Failed`

Result invariants:

- `UpToDate` → no available version and no error
- `UpdateAvailable` → non-empty `AvailableVersion`, no error
- `Deferred` → optional `AvailableVersion`, no error
- `Failed` → typed `UpdateError`, no available version

## Typed failures

`UpdateError` contains:

- `Code`
- `Message`
- `Retryable`

`UpdateErrorCode` values:

- `InvalidRequest`
- `ProviderUnavailable`
- `TransportFailure`
- `ProtocolFailure`
- `MalformedResponse`
- `VerificationFailure`
- `PolicyDenied`
- `Unknown`

These are boundary-specific on purpose:

- `ProviderUnavailable` — the local Licensing Agent cannot be reached or does not respond
- `TransportFailure` — the trusted provider reports a transport failure reaching its authority
- `ProtocolFailure` — provider/SDK contract identity or protocol is incompatible
- `MalformedResponse` — response shape or state invariants are invalid
- `VerificationFailure` — the trusted provider could not verify authority data
- `PolicyDenied` — trusted update policy denied the check

Products should react to the typed result rather than inspect provider-specific strings.

## Capabilities

Current capability:

```text
bke.updates.check/v1
```

It answers:

```text
Given this product and current version,
what is the trusted update state?
```

It can report:

- already current
- update available
- update known but deferred
- typed failure

Checking is intentionally separate from downloading or installing.

## Security boundary

The product-facing request does not accept:

- signing keys
- trust stores
- arbitrary executable paths
- arbitrary installer paths
- arbitrary install roots
- privileged helper selection
- caller-selected download URLs
- update signing authority
- entitlement authority
- policy signing authority
- caller-selected Agent endpoint

The SDK result does not expose:

- signed leases
- signed update policies
- download grants
- trusted-key material
- privileged execution controls
- provider storage

Those stay inside the Licensing Agent / trusted authority boundary.

## What this SDK does NOT do

`BKE.Updater` is not:

- the updater authority
- an installer
- a package downloader
- the signed-policy verifier
- the trusted-key store
- a privileged executor
- a background service

`BkeUpdaterClient` is only the default product-to-Agent adapter implementing the canonical SDK contract.

## Minimal usage

```csharp
using BKE.Updater;

using var updates = BkeUpdaterClient.Create();

var result = await updates.CheckAsync(
    new UpdateCheckRequest(
        productId: "bke-render-dock",
        currentVersion: "1.0.1"));

switch (result.Status)
{
    case UpdateCheckStatus.UpToDate:
        break;

    case UpdateCheckStatus.UpdateAvailable:
        ShowUpdateAvailable(result.AvailableVersion!);
        break;

    case UpdateCheckStatus.Deferred:
        break;

    case UpdateCheckStatus.Failed:
        LogUpdateFailure(result.Error!);
        break;
}
```

The product does not call `/v1/updates/check` itself and does not define its own updater DTOs.

## Composition usage

Code that wants testability or provider substitution can depend on the interface:

```csharp
public sealed class UpdateCoordinator
{
    private readonly IUpdateClient updates;

    public UpdateCoordinator(IUpdateClient updates)
    {
        this.updates = updates;
    }
}
```

The default production composition is:

```csharp
IUpdateClient updates = BkeUpdaterClient.Create();
```

A future provider can implement the same interface without changing product-facing update semantics.

## Provider responsibility

The Licensing Agent / authority side owns implementation-specific trusted work such as:

- remote authority endpoint selection
- signed lease resolution
- signed update policy verification
- trusted-key handling
- policy revision enforcement
- download grant handling
- package/content verification
- privileged update execution

Those responsibilities remain behind the SDK boundary.

## Consumer responsibility

The consuming product owns:

- its `ProductId`
- its current version
- when to request a check
- product UI for `UpToDate`, `UpdateAvailable`, `Deferred`, or `Failed`
- its product-specific decision about how to present an available update

The product must not bypass `BKE.Updater` to call Licensing Agent updater routes directly.
