# Dependency Vulnerability Exceptions

CI fails on any moderate-or-higher advisory unless it is listed here **and** in the tool's ignore list. Each entry needs a justification, an owner and a review date; expired entries must be re-reviewed or removed.

| Advisory | Package / path | Severity | Shipped to users? | Justification | Owner | Added | Review by |
|---|---|---|---|---|---|---|---|
| GHSA-fc5q-39xc-vjpf | `picomatch` ≤ 4.0.7 via `vite` (admin-web devDependency) | High (ReDoS) | No — build-time tool only; never bundled | Exploit needs attacker-controlled glob patterns in our own build config. No patched version published at time of review. | Engineering lead | 2026-11-18 | 2026-12-18 |
