# System Context and Containers (C4 levels 1–2)

## Level 1 — System context

```mermaid
C4Context
title HealthOS — System Context
Person(staff, "Clinic / hospital / QA staff", "Clinicians, front desk, QA, approvers")
Person(tadmin, "Tenant administrator", "Configures modules, roles, forms, workflows")
Person(padmin, "Ontogenesis platform operator", "Manages tenants, releases, licenses")
System(healthos, "HealthOS", "Core platform + licensed products (CareOps, ClinicOS, HospitalOS, MedTechOS, HealthConnect)")
System_Ext(idp, "Enterprise IdP", "Customer Entra ID / Okta / Google via OIDC/SAML federation")
System_Ext(llm, "Approved LLM providers", "Region-pinned, DPA in place; optional")
System_Ext(ext, "External systems", "Customer HMS/EHR/ERP/QMS/CMMS, ABDM, labs")
System_Ext(mail, "Email/SMS provider", "Notifications")
Rel(staff, healthos, "Uses", "HTTPS")
Rel(tadmin, healthos, "Configures", "HTTPS")
Rel(padmin, healthos, "Operates", "HTTPS")
Rel(healthos, idp, "Federates login", "OIDC/SAML")
Rel(healthos, llm, "Draft generation, embeddings (redacted/allowed data only)", "HTTPS")
Rel(healthos, ext, "Sync via HealthConnect", "FHIR / HL7 v2 / REST")
Rel(healthos, mail, "Sends notifications", "HTTPS/SMTP")
```

## Level 2 — Containers (single deployment cell)

```mermaid
flowchart LR
  subgraph Browser
    AW[admin-web<br/>React SPA]
    QW[quality-web<br/>React SPA]
    CW[clinic-web<br/>React SPA]
  end
  subgraph Cell["Deployment cell (one region, e.g. IN-1 or EU-1)"]
    GW[Reverse proxy / ingress<br/>TLS, rate limits, WAF]
    API[core-api<br/>ASP.NET Core modular monolith<br/>Core modules + licensed product modules]
    BG[core-worker<br/>same codebase: outbox dispatcher,<br/>jobs, retention, notifications]
    AI[ai-service<br/>Python FastAPI + worker<br/>provider abstraction, RAG, evals]
    IW[integration-worker<br/>.NET: connectors, sync,<br/>retry, DLQ, reconciliation]
    PG[(PostgreSQL<br/>schema per module, RLS,<br/>pgvector)]
    OBJ[(S3-compatible<br/>object storage)]
    IDP[OIDC provider<br/>Keycloak]
    OTEL[OpenTelemetry collector<br/>PHI-redacted]
  end
  AW & QW & CW --> GW --> API
  AW & QW & CW -. login .-> IDP
  API --> PG
  API --> OBJ
  API -- signed internal calls --> AI
  BG --> PG
  AI --> PG
  AI -. approved providers .-> LLM[(LLM providers)]
  IW --> PG
  IW -. connectors .-> EXT[(External systems)]
  API & BG & AI & IW --> OTEL
```

## Key properties
- **The browser never talks to ai-service or integration-worker directly.** core-api enforces authN/authZ, entitlements and budget, then calls ai-service with a signed, short-lived request context (tenant, user, allowed scopes).
- **core-api and core-worker are the same build**, started in different roles. This keeps the modular monolith deployable as one container image per bundle.
- **One deployment cell per data-residency region.** Tenants are pinned to a cell; no cross-cell PHI replication. (See ADR-0002.)
- **Graceful degradation:** ai-service, integration-worker and LLM providers are optional at runtime; core-api readiness doesn't depend on them.

## Level 3 (component) diagrams
Written per module when the module is built (M2+), in that module's `docs/` folder.
