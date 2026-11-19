# ADR-0004: Integration architecture (HealthConnect)

- **Status:** Proposed
- **Date:** 2026-11-18

## Decision
1. **integration-worker** is a separate .NET process (same repo), running connectors packaged at build time. Connectors implement an SDK interface: `Describe`, `TestConnection`, `Pull`, `Push`, `MapInbound`, `MapOutbound`, `Reconcile`.
2. **Durability:** all outbound sync originates from module outbox events; each sync job has an idempotency key (`tenant, connector, resource, internalId, version`); retries with exponential backoff; poison messages → **dead-letter table** (encrypted payload, TTL, operator replay UI).
3. **Source-of-truth policy per resource per connector:** `internal`, `external`, or `field-level split`; conflict policy `reject | last-writer-wins | manual-review`. Default `manual-review` for clinical data.
4. **External identifiers** stored in owning modules' external-ID tables; HealthConnect keeps the link table and mapping versions.
5. **Standards:** FHIR R4 (Firely .NET SDK) for Patient, Practitioner, Organization, Encounter, Appointment, Observation, DocumentReference first; India profiles (NRCeS/ABDM) and EU profiles as configured. HL7 v2 (ADT, ORM/ORU) via adapter later. DICOMweb only for metadata/links initially.
6. **Mapping Studio:** versioned mappings; AI may suggest; activation requires human approval **and** a passing sample-data test run.
7. **Credentials** in a secret store (dev: user-secrets/.env excluded from git; prod: cloud secret manager or Vault), never in DB plaintext.
8. Simulated HMS connector first (M8); a real connector only with documented vendor permission.

## Consequences
- No claim of FHIR conformance without running the relevant test suites (e.g., Inferno/Touchstone) and documenting results.
