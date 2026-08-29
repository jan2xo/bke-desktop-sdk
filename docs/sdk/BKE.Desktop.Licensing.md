# BKE.Desktop.Licensing

## Purpose

`BKE.Desktop.Licensing` is the higher-level typed licensing capability for BKE desktop products.

It talks only to the machine-local BKE Licensing Agent and keeps licensing authority, activation presentation, and privileged behavior outside the consuming product.

Current package source targets **.NET 10**.

## Contract model

```text
PRODUCT
  ↓
BKE.Desktop.Licensing
  ↓
BKE Licensing Agent
  ↓
LICENSING AUTHORITY / ACTIVATION FLOW
```

The product supplies identity and chooses an interaction policy. The SDK performs the standard authorization flow and returns a typed decision.

## WHAT I NEED

The SDK needs:

- `productId` — stable product identity
- `version` — current product version
- `installationId` — stable installation identity
- a running BKE Licensing Agent on `http://127.0.0.1:43873/`
- optional `LicensingFlowOptions`
- optional `CancellationToken`

For `EnsureAuthorizedAsync(...)`, the flow options may define:

- `ActivationInteraction`
- `AuthorizationRefreshTimeout`
- `AuthorizationRefreshInterval`

Timing values must be greater than zero.

## WHAT I DO

### Direct authorization

`AuthorizeAsync(...)` asks the Agent for the current authorization decision only.

It does **not** launch activation UI.

### Agent-owned License Center

`OpenLicenseCenterAsync(...)` asks the Agent to open its native License Center for the supplied product context.

The product never needs to find or execute the License Center binary itself.

### Complete startup licensing flow

`EnsureAuthorizedAsync(...)` performs the standard product startup flow:

```text
Authorize
  ↓
Authorized? ── yes ──> return Authorized
  ↓ no
ActivationRequired?
  ↓
apply ActivationInteraction policy
  ↓
NativeDesktop → Agent opens License Center
  ↓
wait for Agent authorization refresh
  ↓
return final typed AuthorizationResult
```

Current interaction behavior:

- `NativeDesktop` — supported activation presentation
- `None` — return `ActivationRequired` without launching UI
- `SystemBrowser` — returns `Unsupported`
- `CommandLine` — returns `Unsupported`

## WHAT I GIVE

The main product-facing output is:

```csharp
AuthorizationResult
```

It contains:

- `Status`
- `Reason`
- `Authorized` convenience property

Possible `AuthorizationStatus` values:

- `Authorized`
- `Denied`
- `ActivationRequired`
- `ActivationCancelled`
- `AgentUnavailable`
- `Timeout`
- `ProtocolRejected`
- `Unsupported`
- `InvalidRequest`
- `InvalidResponse`

The lower-level License Center operation returns:

```csharp
LicenseCenterResult
```

with a typed `LicenseCenterStatus` and reason.

## Capabilities

```csharp
Task<AuthorizationResult> AuthorizeAsync(...);

Task<LicenseCenterResult> OpenLicenseCenterAsync(...);

Task<AuthorizationResult> EnsureAuthorizedAsync(
    string productId,
    string version,
    string installationId,
    LicensingFlowOptions? options = null,
    CancellationToken cancellationToken = default);
```

`EnsureAuthorizedAsync(...)` is the normal high-level capability for products that want the SDK to coordinate the standard authorization/activation startup flow.

## Failure semantics

The SDK preserves typed distinctions instead of collapsing all licensing problems into `false`:

- bad product context → `InvalidRequest`
- Agent unavailable → `AgentUnavailable`
- Agent timeout / refresh timeout → `Timeout`
- protocol rejection → `ProtocolRejected`
- malformed Agent response → `InvalidResponse`
- unsupported product/version/interaction → `Unsupported`
- user cancels activation → `ActivationCancelled`
- licensing decision denies access → `Denied`

## Security boundary

`BKE.Desktop.Licensing` is a capability client, not the licensing authority.

It does **not** own or expose:

- license signing keys
- entitlement signing
- lease verification authority
- trusted authority configuration
- activation credentials
- License Center executable paths
- arbitrary privileged commands

The BKE Licensing Agent remains the machine-local trusted control plane.

## Minimal startup usage

```csharp
using BKE.Desktop.Licensing;

using var licensing = BkeLicensingClient.Create();

var result = await licensing.EnsureAuthorizedAsync(
    productId: "bke-render-dock",
    version: "1.0.0",
    installationId: installationId);

if (!result.Authorized)
{
    // Product decides how to present/handle the typed failure.
    return;
}

// Continue product startup.
```

## Consumer responsibility

The consuming product owns:

- product identity
- version identity
- installation ID lifecycle
- application startup policy
- application UI outside the Agent-owned activation presentation
- final behavior after each typed result

The consuming product should not recreate licensing authority or activation transport internally.
