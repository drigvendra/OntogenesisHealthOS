# Product Vision

## One sentence
One secure, modular platform on which Ontogenesis ships several independently sold healthcare and life-sciences products, which customers can run alone, combine, or grow into a complete hospital system, without the products being coupled to each other.

## Problem
- Clinics, hospitals, pharma QA teams and equipment service providers each buy separate, poorly integrated systems, or one monolithic HMS that's too heavy for a small clinic and too rigid for specialties.
- Configuring a system for a specialty (dental, ophthalmology) or a regulatory context (India vs. EU, GxP) usually needs vendor code changes.
- AI features in healthcare are often bolted on without provenance, permission awareness or human review.

## Principles
1. **Standalone first, composable second.** Every product must be sellable and deployable on its own (Core + product). Combinations go through versioned contracts and bridge modules.
2. **Configuration over code.** Specialties, forms, workflows, regions and bundles are configuration (versioned, previewed, approved, rollback-able), not forks.
3. **AI assists; humans decide.** AI produces drafts with citations; authorized humans approve. Products keep working when AI is turned off or unavailable.
4. **Isolation by construction.** Tenant isolation at the database, module isolation at the schema and the build level. UI filtering doesn't count as isolation.
5. **Evidence, not claims.** We maintain risk registers and compliance evidence matrices; we don't market certifications we don't have.

## Target customers & first wedge
| Segment | Product | Why first/later |
|---|---|---|
| Pharma / biotech / med-device QA teams | CareOps Quality AI | **First product (M5).** No patient data, so lower privacy risk and no medical-device question; strong AI value (SOP drafting, comparison, training). |
| General, dental and eye clinics (India first) | ClinicOS AI + packs | Second (M6–M7). Handles PHI; AI is limited to draft documentation with clinician attestation. |
| Hospitals | HospitalOS AI | Later release trains (M10). Needs clinical advisors and department-level pilots. |
| Equipment service providers | MedTechOS AI | Separate release train (M11). |
| Organizations with an existing EHR/HMS | AI add-on + HealthConnect | After M8. |

## Explicitly out of scope for initial releases
Autonomous diagnosis, triage, treatment recommendations, prescribing/medication decisions, image interpretation, and any feature that would be a medical device under its intended use, until separately authorized (see `docs/compliance/regulatory-scope.md`).

## What success looks like (from spec)
One maintainable platform that supports standalone products, optional combinations and a future complete HMS without coupling products to each other. A polished UI without secure workflows, tests and verifiable modularity doesn't count as success.
