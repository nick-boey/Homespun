#!/usr/bin/env bash
#
# coverage-gate.sh — enforce Constitution §V in CI.
#
# Called from .github/workflows/ci.yml in the `coverage-gate` job. For each of
# server / web / worker:
#   1. Runs diff-cover against origin/main to compute delta coverage on the
#      PR's changed lines, failing below 80%.
#   2. Parses overall module coverage from the Cobertura root line-rate.
#   3. Compares the overall against the stored floor in floors.json (ratchet).
# Aggregates the results into reports/coverage-comment.md which the workflow
# posts/updates on the PR. Exits 1 if any per-module gate fails.
#
# Env (with defaults):
#   ARTIFACTS_DIR    artifacts                  root dir of downloaded coverage artifacts
#   FLOORS_FILE      floors.json                per-module ratchet floors; may be absent
#   REPORTS_DIR      reports                    output directory for this script
#   COMPARE_BRANCH   origin/main                branch/ref diff-cover compares against
#   DELTA_THRESHOLD  80                         per-module delta gate threshold, %
#   TARGET_60        0.60                       dated visibility target #1 (line-rate)
#   TARGET_80        0.80                       dated visibility target #2 (line-rate)
#   TARGET_60_DATE   2026-06-30                 dated visibility target #1 date
#   TARGET_80_DATE   2026-09-30                 dated visibility target #2 date
#
set -euo pipefail

ARTIFACTS_DIR="${ARTIFACTS_DIR:-artifacts}"
FLOORS_FILE="${FLOORS_FILE:-floors.json}"
REPORTS_DIR="${REPORTS_DIR:-reports}"
COMPARE_BRANCH="${COMPARE_BRANCH:-origin/main}"
DELTA_THRESHOLD="${DELTA_THRESHOLD:-80}"
TARGET_60="${TARGET_60:-0.60}"
TARGET_80="${TARGET_80:-0.80}"
TARGET_60_DATE="${TARGET_60_DATE:-2026-06-30}"
TARGET_80_DATE="${TARGET_80_DATE:-2026-09-30}"

mkdir -p "$REPORTS_DIR"

# Ensure the compare branch is reachable for diff-cover's merge-base lookup.
# The CI workflow does a shallow clone + explicit fetch; this is defensive.
git fetch --depth=200 origin main >/dev/null 2>&1 || true

# Module descriptors: name | cobertura xml | diff-cover --src-roots
MODULES=(
  "server|${ARTIFACTS_DIR}/coverage-server/Cobertura.xml|src/Homespun.Server"
  "web|${ARTIFACTS_DIR}/coverage-web/cobertura-coverage.xml|src/Homespun.Web"
  "worker|${ARTIFACTS_DIR}/coverage-worker/cobertura-coverage.xml|src/Homespun.Worker"
)

declare -A DELTA_PCT DELTA_OK OVERALL_PCT FLOOR_PCT RATCHET_OK MODULE_PRESENT

