#!/usr/bin/env bash
# Verify that every .nupkg just pushed really appeared on the feed.
#
# The mirror image of verify-package-not-published.sh, and the answer to the
# other half of the same question. That script runs before the push and asks
# "is this version free?"; this one runs after and asks "did the push actually
# publish it?".
#
# The distinction is not academic. `dotnet nuget push` exits 0 in cases where
# nothing was published: a credential that resolved to an empty string and was
# skipped, a `--skip-duplicate` swallowing a 409, a package accepted for
# validation and then rejected by the feed. Six SCOJH repositories shipped green
# releases for five weeks without a single package reaching nuget.org, because
# the run only ever proved that the push command had returned.
#
# Presence on the flat container is the first observation that a consumer could
# also make. Until it holds, the release has not happened.
#
# Usage:
#   scripts/verify-package-published.sh <artifacts-dir|package.nupkg> [...]
#
# Environment:
#   NUGET_FLATCONTAINER_BASE        flat-container base URL.
#                                   Defaults to https://api.nuget.org/v3-flatcontainer
#   NUGET_PUBLISH_TIMEOUT_SECONDS   how long to wait for every package to show
#                                   up. Defaults to 600.
#   NUGET_PUBLISH_POLL_SECONDS      delay between two rounds. Defaults to 15.
#
# Exit status: 0 when every version is online, 1 when any is still missing when
# the budget runs out.

set -uo pipefail

FLATCONTAINER_BASE=${NUGET_FLATCONTAINER_BASE:-https://api.nuget.org/v3-flatcontainer}
TIMEOUT_SECONDS=${NUGET_PUBLISH_TIMEOUT_SECONDS:-600}
POLL_SECONDS=${NUGET_PUBLISH_POLL_SECONDS:-15}

die() {
  echo "verify-package-published: $*" >&2
  exit 2
}

for tool in unzip curl python3; do
  command -v "$tool" >/dev/null 2>&1 || die "'$tool' is required but not installed"
done

[[ $TIMEOUT_SECONDS =~ ^[0-9]+$ ]] || die "NUGET_PUBLISH_TIMEOUT_SECONDS must be a whole number of seconds, got '$TIMEOUT_SECONDS'"
[[ $POLL_SECONDS =~ ^[0-9]+$ ]] || die "NUGET_PUBLISH_POLL_SECONDS must be a whole number of seconds, got '$POLL_SECONDS'"
[[ $POLL_SECONDS -gt 0 ]] || die "NUGET_PUBLISH_POLL_SECONDS must be greater than zero"

[[ $# -gt 0 ]] || die "usage: $0 <artifacts-dir|package.nupkg> [...]"

packages=()
for target in "$@"; do
  if [[ -d $target ]]; then
    while IFS= read -r nupkg; do
      packages+=("$nupkg")
    done < <(find "$target" -maxdepth 1 -name '*.nupkg' ! -name '*.symbols.nupkg' | sort)
  elif [[ -f $target ]]; then
    packages+=("$target")
  else
    die "no such file or directory: $target"
  fi
done

[[ ${#packages[@]} -gt 0 ]] || die "no .nupkg found in: $*"

# Read id and version from the nuspec rather than from the file name: the file
# name is only a convention, the nuspec is what the feed indexed.
read_id_and_version() {
  local nupkg=$1 nuspec_entry
  nuspec_entry=$(unzip -Z1 "$nupkg" '*.nuspec' 2>/dev/null | head -1)
  [[ -n $nuspec_entry ]] || return 1
  unzip -p "$nupkg" "$nuspec_entry" 2>/dev/null | python3 -c '
import sys, xml.etree.ElementTree as ET
try:
    root = ET.fromstring(sys.stdin.buffer.read())
except ET.ParseError as exc:
    sys.stderr.write("unreadable nuspec: %s\n" % exc)
    sys.exit(1)
found = {}
for element in root.iter():
    name = element.tag.rsplit("}", 1)[-1]
    if name in ("id", "version") and name not in found and (element.text or "").strip():
        found[name] = element.text.strip()
if "id" not in found or "version" not in found:
    sys.stderr.write("nuspec has no id or no version\n")
    sys.exit(1)
print(found["id"])
print(found["version"])
'
}

# Answer for one id/version: 0 online, 1 not yet, 2 the feed did not say.
# The last status seen is left in $probe_detail so the failure can name it.
probe_detail=""
probe_version_online() {
  local id_lower=$1 version_lower=$2 response status body

  response=$(curl -sS --max-time 30 --retry 3 --retry-delay 2 \
    -w '\n%{http_code}' "$FLATCONTAINER_BASE/$id_lower/index.json" 2>/dev/null)
  status=$(tail -n1 <<<"$response")
  body=$(sed '$d' <<<"$response")

  case $status in
    404)
      # The id itself is unknown. Either the push published nothing, or this is
      # the very first version of a new id and the feed has not indexed it yet.
      probe_detail="the feed does not know this package id yet (HTTP 404)"
      return 1
      ;;
    200)
      # utf-8-sig, not utf-8: the flat container is served from blob storage and
      # its error bodies carry a UTF-8 BOM. A 200 has none today, but decoding
      # both costs nothing and a BOM must never read as "not published".
      python3 -c '
import json, sys
wanted = sys.argv[1]
try:
    versions = json.loads(sys.stdin.buffer.read().decode("utf-8-sig")).get("versions", [])
except (UnicodeDecodeError, json.JSONDecodeError):
    sys.exit(2)
sys.exit(0 if wanted in {str(v).lower() for v in versions} else 1)
' "$version_lower" <<<"$body"
      case $? in
        0) probe_detail="online"; return 0 ;;
        1) probe_detail="the id is on the feed but this version is not in its index"; return 1 ;;
        *) probe_detail="the feed returned an index that is not valid JSON"; return 2 ;;
      esac
      ;;
    *)
      probe_detail="the feed answered HTTP $status"
      return 2
      ;;
  esac
}

