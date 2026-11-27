#!/usr/bin/env bash
# Fails if the plugin could send anything but a GET request. All Discogs access must stay read-only.
set -euo pipefail

cd "$(dirname "$0")/.."

SOURCE="${1:-src/Lidarr.Plugin.DiscogsLists}"
TRANSPORT="DiscogsTransport.cs"
failed=0

report() {
    if [ -n "$2" ]; then
        printf 'Read-only check failed: %s\n%s\n\n' "$1" "$2" >&2
        failed=1
    fi
}

search() {
    grep -rnE --include='*.cs' --exclude-dir=bin --exclude-dir=obj "$@" "$SOURCE" || true
}

report "non-GET HTTP method" \
    "$(search 'HttpMethod\.(Post|Put|Patch|Delete|Head|Options|Trace|Connect|Parse)|new +HttpMethod|\.Method *=[^=]')"

report "write-style HTTP call" \
    "$(search '\.(Post|Put|Patch|Delete)(AsJson)?(Async)?(<[^>]*>)? *\(')"

report "network access outside $TRANSPORT" \
    "$(search --exclude="$TRANSPORT" 'HttpClient|HttpRequestMessage|HttpRequestBuilder|HttpRequest\(|WebRequest|WebClient|HttpMessageInvoker|Socket|\.Send(Async)? *\(')"

requests="$(search 'new +HttpRequestMessage')"
if [ "$(printf '%s' "$requests" | grep -c .)" != "1" ] || ! printf '%s' "$requests" | grep -q 'new HttpRequestMessage(HttpMethod\.Get,'; then
    report "expected exactly one request, constructed with HttpMethod.Get" "${requests:-none found}"
fi

[ "$failed" -eq 0 ] && echo "Read-only check passed."
exit "$failed"
