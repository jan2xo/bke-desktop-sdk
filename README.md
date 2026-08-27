# BKE Desktop SDK

## What It Is

BKE.Desktop.Client is a reusable .NET 8 client for connecting BKE desktop applications to the installed BKE Licensing Agent.

## What It Is Not

The SDK is not a licensing authority, entitlement engine, updater, trusted-key holder, privileged executor, or source of commercial policy. Decisions remain with the Licensing Agent and BKE Digital Solutions.

## Architecture

Product -> BKE.Desktop.Client -> Licensing Agent -> BKE environment.

Updates remain governed by Digital Solutions -> Licensing Agent -> Updater Core -> OS privileged boundary -> Installed Product. The SDK does not bypass this chain.

## Requirements

- .NET 8.
- BKE Licensing Agent installed and running where authorization is required.
- The current local Agent contract: POST http://127.0.0.1:43873/v1/authorize and POST http://127.0.0.1:43873/v1/license-center/open.

## Installation

No package feed is claimed until one is configured. Consumers pin the explicit package version:

```xml
<PackageReference Include="BKE.Desktop.Client" Version="1.0.0" />
```

## Quick Start

```csharp
using BKE.Desktop.Client;

using var client = BkeDesktopClient.Create();
const string productId = "bke-your-product";
const string version = "1.0.0";
const string installationId = "stable-installation-identity";

var result = await client.AuthorizeAsync(productId, version, installationId);

if (result.Status == AuthorizationStatus.Authorized)
{
    // Start product functionality.
}
else
{
    // Present the typed result and stop or recover; never fail open.
    Console.WriteLine(result.Reason);
}
```

The consumer supplies a legitimate product ID, the running product version, and a stable installation identity according to the product's installation design. The SDK does not invent or authorize product identity.

## API and Error Handling

AuthorizeAsync returns typed statuses: Authorized, Denied, ActivationRequired, AgentUnavailable, Unsupported, InvalidRequest, and InvalidResponse. OpenLicenseCenterAsync returns typed outcomes and verifies the echoed correlation ID.

## Security Model

The SDK transports and normalizes the current Agent contract. It contains no trusted private keys, entitlement logic, privileged helper selection, installation-root policy, update execution, or caller-controlled trusted configuration.

Secure Named Pipe module launch (bke.module-ipc.v1) is intentionally deferred from this initial SDK candidate. It will be added only after the canonical contract is separately verified and assigned to the SDK scope.

## Versioning

The package uses semantic versions and consumers pin explicit versions. The initial package candidate is BKE.Desktop.Client 1.0.0.

## BKE Environment Compatibility

This branch is contract-tested against the observed Agent request/response shapes. Runtime compatibility certification against a live Licensing Agent and a consumer package-restore test remain required before declaring 1.0.0 certified.

## Air Stack and Render Dock Integration

Air Stack and Render Dock migrations are separate phases. This repository change does not modify either consumer. Their exact starting SHAs must be re-inspected immediately before their migrations.

See LICENSE.txt.