# Extract root line-rate from a Cobertura file (returns 0 if missing/NaN).
cobertura_line_rate() {
  local xml="$1"
  python3 - "$xml" <<'PY'
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

# Extract diff-cover total_percent_covered from its JSON report (0 if missing).
diffcover_percent() {
  local json="$1"
  python3 - "$json" <<'PY'
import json, sys
try:
    with open(sys.argv[1]) as f:
        d = json.load(f)
    v = d.get("total_percent_covered")
    if v is None:
        # fallback for older schemas
        v = d.get("report", {}).get("total_percent_covered", 0)
    print(v)
except Exception:
    print(0)
PY
}

# Compare two floats with small tolerance: echo 1 if a >= b - eps else 0.
ge_tolerant() {
  python3 -c "import sys; a=float(sys.argv[1]); b=float(sys.argv[2]); print(1 if a + 1e-4 >= b else 0)" "$1" "$2"
}

overall_failure=0

for entry in "${MODULES[@]}"; do
  IFS='|' read -r module xml src <<<"$entry"

  if [[ ! -f "$xml" ]]; then
    MODULE_PRESENT[$module]=0
    echo "::warning::coverage-gate: missing coverage artifact for $module ($xml)"
    continue
  fi
  MODULE_PRESENT[$module]=1

  md="$REPORTS_DIR/diff-$module.md"
  json="$REPORTS_DIR/diff-$module.json"

  # diff-cover exits non-zero when under --fail-under; capture exit without
  # aborting so we evaluate every module and produce a full PR comment.
  set +e
  diff-cover "$xml" \
    --compare-branch "$COMPARE_BRANCH" \
    --src-roots "$src" \
    --fail-under "$DELTA_THRESHOLD" \
    --format "markdown:$md" \
    --format "json:$json"
  dc_exit=$?
  set -e

  delta="$(diffcover_percent "$json")"
  DELTA_PCT[$module]="$delta"
  if [[ $dc_exit -eq 0 ]]; then
    DELTA_OK[$module]=1
  else
    DELTA_OK[$module]=0
    overall_failure=1
  fi

  overall="$(cobertura_line_rate "$xml")"
  OVERALL_PCT[$module]="$overall"

  if [[ -f "$FLOORS_FILE" ]]; then
    floor="$(jq -r --arg m "$module" '.[$m] // empty' "$FLOORS_FILE")"
    if [[ -z "$floor" ]]; then
      FLOOR_PCT[$module]=""
      RATCHET_OK[$module]=1
    else
      FLOOR_PCT[$module]="$floor"
      if [[ "$(ge_tolerant "$overall" "$floor")" == "1" ]]; then
        RATCHET_OK[$module]=1
      else
        RATCHET_OK[$module]=0
        overall_failure=1
      fi
    fi
  else
    FLOOR_PCT[$module]=""
    RATCHET_OK[$module]=1
  fi
done

# -- Build the PR comment --------------------------------------------------

fmt_pct() { python3 -c "print(f'{float(\"$1\")*100:.1f}%')"; }
fmt_gap() { python3 -c "print(f'{(float(\"$1\")-float(\"$2\"))*100:+.1f} pp')"; }

comment_file="$REPORTS_DIR/coverage-comment.md"
{
  echo "## Coverage Report"
  echo
  echo "| Module | Delta (changed lines) | Overall | Floor (\`main\`) | Δ vs floor | ${TARGET_60_DATE} (60%) | ${TARGET_80_DATE} (80%) |"
  echo "|---|---|---|---|---|---|---|"
  for entry in "${MODULES[@]}"; do
    IFS='|' read -r module _ _ <<<"$entry"
    if [[ "${MODULE_PRESENT[$module]:-0}" == 0 ]]; then
      echo "| $module | _no coverage artifact_ | — | — | — | — | — |"
      continue
    fi
    overall="${OVERALL_PCT[$module]}"
    delta="${DELTA_PCT[$module]}"
    floor="${FLOOR_PCT[$module]}"

    delta_icon="❌"; [[ "${DELTA_OK[$module]}" == 1 ]] && delta_icon="✅"
    ratchet_icon="❌"; [[ "${RATCHET_OK[$module]}" == 1 ]] && ratchet_icon="✅"

    if [[ -z "$floor" ]]; then
      floor_cell="_baseline missing_"
      ratchet_cell="—"
    else
      floor_cell="$(fmt_pct "$floor")"
      ratchet_cell="$(fmt_gap "$overall" "$floor") ${ratchet_icon}"
    fi

    gap60="$(fmt_gap "$overall" "$TARGET_60")"
    gap80="$(fmt_gap "$overall" "$TARGET_80")"

    printf '| %s | %.1f%% %s | %s | %s | %s | %s | %s |\n' \
      "$module" "$delta" "$delta_icon" \
      "$(fmt_pct "$overall")" \
      "$floor_cell" \
      "$ratchet_cell" \
      "$gap60" "$gap80"
  done
  echo
  echo "### Gates"
  any_delta_fail=0; any_ratchet_fail=0
  for entry in "${MODULES[@]}"; do
    IFS='|' read -r module _ _ <<<"$entry"
    [[ "${MODULE_PRESENT[$module]:-0}" == 0 ]] && continue
    [[ "${DELTA_OK[$module]}" == 0 ]] && any_delta_fail=1
    [[ "${RATCHET_OK[$module]}" == 0 ]] && any_ratchet_fail=1
  done
  if [[ $any_delta_fail -eq 0 ]]; then
    echo "- **Delta ≥ ${DELTA_THRESHOLD}% on changed lines** (per module): ✅"
  else
    echo "- **Delta ≥ ${DELTA_THRESHOLD}% on changed lines** (per module): ❌"
  fi
  if [[ $any_ratchet_fail -eq 0 ]]; then
    echo "- **Ratchet** (overall ≥ \`main\` floor): ✅"
  else
    echo "- **Ratchet** (overall ≥ \`main\` floor): ❌"
  fi
  echo "- **Dated targets**: visibility only (not enforced by this gate)."
  echo
  echo "<details><summary>Per-file uncovered lines (diff-cover)</summary>"
  echo
  for entry in "${MODULES[@]}"; do
    IFS='|' read -r module _ _ <<<"$entry"
    md="$REPORTS_DIR/diff-$module.md"
    if [[ -f "$md" ]]; then
      echo "#### $module"
      echo
      cat "$md"
      echo
    fi
  done
  echo "</details>"
  echo
  if [[ -f "$FLOORS_FILE" ]]; then
    src="$(jq -r '.commit // "unknown"' "$FLOORS_FILE")"
    updated="$(jq -r '.updated_at // "unknown"' "$FLOORS_FILE")"
    echo "_Floors from cache \`coverage-floors-v1-main\` — recorded at ${updated} against commit \`${src}\`._"
  else
    echo "_Floors baseline missing — this PR's numbers will seed the floor on merge to \`main\`._"
  fi
} > "$comment_file"

echo "coverage-gate: wrote $comment_file"
exit "$overall_failure"
