#!/usr/bin/env bash
# Rebuild BlogSystemFromMindMap site from mind maps and push dist/ to GitHub Pages.
# Does NOT commit /data/mindmaps (use MCP git_sync for the library).
#
# Usage:
#   ./scripts/auto_rebuild.sh              # build + commit + push
#   ./scripts/auto_rebuild.sh --skip-push  # build + commit only
#   ./scripts/auto_rebuild.sh --dry-run    # build only, no git write
#
# Env:
#   BLOG_ROOT   default /root/BlogSystemFromMindMap
#   SCAN_DIR    default /data/mindmaps
#   OUT_DIR     default $BLOG_ROOT/dist
#   BASE_URL    default https://yixiaozi.github.io/BlogSystemFromMindMap
#   GIT_REMOTE  default origin
#   GIT_BRANCH  default master

set -euo pipefail

BLOG_ROOT="${BLOG_ROOT:-/root/BlogSystemFromMindMap}"
SCAN_DIR="${SCAN_DIR:-/data/mindmaps}"
OUT_DIR="${OUT_DIR:-$BLOG_ROOT/dist}"
BASE_URL="${BASE_URL:-https://yixiaozi.github.io/BlogSystemFromMindMap}"
GIT_REMOTE="${GIT_REMOTE:-origin}"
GIT_BRANCH="${GIT_BRANCH:-master}"
SKIP_PUSH=0
DRY_RUN=0

for arg in "$@"; do
  case "$arg" in
    --skip-push) SKIP_PUSH=1 ;;
    --dry-run) DRY_RUN=1 ;;
    -h|--help)
      sed -n '2,22p' "$0"
      exit 0
      ;;
    *)
      echo "Unknown argument: $arg" >&2
      exit 2
      ;;
  esac
done

log() { printf '[%s] %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$*"; }
die() { log "ERROR: $*"; exit 1; }

export PATH="/usr/local/bin:/usr/share/dotnet:${PATH:-/usr/bin:/bin}"
export DOTNET_ROOT="${DOTNET_ROOT:-/usr/share/dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

command -v dotnet >/dev/null 2>&1 || die "dotnet not found"
command -v git >/dev/null 2>&1 || die "git not found"
[[ -d "$BLOG_ROOT/.git" ]] || die "Blog repo missing: $BLOG_ROOT"
[[ -d "$SCAN_DIR" ]] || die "Scan dir missing: $SCAN_DIR"
[[ -f "$BLOG_ROOT/MindmapBlog/MindmapBlog.csproj" ]] || die "MindmapBlog project missing"

cd "$BLOG_ROOT"
log "Blog root: $BLOG_ROOT"
log "Scan dir:  $SCAN_DIR"
log "Out dir:   $OUT_DIR"
log "Base URL:  $BASE_URL"

if [[ "$DRY_RUN" -eq 0 ]]; then
  log "Syncing $GIT_REMOTE/$GIT_BRANCH (ff-only)"
  git fetch "$GIT_REMOTE" "$GIT_BRANCH"
  git merge --ff-only "$GIT_REMOTE/$GIT_BRANCH" \
    || die "Local branch diverged from $GIT_REMOTE/$GIT_BRANCH; fix manually"
fi

log "Ensuring Release build..."
dotnet build MindmapBlog/MindmapBlog.csproj -c Release -v q

LOG_FILE="$(mktemp)"
trap 'rm -f "$LOG_FILE"' EXIT

log "Generating site..."
mkdir -p "$OUT_DIR"
set +e
dotnet run --project MindmapBlog/MindmapBlog.csproj -c Release --no-build -- \
  --scan "$SCAN_DIR" \
  --out "$OUT_DIR" \
  --base-url "$BASE_URL" | tee "$LOG_FILE"
rc=${PIPESTATUS[0]}
set -e
[[ "$rc" -eq 0 ]] || die "MindmapBlog exited with code $rc"

if grep -q "没有解析到文章" "$LOG_FILE"; then
  die "No publishable articles found under $SCAN_DIR (need internet-publish icon nodes)"
fi
if ! grep -qE "已生成 [1-9][0-9]* 篇文章" "$LOG_FILE"; then
  die "Build finished but article count line missing; check generator output"
fi
[[ -f "$OUT_DIR/index.html" ]] || die "Missing $OUT_DIR/index.html after build"

if [[ "$DRY_RUN" -eq 1 ]]; then
  log "Dry-run complete (no git commit/push)."
  exit 0
fi

log "Staging dist/..."
git add -- dist
if git diff --cached --quiet; then
  log "No changes in dist/. Nothing to commit."
  exit 0
fi

CHANGED=$(git diff --cached --name-only | wc -l | tr -d ' ')
MSG="auto: rebuild blog $(date '+%Y-%m-%d %H:%M') (${CHANGED} files)"
log "Committing: $MSG"
git -c user.name="Hermes Agent" -c user.email="hermes@agent.local" commit -m "$MSG"

if [[ "$SKIP_PUSH" -eq 1 ]]; then
  log "Skip push (--skip-push)."
  exit 0
fi

log "Pushing $GIT_REMOTE $GIT_BRANCH..."
git push "$GIT_REMOTE" "$GIT_BRANCH"
log "Done. GitHub Actions will deploy Pages from dist/."
