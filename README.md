# BKE Desktop SDK

## What It Is

`BKE.Desktop.Client` is a reusable .NET 8 managed client for desktop applications that communicate with the installed BKE Licensing Agent.

It centralizes proven integration mechanics such as the local Agent HTTP contract, request serialization, response parsing, timeout handling, and typed result normalization. Product applications provide their own legitimate product identity and installation identity.

## What It Is Not

The SDK is not:

- a licensing authority or entitlement engine;
- a license issuer or activation-key authority;
- a trusted-key or target-policy store;
- an updater or update-policy authority;
- a privileged executor;
- a replacement for BKE Licensing Agent or Updater Core.

Commercial and authorization decisions remain with the BKE environment.

## Architecture

Normal desktop authorization flow:

`Product -> BKE.Desktop.Client -> BKE Licensing Agent -> BKE environment`

The update authority chain remains separate:

`BKE Digital Solutions -> BKE Licensing Agent -> Updater Core -> OS privileged boundary -> Installed Product`

The SDK does not bypass or replace that chain.

## Requirements

- .NET 8 development target.
- BKE Licensing Agent installed and running where authorization is required.
- Compatibility with the local Agent contract documented below.

## Installation

The package feed has not been established yet. When the package is available through an approved feed, consumers should pin the explicit version:

```xml
<PackageReference Include="BKE.Desktop.Client" Version="1.0.0" />
```

Do not use floating versions or manually copy an unversioned DLL.

## Quick Start

```csharp
using BKE.Desktop.Client;

using var client = BkeDesktopClient.Create();

var productId = "bke-your-product";
var version = "1.0.0";
var installationId = GetStableInstallationId();

var result = await client.AuthorizeAsync(productId, version, installationId);

if (result.Status == AuthorizationStatus.Authorized)
{
    // Start product functionality.
}
else
{
    // Handle the typed status and fail closed.
}
```

`GetStableInstallationId()` represents an identity obtained by the consumer using its approved installation-identity policy. The SDK does not invent product identity or make commercial decisions.

## Current Agent Contract

The current local contract uses:

- `POST http://127.0.0.1:43873/v1/authorize`
- JSON fields `product_id`, `version`, and `installation_id`
- an authorization response containing `authorized` and `reason`

The SDK targets the default local Agent endpoint and does not claim arbitrary endpoint configuration.

## Product Identity

The consumer supplies:

- `productId`: the registered BKE product identifier;
- `version`: the product version being authorized;
- `installationId`: a stable identity for the installation, obtained under the consumer's approved policy.

The SDK does not hardcode Air Stack or Render Dock business logic.

## Error Handling

Authorization results expose typed statuses such as:

- `Authorized`
- `Denied`
- `ActivationRequired`
- `AgentUnavailable`
- `Unsupported`
- `InvalidResponse`

Consumers must treat unavailable, unsupported, malformed, and denied outcomes as non-authorized. The SDK does not silently fail open.

License Center requests, where supported by the current client surface, also return typed outcomes and validate the echoed correlation ID.

## Security Model

The SDK is transport and integration infrastructure. It does not contain trusted private signing keys, entitlement logic, privileged helper selection, installation-root authority, update execution, or caller-controlled trusted configuration.

The Licensing Agent remains the local authority. BKE Digital Solutions remains the commercial and policy authority. The SDK cannot be used to select trusted keys, privileged helpers, installation roots, or privileged target policy.

## Versioning

The package uses explicit semantic versions. Consumers should pin a tested package version such as `1.0.0`. A new package version requires compatibility evidence before adoption.

## BKE Environment Compatibility

This repository currently documents the local Agent contract at Licensing Agent baseline `b34dcaebddbdad724b7d381172bf41eb7ec5a7cd`. This is a compatibility target/reference, not a claim of completed runtime certification. Package certification requires successful contract tests, package verification, and an explicit compatibility evidence record.

## Air Stack Integration

Air Stack migration is intentionally separate. After this SDK candidate is certified, Air Stack will be migrated and tested in its own repository without changing its product behavior or bypassing Licensing Agent authority.

## Render Dock Integration

Render Dock migration is intentionally separate. After the SDK candidate is certified and the Air Stack integration pattern is proven, Render Dock will be migrated and tested in its own repository.

## Secure Module Launch

Secure module-launch transport is not claimed as a supported SDK 1.0.0 capability by this metadata/documentation work. Its inclusion or deferral must be decided and certified in the implementation workstream before consumer migration.

## Publication Status

No production package has been published. The repository package artifact, if generated by CI, is a candidate artifact only.

See [LICENSE.txt](LICENSE.txt).
