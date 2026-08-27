# BKE Desktop SDK

BKE.Desktop.Client is a reusable .NET 8 client for connecting BKE desktop applications to the installed BKE environment through BKE Licensing Agent.

It is not a licensing authority, entitlement engine, updater, trusted-key holder, privileged executor, or source of commercial policy. Decisions remain with Licensing Agent and BKE Digital Solutions.

Architecture: Product -> BKE.Desktop.Client -> Licensing Agent -> BKE environment.

Updates remain governed by: Digital Solutions -> Licensing Agent -> Updater Core -> OS privileged boundary -> Installed Product. The SDK does not bypass this chain.

Requirements: .NET 8, BKE Licensing Agent installed and running, and the certified v1 local Agent contract.

Installation (no package feed is claimed until configured):

    <PackageReference Include="BKE.Desktop.Client" Version="1.0.0" />

Quick start:

    using BKE.Desktop.Client;
    using var client = BkeDesktopClient.Create();
    var result = await client.AuthorizeAsync("bke-your-product", "1.0.0", installationId);
    if (result.Status == AuthorizationStatus.Authorized) { /* start product */ }

The request is POST http://127.0.0.1:43873/v1/authorize with product_id, version, and installation_id. The consumer supplies legitimate product identity and stable installation identity.

AuthorizationResult.Status distinguishes Authorized, Denied, ActivationRequired, AgentUnavailable, Unsupported, and InvalidResponse. OpenLicenseCenterAsync returns typed outcomes and verifies the echoed correlation ID.

The SDK contains no trusted private keys, entitlement logic, privileged helper selection, installation-root policy, update execution, or caller-controlled trusted configuration.

Version 1.0.0 is certified against Licensing Agent main SHA b34dcaebddbdad724b7d381172bf41eb7ec5a7cd, including /v1/authorize and /v1/license-center/open.

Air Stack and Render Dock migrations are intentionally separate phases and are not performed in this repository phase. Consumers pin explicit semantic package versions.

See LICENSE.txt.
