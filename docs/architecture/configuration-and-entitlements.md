# Configuration Precedence and Entitlements

## 1. Configuration precedence

Levels, lowest to highest specificity:

```
1 global defaults            (shipped with module, in its settings schema)
2 country / regulatory profile (e.g. IN, EU-DE, EU-FR — not "EU" as one jurisdiction)
3 industry profile           (clinic, hospital, pharma-QA, medtech-service)
4 specialty                  (dental, vision, ...)
5 organization
6 site / department
7 role
8 user
```

### Setting definition (declared in the module's settings schema)
```json
{
  "key": "careops.document.reviewIntervalMonths",
  "type": "integer",
  "unit": "month",
  "default": 24,
  "constraints": { "min": 1, "max": 60 },
  "allowedScopes": ["global", "country", "industry", "organization", "site"],
  "owner": "careops.quality",
  "sensitivity": "normal",
  "effectiveDating": true,
  "migration": { "from": "1.x", "strategy": "copy" }
}
```

### Resolution algorithm
1. Collect values for the key from all levels that apply to the request context (tenant, org, site, role(s), user), ignoring scopes not in `allowedScopes`.
2. Walk from **lowest to highest** specificity; each level overrides the previous **unless** a lower level set `locked: true` (e.g., a regulatory profile locks a minimum retention). A locked value can't be overridden; attempts fail validation at authoring time, not at runtime.
3. Apply **constraints** accumulated from all levels (a regulatory profile can narrow `min`/`max` without fixing the value). The final value must satisfy the intersection.
4. Multiple roles: the setting declares a merge strategy (`most-restrictive`, `most-permissive`, `union`, `first-by-role-priority`); default `most-restrictive`.
5. Effective dating: only values with `effectiveFrom ≤ now < effectiveTo` participate.
6. Result includes **provenance**: which level supplied the value and which levels were locked/constrained — shown in the admin UI ("why is this value 7?").

### Lifecycle
`draft → validated → previewed → approved → published (immutable snapshot) → superseded`; rollback = publish a new snapshot equal to a previous one (never mutate history). Every transition is audited with actor, diff and reason. AI Configuration Copilot can only create `draft`.

Forms and workflow definitions follow the same lifecycle and are versioned; running workflow instances stay pinned to the definition version they started with.

## 2. Licensing & entitlements

### Model
```
Product (CAREOPS, CLINICOS, DENTAL, VISION, HOSPITALOS, MEDTECHOS, HEALTHCONNECT, AI_*)
  └─ Feature (e.g. clinicos.billing, ai.clinical-doc-copilot)
Plan (bundle of product features + limits, priced outside the platform)
Subscription (tenant, plan, term, status)
  └─ Entitlement (tenant, feature, scope: tenant|site, seats?, usage meter?, validFrom, validTo, source: subscription|trial|manual-grant)
UsageMeter (tenant, meter: ai.tokens|ai.requests|connector.messages|storage.gb, period, limit, consumed)
```

- **No product assumptions in code.** Code asks `IEntitlements.Check(tenant, feature, scope)`; which plan grants it is data.
- Seat licenses: counted on named active users holding a role that requires the feature.
- Usage-based AI: pre-call budget reservation in the AI gateway; overrun blocked, not billed silently.
- **Expiry behavior:** entitlement expiry → grace period (configurable) → module becomes **read-only**, never deleted. Regulated record access for audit/export stays available.
- Licence data source: in M2, entitlements are managed in-platform by platform operators; signed offline licence files for on-prem deployments are a later item (backlog).
- Every entitlement change is audited.
