# Risk Register

Likelihood / Impact: L, M, H. Review at every milestone.

| ID | Risk | L | I | Mitigation | Owner | Status |
|---|---|---|---|---|---|---|
| R-01 | Cross-tenant data exposure | M | H | RLS FORCED, token-derived tenant, cross-tenant test matrix (M2) | Eng lead | Open — M2 |
| R-02 | PHI leaks into logs, traces or LLM prompts | M | H | Classification + redaction (done for classified logging), AI gateway redaction (M4); analyzer to forbid unclassified logging (backlog) | Eng lead | Partially mitigated |
| R-03 | AI output treated as final clinical/quality record | M | H | Draft-only outputs, human approval gates (M4/M5) | Product | Open — M4 |
| R-04 | Product coupling makes standalone sales impossible | M | H | Architecture tests (done), bundle build matrix (M3) | Architect | Partially mitigated |
| R-05 | Regulatory misclassification of AI documentation features (MDR/CDSCO, EU AI Act) | M | H | Intended-use docs; counsel review before launch | Owner | Open |
| R-06 | **Development machine cannot run locally built .NET code (Smart App Control)** | H | M | Owner disables SAC on dev machine, or use CI/containers/cloud dev env | Owner | **Open — blocking** |
| R-07 | **No container runtime (WSL2 missing)** | H | M | Install WSL2 | Owner | **Open — blocking** |
| R-08 | Hand-written EF migration drifts from model | L | M | `Model_has_no_pending_changes` test; EF refuses pending changes | Eng lead | Mitigated once CI runs |
| R-09 | Supply-chain vulnerability in dependencies | M | M | Lockfiles, audits failing builds, exception register with expiry | Eng lead | Mitigated |
| R-10 | Scope far exceeds team capacity | H | H | CareOps first; re-plan after M5 | Owner | Open |
| R-11 | Keycloak operational burden in production | M | M | Allow customer IdP federation; managed option later | Architect | Open |
