#!/usr/bin/env bash
# Prints the pre-release version for HEAD, for example 0.2.0-develop.3.
#
# Lidarr only compares the X.Y.Z part of pre-releases, so every pre-release gets a higher one than
# the last. The size of the step comes from the conventional commits since the previous pre-release.
set -euo pipefail

existing="$(git tag --points-at HEAD --list 'v*-develop.*' | head -n 1)"
if [ -n "$existing" ]; then
    echo "${existing#v}"
    exit 0
fi

tags="$(git tag --list 'v*' | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+(-develop\.[0-9]+)?$' || true)"
if [ -z "$tags" ]; then
    echo "0.1.0-develop.1"
    exit 0
fi

base="$(printf '%s\n' "$tags" | sed -E 's/^v//; s/-.*//' | sort -t. -k1,1n -k2,2n -k3,3n | tail -n 1)"
last_build="$(printf '%s\n' "$tags" | sed -nE 's/.*-develop\.([0-9]+)$/\1/p' | sort -n | tail -n 1)"

if [ -n "$last_build" ]; then
    previous="$(printf '%s\n' "$tags" | grep -E -- "-develop\.${last_build}\$" | head -n 1)"
else
    previous="v$base"
fi

subjects="$(git log --format=%s "$previous..HEAD")"
bodies="$(git log --format=%b "$previous..HEAD")"

IFS=. read -r major minor patch <<<"$base"

if grep -qE '^[a-z]+(\([^)]*\))?!:' <<<"$subjects" || grep -qE '^BREAKING[ -]CHANGE:' <<<"$bodies"; then
    # Before 1.0.0 a breaking change moves the minor version.
    if [ "$major" -eq 0 ]; then
        minor=$((minor + 1)); patch=0
    else
        major=$((major + 1)); minor=0; patch=0
    fi
elif grep -qE '^feat(ure)?(\([^)]*\))?:' <<<"$subjects"; then
    minor=$((minor + 1)); patch=0
else
    patch=$((patch + 1))
fi

echo "$major.$minor.$patch-develop.$((${last_build:-0} + 1))"
