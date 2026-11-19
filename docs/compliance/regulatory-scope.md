# Regulatory Scope (engineering view — not legal advice)

> This document identifies which regulations **may apply** so that engineering builds the right controls and the business engages qualified regulatory/legal counsel. It doesn't assert compliance with anything. Every "applicability" below needs confirmation by counsel before a product is sold in that market.

## 1. Intended-use stance for initial releases
| Feature class | Initial release? | Reasoning |
|---|---|---|
| Quality management, document control, CAPA, training (CareOps) | Yes | Not a medical device; may be a GxP computerized system for pharma customers → validation support needed |
| Administrative clinic workflows (registration, scheduling, billing) | Yes | Generally not a medical device |
| Clinical documentation drafting (AI) with clinician attestation | Yes, gated | Intended use: transcription/drafting aid, no diagnostic suggestion. Counsel to confirm MDR Rule 11 / CDSCO stance before launch |
| Diagnosis, triage, treatment/medication suggestions, image interpretation, risk scores | **No** | Likely medical-device software (SaMD); requires intended-use, ISO 14971 risk management, IEC 62304, clinical evaluation, conformity assessment |

## 2. Applicability matrix
| Regime | Region | Likely relevant to | Engineering controls we build | Owner action |
|---|---|---|---|---|
| GDPR | EU | All products processing personal data of EU subjects | Consent/purpose model, DSAR & erasure workflows with retention exceptions, records of processing, encryption, breach-detection logging, EU cell | DPA templates, DPIA for ClinicOS/HospitalOS, transfer assessments, member-state health-data law review |
| EU AI Act | EU | AI features; high-risk if safety component of a regulated medical device or listed use | AI inventory, intended-use per capability, logging, human oversight, transparency labels, evals | Counsel classification per AI capability; timelines per phased application |
| MDR 2017/745 / IVDR | EU | Only if a feature meets medical-device definition | Feature flags to exclude device-like features; design-history docs if ever pursued | Intended-use statements reviewed by regulatory |
| EHDS | EU | EHR systems (HospitalOS/ClinicOS) — phased obligations | FHIR-based interoperability, logging | Track implementing acts |
| DPDP Act 2023 + DPDP Rules | India | All products with Indian data principals | Consent manager compatibility, notice, data-principal rights, breach logging, retention | Confirm commencement dates of each provision; significant-data-fiduciary assessment |
| ABDM (ABHA, HFR, HPR, consent artefacts) | India | ClinicOS/HospitalOS if integrated | ABDM connector in HealthConnect (M8+), FHIR profiles per NRCeS | Sandbox registration, certification per ABDM process |
| Medical Devices Rules 2017 (CDSCO) | India | Device-like software | Same exclusions as MDR | Counsel review |
| Clinical Establishments / state rules, record retention | India | ClinicOS/HospitalOS | Configurable retention classes per region profile | Counsel to supply retention periods |
| EU GMP Annex 11, 21 CFR Part 11 | Pharma customers | CareOps (GxP profile) | Audit trail, e-signatures (re-auth, meaning, linkage), access control, validation pack (URS/FS/traceability/IQ/OQ templates), change control | Customer performs validation; we supply evidence |
| ISO 13485 / IEC 62304 / ISO 14971 | MedTech | Only if we build device software; also customers' QMS needs (CareOps supports them as a tool) | Traceability features in CareOps | Decide if Ontogenesis itself pursues ISO 13485 |
| ISO 27001 / SOC 2 | All | Enterprise sales | Security controls, evidence collection | Business decision, ~M12 |

## 3. Artefacts to maintain
- `docs/compliance/evidence-matrix.md` — requirement → control → test/evidence → status (created in M1, populated each milestone).
- `docs/compliance/risk-register.md` — risks, likelihood/severity, mitigations, owner (created in M1).
- `docs/compliance/intended-use/<ai-capability>.md` — one per AI capability before it ships.

## 4. Things we'll never write in product or marketing copy without counsel sign-off
"Compliant", "certified", "validated", "HIPAA/GDPR/Part 11 ready", "FDA/CE/CDSCO approved", "diagnoses", "recommends treatment".
