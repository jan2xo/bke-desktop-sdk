# BKE.Updater

## Purpose

`BKE.Updater` is the product-neutral update capability contract for BKE software.

It defines what a product asks for and what an update provider returns. It does **not** implement the trusted updater authority, installer, verifier, or privileged executor.

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
IUpdateClient
  ↓
BKE.Updater contract
  ↓
PROVIDER ADAPTER
  ↓
TRUSTED UPDATE AUTHORITY
```

A product depends on `IUpdateClient`, not on a specific updater executable or remote service.

The provider behind that interface may change without changing the product-facing contract.

## WHAT I NEED

An update check needs an `UpdateCheckRequest` containing:

- `ProductId` — stable product identity
- `CurrentVersion` — version currently running
- optional `RequestedVersion` — an explicitly requested target version

The consumer also needs an implementation of:

```csharp
IUpdateClient
```

The package itself intentionally contains only the portable contract and does not choose the provider.

## WHAT I DO

The contract defines one capability operation:

```csharp
Task<UpdateCheckResult> CheckAsync(
    UpdateCheckRequest request,
    CancellationToken cancellationToken = default);
```

A conforming provider must:

1. accept the typed request
2. resolve update availability using its trusted implementation
3. validate/verify whatever authority data its implementation requires
4. map the result into the invariant-safe SDK result types
5. never leak authority-bearing implementation details through the product-facing result

The provider implementation may use the BKE Licensing Agent today and a different provider later while preserving the same SDK contract.

## WHAT I GIVE

The operation returns:

```csharp
UpdateCheckResult
```

### Successful/normal states

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

This distinction is intentional. A product can react differently to a temporary provider outage, a transport problem, a malformed authority response, or a verification/policy failure.

## Capabilities

Current contract capability:

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

## What this SDK does NOT accept

The product-facing request intentionally does not accept:

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

Those belong to the provider/authority side of the boundary.

## What this SDK does NOT do

`BKE.Updater` itself is not:

- the updater authority
- an installer
- a package downloader
- a signature verifier implementation
- a privileged executor
- a background service

It is the stable product-facing capability contract those implementations conform to.

## Minimal consumer usage

```csharp
using BKE.Updater;

public sealed class UpdateCoordinator
{
    private readonly IUpdateClient updates;

    public UpdateCoordinator(IUpdateClient updates)
    {
        this.updates = updates;
    }

    public async Task<UpdateCheckResult> CheckAsync(
        string productId,
        string currentVersion,
        CancellationToken cancellationToken = default)
    {
        var request = new UpdateCheckRequest(productId, currentVersion);
        return await updates.CheckAsync(request, cancellationToken);
    }
}
```

The product does not need to know whether `IUpdateClient` is backed by the Licensing Agent, a future BKE runtime, or another certified provider.

## Provider responsibility

A provider implementation owns implementation-specific work such as:

- transport
- provider discovery
- trusted authority endpoint selection
- signed policy verification
- trusted-key handling
- download grant handling
- package/content verification
- privileged update execution

Those responsibilities stay behind the SDK boundary.

## Consumer responsibility

The consuming product owns:

- its `ProductId`
- its current version
- when to ask for an update check
- product UI for update state
- policy for what to do with `UpToDate`, `UpdateAvailable`, `Deferred`, or `Failed`

The product should not bypass `IUpdateClient` to directly depend on provider internals.
