# BKE Capability Contract Standard

Every reusable capability documents:

- **CAPABILITY**
- **IDENTITY / VERSION**
- **WHAT I NEED**
- **INPUT**
- **PROCESS** (private implementation)
- **OUTPUT**
- **WHAT I GIVE**
- **ERRORS**
- **SECURITY BOUNDARY**
- **SUPPORTED ADAPTERS**

Contracts are typed, product-neutral, async-friendly, and transport-independent. Consumers depend on interfaces and data contracts, never authority-bearing implementation details. Transport, persistence, UI, privileged execution, and remote providers are adapters outside the portable contract.

## Current execution model

```
bke-sdk
├── BKE.Updater       (code, tests, NuGet, CI)
├── BKE.Notifications (code, tests, NuGet, CI)
└── Full SDK Certification (deliberate only)
```

Air Stack and Render Dock are later proof consumers; they are not capability owners.

## Security boundary

The consumer-facing updater contract does not accept signing keys, trust stores, executable or installer paths, installation roots, privileged helper selection, trusted URLs, policy authority, signing authority, entitlement authority, or update authorization authority. A trusted provider adapter owns those decisions.

Notifications are software-side messages. OS toasts, push services, queues, databases, feeds, and UI are future adapters.

## .NET 10 baseline

**2026-08-29 — .NET 10 baseline decision**

Active BKE SDK development now targets stable .NET 10. .NET 8 SDK development is deprecated. Existing .NET 8 package bytes remain immutable historical artifacts; new package versions target .NET 10 only. Products still targeting .NET 8 must migrate before consuming these package versions.

## Future architecture (document only)

`bke-runtime` may eventually host capability registry, provider resolution, plugin loading, dependency graphs, jobs, events, state, HTTPS adapters, and UI composition. The portable artifact is the contract; `bke-sdk` is not an executable host.

Container distinction: `bke-sdk` is reusable contracts/libraries/packages. `bke-runtime` is the future executable capability host. Use Docker/Podman/OCI to run `bke-runtime`, not `docker run bke-sdk`.
