#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

dotnet test src/Lidarr.Plugin.DiscogsLists.Test/Lidarr.Plugin.DiscogsLists.Test.csproj -c Release \
    -p:AssemblyVersion="$(tr -d '[:space:]' < lidarr.version)" \
    -p:EnableAnalyzers=false \
    -p:NuGetAudit=false \
    "$@"
