# Testing HealthOS in GitHub Codespaces

Runs the whole application in the cloud; nothing changes on your PC. Synthetic data only.

## Cost
Personal GitHub accounts include a free monthly Codespaces allowance (Free: 120 core-hours; Pro: 180).
This project uses a 4-core machine, i.e. about **30 hours/month free** (45 with Pro). With the default
spending limit of $0 you are never charged; the codespace stops when the allowance is used. Stop the
codespace when you finish (it also stops after 30 minutes idle) and delete old ones.

## First time (≈ 10 minutes)
1. Install **VS Code** on your PC and the extension **GitHub Codespaces** (sign in with GitHub).
   Using VS Code (not the browser tab) matters: it forwards the app to `http://localhost:…` on your PC,
   which the login server expects.
2. In VS Code: `F1` → **Codespaces: Create New Codespace** → repository `drigvendra/OntogenesisHealthOS`
   → branch `main` (or `dev` for the newest work) → machine **4-core**.
3. Wait for setup to finish (installs .NET, Node, Python and dependencies).
4. Open a terminal in VS Code (`Ctrl+\``) and run:
   ```bash
   bash scripts/dev-start.sh
   ```
   It starts the database, the login server, the API and the admin web app, and prints the password and
   the authenticator setup key.
5. Open **http://localhost:5173** in your browser.

## Trying it out
1. **Sign in** as `synthetic.admin` with the printed password. On the next screen enter the 6-digit code from
   your authenticator app (add the printed setup key once as a time-based account).
2. In the VS Code terminal run `bash scripts/grant-operator.sh synthetic.admin@example.invalid`,
   then **Sign out** and sign in again. You now see **Platform: tenants**.
3. Create a tenant, open it, use **Onboarding** to pick a profile (e.g. *Dental clinic*, India) → **Preview** →
   **Apply**.
4. Under **Tenant administrator**, appoint `synthetic.admin@example.invalid`.
5. Choose the tenant in the **Tenant** selector at the top. Explore **Modules**, **Configuration**,
   **Members & roles**, **Licenses** and **Audit log** (try **Verify chain**).

`synthetic.user` signs in without MFA: useful to see that administrative actions are refused without it.

## Stopping
`bash scripts/dev-stop.sh` (add `--all` to stop the database and login server too), then stop the
codespace from VS Code (`F1` → **Codespaces: Stop Current Codespace**).
