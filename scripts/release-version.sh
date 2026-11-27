#!/usr/bin/env bash
# Prints the stable version for HEAD: the version of the develop pre-release with identical content,
# so that only code that has been published as a pre-release can be released.
set -euo pipefail

tree="$(git rev-parse 'HEAD^{tree}')"

version="$(
    git tag --list 'v*-develop.*' | while read -r tag; do
        if [ "$(git rev-parse "$tag^{tree}")" = "$tree" ]; then
            printf '%s\n' "$tag" | sed -E 's/^v//; s/-.*//'
        fi
    done | sort -t. -k1,1n -k2,2n -k3,3n | tail -n 1
)"

if [ -z "$version" ]; then
    echo "No develop pre-release matches this content. Publish it from develop first." >&2
    exit 1
fi

echo "$version"