ids=()
versions=()
for nupkg in "${packages[@]}"; do
  if ! metadata=$(read_id_and_version "$nupkg"); then
    echo "verify-package-published: could not read id/version from $(basename "$nupkg")" >&2
    exit 2
  fi
  ids+=("$(sed -n 1p <<<"$metadata")")
  versions+=("$(sed -n 2p <<<"$metadata")")
done

echo "Waiting for ${#packages[@]} package(s) to appear on $FLATCONTAINER_BASE"
echo "Budget ${TIMEOUT_SECONDS}s, one round every ${POLL_SECONDS}s."
echo

pending=()
for index in "${!ids[@]}"; do
  pending+=("$index")
done

started=$SECONDS
last_detail=()
while true; do
  still_pending=()
  for index in "${pending[@]}"; do
    # The flat container indexes ids and versions lowercased.
    if probe_version_online "${ids[index],,}" "${versions[index],,}"; then
      echo "ok    ${ids[index]} ${versions[index]} — online after $((SECONDS - started))s"
    else
      last_detail[index]=$probe_detail
      still_pending+=("$index")
    fi
  done
  pending=("${still_pending[@]}")

  [[ ${#pending[@]} -eq 0 ]] && break

  elapsed=$((SECONDS - started))
  # Stop before sleeping past the budget: one more round could only report the
  # same thing later.
  if [[ $((elapsed + POLL_SECONDS)) -gt $TIMEOUT_SECONDS ]]; then
    break
  fi
  echo "…    ${#pending[@]} package(s) not visible yet at ${elapsed}s, retrying in ${POLL_SECONDS}s"
  sleep "$POLL_SECONDS"
done

elapsed=$((SECONDS - started))
echo
if [[ ${#pending[@]} -gt 0 ]]; then
  for index in "${pending[@]}"; do
    echo "FAIL  ${ids[index]} ${versions[index]}" >&2
    echo "      still not on $FLATCONTAINER_BASE after ${elapsed}s — ${last_detail[index]}" >&2
  done
  echo >&2
  echo "${#pending[@]} package(s) never appeared within ${TIMEOUT_SECONDS}s. The push reported success" >&2
  echo "but the feed has nothing to show for it: this release did not publish." >&2
  echo "If the packages do turn up later, raise NUGET_PUBLISH_TIMEOUT_SECONDS — the" >&2
  echo "first version of a brand new package id waits on feed-side validation." >&2
  exit 1
fi

echo "All ${#packages[@]} package(s) are online after ${elapsed}s."
