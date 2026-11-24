#!/usr/bin/env bash
set -euo pipefail
BASE="${BASE_URL:-http://localhost:8080}"
QUESTION="${1:-Why did calls fail on trunk-du-backup this morning?}"
curl -sS -X POST "$BASE/api/agent/ask" \
  -H "Content-Type: application/json" \
  -d "$(python3 -c 'import json,sys; print(json.dumps({"question": sys.argv[1]}))' "$QUESTION")"
echo
