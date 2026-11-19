# ADR-0005: AI architecture and governance

- **Status:** Proposed
- **Date:** 2026-11-18

## Decision
1. **Gateway pattern.** `core.ai-gateway` (in core-api) is the only entry point: checks entitlement, permission, budget, region/provider policy, redacts, records audit; then calls **ai-service** (Python) with a signed request context. Browsers never call ai-service.
2. **Provider abstraction** in ai-service: `generate`, `embed`, `rerank` interfaces; providers configured per cell and tenant from an allow-list with flags `{region, dpa, zeroRetention, phiAllowed}`. A deterministic **fake provider** is the default in dev/CI so tests are reproducible and free. Candidate real providers: Anthropic Claude (current models, e.g. `claude-sonnet-5-5` for drafting, `claude-haiku-5-5` for classification), Azure-hosted / regional endpoints, and self-hosted open-weight models for strict residency — final list is a business/legal decision.
3. **Outputs are drafts.** Every AI capability returns a schema-validated structured output stored as `AiDraft {capability, promptVersion, modelId, inputsRef, citations[], status: draft}`; domain modules accept drafts only through a human-approval command executed by a human principal.
4. **No side-effecting tools for models.** Models can't run SQL, call write APIs, publish config, or prescribe. Retrieval is performed by code before the model call.
5. **Permission-aware RAG:** chunks stored with `tenant_id`, `source_document_version_id`, ACL principal set, classification; retrieval filters in SQL; citations re-verified against the user's current permissions at display time. pgvector initially.
6. **Versioning:** prompts are versioned files in `services/ai-service/prompts/` with IDs; model + prompt version recorded on each draft.
7. **Evaluation harness** (`tests/ai-evaluations/`): per capability dataset (synthetic), metrics (groundedness, citation validity, schema validity, refusal correctness, injection resistance), acceptance thresholds; runs in CI against the fake provider (pipeline checks) and on demand against real providers (quality checks).
8. **Fallback:** if AI is unavailable or over budget, features show unavailable and manual workflows continue.
9. **Per-capability intended-use document** required before enabling a capability for customers.

## Consequences
- Some latency overhead from the gateway hop; acceptable.
- Clinical documentation copilot can't launch in a region until an approved provider with PHI flags exists there.
