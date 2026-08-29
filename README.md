# BKE Desktop SDK Family

`bke-sdk` is the umbrella repository for product-facing .NET 10 desktop capabilities used by BKE software.

## Capability family

The package family is intentionally capability-oriented:

```text
BKE.Desktop
├── BKE.Desktop.Licensing
├── BKE.Desktop.Identity
├── BKE.Desktop.ModuleClient
├── BKE.Desktop.UpdaterClient
└── BKE.Desktop.GraceClient
```

Each capability remains independently versioned. A product composes only the capabilities it needs.

## Licensing capability

`BKE.Desktop.Licensing` is the hardened successor for product-to-Licensing-Agent authorization and activation orchestration.

It owns integration mechanics only:

```text
Product
  -> BKE.Desktop.Licensing
  -> BKE Licensing Agent
  -> BKE licensing environment
```

The package never becomes the licensing authority. It does not contain lease authority, entitlement policy, private signing keys, trusted-key selection, privileged update execution, installer authority, or product-specific business logic.

### Quick start

```csharp
using BKE.Desktop.Licensing;

using var licensing = BkeLicensingClient.Create();

var result = await licensing.EnsureAuthorizedAsync(
    productId: "bke-your-product",
    version: "1.0.0",
    installationId: GetStableInstallationId(),
    options: new LicensingFlowOptions
    {
        ActivationInteraction = ActivationInteraction.NativeDesktop
    });

if (!result.Authorized)
{
    // Fail closed.
    return;
}

// Start product functionality.
```

`EnsureAuthorizedAsync` owns the standard startup choreography:

```text
Authorize
  -> Authorized: return
  -> ActivationRequired:
       apply interaction policy
       -> Agent-owned activation presentation
       -> wait for completion
       -> re-authorize
       -> return final decision
```

Products should not duplicate this orchestration in `Program.cs` and should not locate or launch the License Center executable themselves.

## Activation interaction policy

The public policy is explicit rather than a generic `GUI=true` flag:

```csharp
public enum ActivationInteraction
{
    NativeDesktop,
    SystemBrowser,
    CommandLine,
    None
}
```

For `BKE.Desktop.Licensing` 1.0.0:

- `NativeDesktop` is supported and is the default. The SDK asks the Licensing Agent to open its native License Center.
- `None` performs authorization only and leaves `ActivationRequired` to the caller without presenting activation UI.
- `SystemBrowser` is reserved but deliberately returns `Unsupported` until a browser activation path is separately hardened and certified.
- `CommandLine` is reserved but deliberately returns `Unsupported` until an Agent-owned CLI activation path is separately hardened and certified.

An unsupported interaction must never silently fall back to another presentation mode.

## Low-level surface

Advanced callers can use:

- `AuthorizeAsync(productId, version, installationId)`
- `OpenLicenseCenterAsync(productId, version, installationId)`

The current Agent contract uses the fixed loopback boundary:

- `POST http://127.0.0.1:43873/v1/authorize`
- `POST http://127.0.0.1:43873/v1/license-center/open`

Automatic redirects and proxy use are disabled by the default client factory.

## Legacy compatibility package

`BKE.Desktop.Client` 1.0.0 is retained as a frozen compatibility package for already-certified consumers. It is not renamed in place and its public API is not broken.

New licensing integrations should target:

```xml
<PackageReference Include="BKE.Desktop.Licensing" Version="1.0.0" />
```

Consumer migration is performed repository-by-repository with CI evidence.

## Security boundary

The Licensing Agent remains the local authority and owns activation presentation. BKE Digital Solutions remains the commercial and policy authority.

The SDK cannot select trusted keys, write leases, choose privileged helpers, choose install roots, or authorize itself. Unavailable, malformed, unsupported, denied, or failed outcomes remain fail-closed.

The local product-to-Agent transport is currently ordinary loopback HTTP. It does not cryptographically authenticate the process that owns `127.0.0.1:43873`; process authenticity is a shared protocol-boundary hardening item and must not be "solved" by embedding authority or private keys in product code.

## Licensing of SDK source

New BKE Desktop capability packages are distributed under [LICENSE-BKE-PROPRIETARY.txt](LICENSE-BKE-PROPRIETARY.txt).

The already-published `BKE.Desktop.Client` 1.0.0 compatibility package retains the license terms under which that version was released; changing successor-package licensing does not retroactively rewrite the terms of previously distributed copies.


## .NET 10 baseline

**2026-08-29 — .NET 10 baseline decision**

Active BKE SDK development now targets stable .NET 10. Previous .NET 8 package releases remain immutable historical compatibility artifacts and are not republished. New package releases are .NET 10 only. Products still targeting .NET 8 must migrate before adopting these package versions.

The future `bke-runtime` executable host remains a separate project; this repository contains reusable SDK libraries and secured contracts only.
