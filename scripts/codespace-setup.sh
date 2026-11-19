#!/usr/bin/env bash
# One-time setup inside a Codespace / dev container: tool versions and dependencies.
set -euo pipefail
cd "$(dirname "$0")/.."

corepack enable
corepack prepare pnpm@10 --activate
pnpm install --frozen-lockfile

# Python for the AI service, via uv.
curl -LsSf https://astral.sh/uv/install.sh | sh
export PATH="$HOME/.local/bin:$PATH"
(cd services/ai-service && uv python install 3.14 && uv sync)

dotnet restore HealthOS.slnx

echo
echo "Setup complete. Start everything with:  bash scripts/dev-start.sh"
