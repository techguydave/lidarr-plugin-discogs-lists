#!/usr/bin/env bash
# Tests the versioning and read-only scripts against throwaway repositories and sources.
set -euo pipefail

SCRIPTS="$(cd "$(dirname "$0")" && pwd)"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/plugin-scripts.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

expect() {
    if [ "$2" != "$3" ]; then
        echo "FAIL $1: expected '$3', got '$2'" >&2
        exit 1
    fi
    echo "ok   $1"
}

commit() {
    git commit -q --allow-empty -m "$1" ${2:+-m "$2"}
}

# Publishes the pre-release for HEAD the way the workflow does.
publish() {
    version="$("$SCRIPTS/next-version.sh")"
    git tag -f "v$version" >/dev/null
}

git init -q "$WORK/repo"
cd "$WORK/repo"
git config user.name test
git config user.email test@example.com
git config commit.gpgsign false
git config tag.gpgsign false

commit "feat: initial"
publish; expect "first pre-release" "$version" "0.1.0-develop.1"
publish; expect "re-run keeps the version" "$version" "0.1.0-develop.1"
commit "fix: bug"
publish; expect "fix bumps patch" "$version" "0.1.1-develop.2"
commit "docs: words"; commit "feat(api): thing"
publish; expect "feat bumps minor" "$version" "0.2.0-develop.3"
commit "feat!: break"
publish; expect "breaking bumps minor before 1.0" "$version" "0.3.0-develop.4"
commit "fix: more" "BREAKING CHANGE: setting removed"
publish; expect "breaking footer is detected" "$version" "0.4.0-develop.5"
commit "chore(release): prepare"
publish; expect "chore bumps patch" "$version" "0.4.1-develop.6"
expect "release promotes matching pre-release" "$("$SCRIPTS/release-version.sh")" "0.4.1"
git tag v0.4.1
commit "fix: after release"
publish; expect "continues after a stable tag" "$version" "0.4.2-develop.7"
git tag v1.0.0
commit "feat!: break again"
publish; expect "breaking bumps major after 1.0" "$version" "2.0.0-develop.8"
echo change > file && git add file && commit "fix: unpublished"
if "$SCRIPTS/release-version.sh" 2>/dev/null; then
    echo "FAIL unpublished content must not be releasable" >&2
    exit 1
fi
echo "ok   unpublished content is not releasable"

mkdir -p "$WORK/source"
cp "$SCRIPTS/../src/Lidarr.Plugin.DiscogsLists/Api/DiscogsTransport.cs" "$WORK/source/"
"$SCRIPTS/check-read-only.sh" "$WORK/source" >/dev/null
echo "ok   read-only check accepts the transport"

for violation in 'client.PostAsync(uri, body);' 'var m = HttpMethod.Delete;' 'new HttpClient();' '_client.Put<Thing>(request);' 'request.Method = other;'; do
    echo "$violation" > "$WORK/source/Bad.cs"
    if "$SCRIPTS/check-read-only.sh" "$WORK/source" >/dev/null 2>&1; then
        echo "FAIL read-only check missed: $violation" >&2
        exit 1
    fi
    echo "ok   read-only check rejects: $violation"
done
