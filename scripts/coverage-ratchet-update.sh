#!/usr/bin/env bash
#
# coverage-ratchet-update.sh — update per-module coverage floors after merge.
#
# Called from .github/workflows/coverage-ratchet-update.yml on push to main
# (and a weekly safety-net cron). Reads the current floors.json (if any),
# parses the overall line-rate from each module's freshly-produced Cobertura
# report, and writes floors.json with floor = max(existing, new). Monotone:
# a single flaky lower reading never lowers the floor.
#
# Env (with defaults):
#   ARTIFACTS_DIR   artifacts
#   FLOORS_FILE     floors.json
#   COMMIT_SHA      (defaults to $GITHUB_SHA or `git rev-parse HEAD`)
#
set -euo pipefail

ARTIFACTS_DIR="${ARTIFACTS_DIR:-artifacts}"
FLOORS_FILE="${FLOORS_FILE:-floors.json}"
COMMIT_SHA="${COMMIT_SHA:-${GITHUB_SHA:-$(git rev-parse HEAD)}}"

MODULES=(
  "server|${ARTIFACTS_DIR}/coverage-server/Cobertura.xml"
  "web|${ARTIFACTS_DIR}/coverage-web/cobertura-coverage.xml"
  "worker|${ARTIFACTS_DIR}/coverage-worker/cobertura-coverage.xml"
)

cobertura_line_rate() {
  python3 - "$1" <<'PY'
import math, sys, xml.etree.ElementTree as ET
try:
    root = ET.parse(sys.argv[1]).getroot()
    raw = root.get("line-rate", "0")
    val = float(raw)
    print("0" if (math.isnan(val) or math.isinf(val)) else raw)
except Exception:
    print("0")
PY
}

# Read existing floors into a JSON object (or start empty).
if [[ -f "$FLOORS_FILE" ]]; then
  existing="$(cat "$FLOORS_FILE")"
else
  existing='{}'
fi

new_floors='{}'
for entry in "${MODULES[@]}"; do
  IFS='|' read -r module xml <<<"$entry"
  if [[ ! -f "$xml" ]]; then
    echo "::warning::ratchet-update: missing coverage artifact for $module ($xml) — skipping"
    continue
  fi
  current="$(cobertura_line_rate "$xml")"
  prev="$(jq -r --arg m "$module" '.[$m] // empty' <<<"$existing")"
  if [[ -z "$prev" ]]; then
    next="$current"
  else
    next="$(python3 -c "print(max(float('$prev'), float('$current')))")"
  fi
  new_floors="$(jq --arg m "$module" --argjson v "$next" '.[$m] = $v' <<<"$new_floors")"
  echo "ratchet-update: $module overall=$current prev_floor=${prev:-none} new_floor=$next"
done

final="$(jq \
  --argjson floors "$new_floors" \
  --arg sha "$COMMIT_SHA" \
  --arg updated "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  '{server: $floors.server, web: $floors.web, worker: $floors.worker, commit: $sha, updated_at: $updated}' \
  <<<'{}')"

# Drop null keys (modules whose artifact was missing this run) so the floor
# from the previous run survives. Re-apply them from the existing file.
final="$(jq -n \
  --argjson prev "$existing" \
  --argjson new "$final" \
  '{
     server: ($new.server // $prev.server),
     web:    ($new.web    // $prev.web),
     worker: ($new.worker // $prev.worker),
     commit: $new.commit,
     updated_at: $new.updated_at
   } | with_entries(select(.value != null))')"

echo "$final" > "$FLOORS_FILE"
echo "ratchet-update: wrote $FLOORS_FILE"
cat "$FLOORS_FILE"
