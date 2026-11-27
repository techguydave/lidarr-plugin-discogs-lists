#!/usr/bin/env bash
# Prints the body of a GitHub release.
# Usage: scripts/release-notes.sh stable <version>
#        scripts/release-notes.sh prerelease <version>
set -euo pipefail

cd "$(dirname "$0")/.."

CHANNEL="${1:?channel required}"
VERSION="${2:?version required}"
REPOSITORY="https://github.com/techguydave/lidarr-plugin-discogs-lists"
LIDARR_VERSION="$(tr -d '[:space:]' < lidarr.version)"

# The CHANGELOG section under the given heading, without the heading itself.
section() {
    awk -v heading="## [$1]" '
        index($0, heading) == 1 { found = 1; next }
        found && (/^## / || /^\[[^]]+\]: /) { exit }
        found { print }
    ' CHANGELOG.md
}

case "$CHANNEL" in
    stable)
        notes="$(section "$VERSION")"
        if [ -z "$(printf '%s' "$notes" | tr -d '[:space:]')" ]; then
            echo "CHANGELOG.md has no entry for $VERSION." >&2
            exit 1
        fi
        printf '%s\n\n' "$notes"
        install="$REPOSITORY"
        ;;
    prerelease)
        printf 'Pre-release built from `develop`. Not intended for production use.\n\n'
        printf '## Changes\n\n'
        previous="$(git describe --tags --abbrev=0 --match 'v*-develop.*' HEAD^ 2>/dev/null || true)"
        git log --no-merges --format='- %s' ${previous:+"$previous..HEAD"}
        printf '\n'
        install="$REPOSITORY/tree/develop"
        ;;
    *)
        echo "Unknown channel: $CHANNEL" >&2
        exit 1
        ;;
esac

printf 'Install in Lidarr under **System > Plugins** with `%s`\n\n' "$install"
printf 'Minimum Lidarr Version: %s\n' "$LIDARR_VERSION"
