# Data Ownership and Tenant Isolation

## 1. Ownership table (who is the system of record)

| Concept | Owning module | Schema | Contains PHI? | Consumers get it via |
|---|---|---|---|---|
| Tenant, organization, site, department, region cell | core.tenancy | `core_tenancy` | No | Contracts API |
| User, role, permission, assignment | core.identity | `core_identity` | Staff PII | Contracts API, `core.user.*` events |
| Audit event | core.audit | `core_audit` | May reference subject IDs, never payload PHI | Audit query API (permissioned) |
| Product, plan, subscription, entitlement, usage meter | core.licensing | `core_licensing` | No | Entitlement check API |
| Module state per tenant | core.modules | `core_modules` | No | Navigation/route manifest API |
| Setting, form definition, workflow definition, config snapshot | core.configuration | `core_configuration` | No (definitions only) | Resolved-config API |
| Document, version, binary object ref, retention hold | core.documents | `core_documents` + object store | Depends on owner module's classification | Documents API with owner-module ACL callback |
| Workflow instance, task, approval, e-signature | core.workflows | `core_workflows` | References only | Workflow API, events |
| AI request, draft, citation, evaluation result, usage | core.ai-gateway | `core_ai` | Redacted prompts only; drafts classified by caller | AI API |
| Patient, practitioner, encounter, consent, external IDs | clinical.registry | `clinical_registry` | **Yes** | Contracts API, thin events |
| Appointment, clinical note, follow-up, clinic bill | clinicos.core / clinicos.billing | `clinicos_core`, `clinicos_billing` | **Yes** | Contracts API |
| Odontogram, tooth procedures | specialty.dental | `specialty_dental` | **Yes** | Contracts API |
| Vision exam, refraction, IOP | specialty.vision | `specialty_vision` | **Yes** | Contracts API |
| Controlled document metadata, deviation, CAPA, training record | careops.quality | `careops_quality` | Normally no; deviations may mention patients → classified field | Contracts API |
| Connector, mapping, sync job, DLQ entry, external ID link | healthconnect.* | `healthconnect` | Payloads in transit; DLQ payloads encrypted + TTL | Connector API |
| Embeddings / chunks | core.ai-gateway (index) on behalf of owner module | `core_ai` (pgvector) | Mirrors source classification | Retrieval API only, ACL-filtered |

**Rule:** a module never reads another module's schema. Read models that aggregate across modules (e.g., a dashboard) are built in the consuming module from events/APIs and are owned by it.

## 2. Identifiers
- Internal IDs: ULID/UUIDv7 (time-ordered, non-guessable), never reused.
- External IDs: `external_identifier(system_uri, value, assigner, source_of_truth, valid_from, valid_to)` linked to the internal ID; uniqueness per tenant + system.
- Human-facing numbers (MRN, document number, CAPA number) are generated per tenant/site by configurable sequences and are separate from internal IDs.

## 3. Tenant isolation — defense in depth

| Layer | Control | Test |
|---|---|---|
| Deployment | Tenants pinned to a regional cell; optional dedicated DB or dedicated cell for enterprise/pharma | Deployment config test |
| Database | `tenant_id NOT NULL` on every tenant-owned table; **RLS enabled and FORCED**; policy `tenant_id = current_setting('app.tenant_id')::uuid`; app roles lack `BYPASSRLS` and are not table owners | Direct-SQL tests with wrong/missing tenant setting |
| Database (module) | One DB role per module with grants only on its own schema | Test: module role cannot `SELECT` from another schema |
| Data access | EF Core global query filter on `tenant_id` (second layer); `SET LOCAL app.tenant_id` in a connection interceptor per transaction | Unit + integration tests |
| API | Tenant resolved from the authenticated token claim, never from a request body/header the client controls; site/department scope via ABAC | Cross-tenant API matrix test for every endpoint |
| Object storage | Key prefix `tenant/{tenantId}/module/{moduleId}/...`; access only through core.documents with pre-signed short-lived URLs | Pre-signed URL scope test |
| AI/RAG | Vector queries always filtered by `tenant_id` + ACL principal set *inside* the SQL query, not post-filtered | Cross-tenant RAG denial test |
| Cache | Cache keys prefixed with tenant id; no shared cache in M1–M4 | Code review + test |
| Jobs/events | Every job and event carries `tenant_id`; worker sets DB tenant context before handling | Handler test |
| Platform operators | Cross-tenant access only via audited break-glass role, time-boxed | Audit test |

Fail-closed: if `app.tenant_id` is unset, RLS returns no rows and inserts fail.

## 4. Retention and deletion
- Each record type declares a retention class (config-driven per region/industry profile): e.g. `clinical-record`, `regulated-record`, `audit`, `transient`.
- Deletion requests (GDPR Art. 17 / DPDP) go through a workflow that checks legal retention holds; non-deletable records get restricted processing instead.
- Module disable/uninstall never deletes retained data; uninstall with retained data is refused unless a documented, approved purge workflow runs after retention expiry.

## 5. PHI handling rules
- No PHI in logs, traces, metrics labels, exception messages, analytics, or LLM prompts to providers without an approved DPA + region flag. A redaction layer in logging and the AI gateway enforces this; tests use synthetic PHI canaries to detect leaks.
- Field-level encryption (application-level, envelope keys per tenant) for designated high-sensitivity fields (e.g., national IDs, ABHA numbers).
