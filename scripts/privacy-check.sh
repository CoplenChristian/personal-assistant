#!/bin/sh
set -eu

repo_root=$(git rev-parse --show-toplevel)
path_file=$(mktemp)
failed=0

cleanup() {
  rm "$path_file"
}
trap cleanup EXIT HUP INT TERM

{
  git -C "$repo_root" ls-files
  git -C "$repo_root" diff --cached --name-only --diff-filter=ACMR
  git -C "$repo_root" ls-files --others --exclude-standard
} | LC_ALL=C sort -u > "$path_file"

reject() {
  printf 'privacy-check: %s: %s\n' "$1" "$2" >&2
  failed=1
}

check_path() {
  path=$1
  path_lower=$(printf '%s' "$path" | tr '[:upper:]' '[:lower:]')

  case "$path_lower" in
    runtime/*)
      [ "$path_lower" = "runtime/readme.md" ] || reject "$path" "runtime state is not tracked"
      ;;
    agents/*/memory.md|agents/*/handoff.md|shared/user.md)
      reject "$path" "populated personal state must stay under ignored runtime/"
      ;;
    agents/*/local/*|agents/*/transcripts/*|agents/*/browser-profiles/*|agents/*/mail-cache/*|agents/*/screenshots/*|agents/*/downloads/*)
      reject "$path" "agent-local state is not tracked"
      ;;
    node_modules/*|*/node_modules/*|bin/*|*/bin/*|obj/*|*/obj/*|testresults/*|*/testresults/*|coverage/*|*/coverage/*)
      reject "$path" "generated dependency/build/test output is not tracked"
      ;;
    *.sqlite|*.sqlite-*|*.sqlite3|*.sqlite3-*|*.db|*.db-*|*.log|*.jsonl|*.har|*.p12|*.pfx|*.token)
      reject "$path" "runtime or credential artifact is not tracked"
      ;;
    storage-state*.json|*/storage-state*.json|cookies*|*/cookies*|credentials/*|*/credentials/*|secrets/*|*/secrets/*|oauth/*|*/oauth/*|tokens/*|*/tokens/*|cache/*|*/cache/*|caches/*|*/caches/*|vault/*|*/vault/*)
      reject "$path" "credential or browser state path is not tracked"
      ;;
    .env|.env.*)
      [ "$path_lower" = ".env.example" ] || reject "$path" "local environment files are not tracked"
      ;;
    personalassistantvault/*|*/personalassistantvault/*)
      reject "$path" "personal document vault is outside the repository"
      ;;
  esac
}

check_content() {
  path=$1
  full_path=$repo_root/$path

  if git -C "$repo_root" diff --cached --name-only --diff-filter=ACMR | grep -F -x -q "$path"; then
    if git -C "$repo_root" show ":$path" 2>/dev/null | LC_ALL=C grep -Eni -m1 \
      'BEGIN (RSA|OPENSSH|EC|DSA) PRIVATE KEY|ghp_[A-Za-z0-9_]+|github_pat_[A-Za-z0-9_]+|xox[baprs]-[A-Za-z0-9-]+|AKIA[0-9A-Z]{16}|sk-[A-Za-z0-9_-]{16,}' \
      >/dev/null 2>&1; then
      reject "$path" "credential-shaped content detected in staged blob"
    fi
    return 0
  fi

  [ -f "$full_path" ] || return 0
  if LC_ALL=C grep -Eni -m1 \
    'BEGIN (RSA|OPENSSH|EC|DSA) PRIVATE KEY|ghp_[A-Za-z0-9_]+|github_pat_[A-Za-z0-9_]+|xox[baprs]-[A-Za-z0-9-]+|AKIA[0-9A-Z]{16}|sk-[A-Za-z0-9_-]{16,}' \
    "$full_path" >/dev/null 2>&1; then
    reject "$path" "credential-shaped content detected"
  fi
}

while IFS= read -r path; do
  [ -n "$path" ] || continue
  check_path "$path"
  check_content "$path"
done < "$path_file"

if [ "$failed" -ne 0 ]; then
  printf 'privacy-check: failed\n' >&2
  exit 1
fi

printf 'privacy-check: passed\n'
