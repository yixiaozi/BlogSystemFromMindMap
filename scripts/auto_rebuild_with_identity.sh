#!/usr/bin/env bash
# Compatibility wrapper: Hermes previously wrote a stub here.
# Real implementation lives in auto_rebuild.sh
set -euo pipefail
DIR="$(cd "$(dirname "$0")" && pwd)"
exec "$DIR/auto_rebuild.sh" "$@"
