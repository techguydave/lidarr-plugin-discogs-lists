#!/usr/bin/env bash
# Builds the plugin and writes the installable zip to artifacts/.
# Usage: scripts/build.sh [version] [plugin-branch]
set -euo pipefail

cd "$(dirname "$0")/.."
ROOT="$PWD"

VERSION="${1:-0.0.0-local}"
BRANCH="${2:-}"
FRAMEWORK="net8.0"
PROJECT="src/Lidarr.Plugin.DiscogsLists/Lidarr.Plugin.DiscogsLists.csproj"
OUTPUT="src/Lidarr.Plugin.DiscogsLists/bin/Release/$FRAMEWORK"

# The plugin must be compiled against a Lidarr assembly version no newer than the one users run.
# NuGet audit is off because it fails the build on advisories in Lidarr's own dependencies, none of which ship here.
LIDARR_VERSION="$(tr -d '[:space:]' < lidarr.version)"

rm -rf artifacts "$OUTPUT"
mkdir -p artifacts
dotnet build "$PROJECT" -c Release \
    -p:Version="$VERSION" \
    -p:PluginBranch="$BRANCH" \
    -p:AssemblyVersion="$LIDARR_VERSION" \
    -p:EnableAnalyzers=false \
    -p:NuGetAudit=false

# Lidarr installs the release asset whose name ends in <framework>.zip.
(
    cd "$OUTPUT"
    zip -q "$ROOT/artifacts/Lidarr.Plugin.DiscogsLists.${FRAMEWORK}.zip" \
        Lidarr.Plugin.DiscogsLists.dll \
        Lidarr.Plugin.DiscogsLists.pdb \
        Lidarr.Plugin.DiscogsLists.deps.json
)

echo "Packaged artifacts/Lidarr.Plugin.DiscogsLists.${FRAMEWORK}.zip (version $VERSION)"
