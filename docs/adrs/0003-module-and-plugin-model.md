# ADR-0003: Module and plugin model

- **Status:** Proposed
- **Date:** 2026-11-18

## Decision
1. Modules are **trusted, build-time packaged** .NET projects (+ optional web route packages) with a `module.json` manifest. No runtime loading of third-party assemblies in initial releases.
2. **Two-level activation:** package (bundle build profile) and tenant (dependency resolution ∧ entitlement ∧ admin enablement). See `docs/architecture/module-boundaries.md`.
3. **Public/internal split:** `X.Contracts` is the only referenceable surface. Enforced by architecture tests in CI.
4. **Products never depend on products.** Packs depend on their parent product. Cross-product features are **bridge modules**.
5. **New domain module `clinical.registry`** owns patient, practitioner, encounter and consent master data, so ClinicOS and HospitalOS share contracts without depending on each other. *(Departs from spec §3.3 folder list, which put these in clinicos-core — needs approval.)*
6. Lifecycle hooks: install, upgrade, enable, disable, uninstall. Uninstall is non-destructive by default.
7. Web UI: separate product SPAs (`admin-web`, `quality-web`, `clinic-web`, later `hospital-web`, `medtech-web`) sharing `packages/ui-kit`; each SPA builds the routes of modules in its bundle and filters at runtime by the navigation manifest from core-api.

## Future: externally authored plugins (not built now)
Out-of-process only: plugins run as separate services (container or WASM sandbox) talking to HealthOS via the public API + webhooks with scoped OAuth credentials, a signed manifest, a review process, and resource quotas. Never in-process.

## Consequences
- Adding a module = new projects + manifest + schema + bundle profile update.
- Extracting a module to a separate service later is possible because communication is already via contracts/events.
