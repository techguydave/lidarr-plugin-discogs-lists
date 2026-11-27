#!/usr/bin/env bash
# Fails if the plugin could send anything but a GET request. All Discogs access must stay read-only.
set -euo pipefail

cd "$(dirname "$0")/.."

SOURCE="${1:-src/Lidarr.Plugin.DiscogsLists}"
# The only files allowed to use the network. Each may build exactly one request, and it must be a GET.
TRANSPORTS=("DiscogsTransport.cs" "MusicBrainzTransport.cs")
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

excludes=()
for transport in "${TRANSPORTS[@]}"; do excludes+=(--exclude="$transport"); done

report "network access outside ${TRANSPORTS[*]}" \
    "$(search "${excludes[@]}" 'HttpClient|HttpRequestMessage|HttpRequestBuilder|HttpRequest\(|WebRequest|WebClient|HttpMessageInvoker|Socket|\.Send(Async)? *\(')"

requests="$(search 'new +HttpRequestMessage')"
for transport in "${TRANSPORTS[@]}"; do
    mine="$(printf '%s\n' "$requests" | grep "/$transport:" || true)"
    if [ "$(printf '%s' "$mine" | grep -c .)" != "1" ] || ! printf '%s' "$mine" | grep -q 'new HttpRequestMessage(HttpMethod\.Get,'; then
        report "expected exactly one request in $transport, constructed with HttpMethod.Get" "${mine:-none found}"
    fi
done
others="$(printf '%s\n' "$requests" | grep -v -e "/DiscogsTransport.cs:" -e "/MusicBrainzTransport.cs:" | grep . || true)"
report "request built outside the transports" "$others"

[ "$failed" -eq 0 ] && echo "Read-only check passed."
exit "$failed"
