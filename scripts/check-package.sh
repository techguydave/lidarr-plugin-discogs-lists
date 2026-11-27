#!/usr/bin/env bash
# Confirms the release zip holds the plugin and nothing that Lidarr already provides.
set -euo pipefail

cd "$(dirname "$0")/.."

actual="$(unzip -Z1 artifacts/Lidarr.Plugin.DiscogsLists.net8.0.zip | LC_ALL=C sort)"
expected="Lidarr.Plugin.DiscogsLists.deps.json
Lidarr.Plugin.DiscogsLists.dll
Lidarr.Plugin.DiscogsLists.pdb"

if [ "$actual" != "$expected" ]; then
    printf 'Unexpected package contents:\n%s\n' "$actual" >&2
    exit 1
fi

echo "Package check passed."
