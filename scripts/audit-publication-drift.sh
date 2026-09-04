#!/usr/bin/env bash
# Ask, from outside any release run, whether each repository actually shipped
# what its last tag promised.
#
# verify-package-published.sh guards a release while it happens. It cannot see
# a repository that simply stops releasing, or one whose release failed months
# ago and was never looked at again. That blind spot is what cost five weeks
# here: six repositories cut releases on 2026-07-25, every run went green, and
# nothing on nuget.org moved. Nobody was watching, because everything was green.
#
# So this asks the question from the outside, with no knowledge of any run:
# for each repository, take the version of its latest published release and
# look for it on the feed. If it is nowhere, that repository has drifted.
#
# Where it belongs: in a scheduled workflow of this repository, run daily. It
# needs no credential beyond a token that can read releases and trees.
#
# Usage:
#   scripts/audit-publication-drift.sh [<owner>/<repo> ...]
#
# With no argument it audits the seven Compendium repositories.
#
# Environment:
#   NUGET_FLATCONTAINER_BASE  flat-container base URL.
#                             Defaults to https://api.nuget.org/v3-flatcontainer
#
# Requires the gh CLI, authenticated with read access to the repositories.
#
# Exit status: 0 when every repository is in sync, 1 when any has drifted.

set -uo pipefail

FLATCONTAINER_BASE=${NUGET_FLATCONTAINER_BASE:-https://api.nuget.org/v3-flatcontainer}

DEFAULT_REPOSITORIES=(
  SCOJH/Compendium
  SCOJH/compendium-ai
  SCOJH/compendium-eventsourcing
  SCOJH/compendium-geo
  SCOJH/compendium-messaging
  SCOJH/compendium-storage
  SCOJH/compendium-vectorstore
)

die() {
  echo "audit-publication-drift: $*" >&2
  exit 2
}

for tool in gh curl python3; do
  command -v "$tool" >/dev/null 2>&1 || die "'$tool' is required but not installed"
done

repositories=("$@")
[[ ${#repositories[@]} -gt 0 ]] || repositories=("${DEFAULT_REPOSITORIES[@]}")

# The version a tag promised. Tags come in two shapes here: "v1.1.0-preview.3"
# in the domain repositories, and "core-v1.0.6" in the framework repository
# since release trains landed. Both carry the version after the "v".
version_of_tag() {
  local tag=$1
  [[ $tag =~ ^(.*-)?v(.+)$ ]] || return 1
  printf '%s' "${BASH_REMATCH[2]}"
}

# The package ids a repository is expected to publish, read from its own source
# tree rather than from a table kept here: a table would be one more thing to
# forget to update, which is the failure mode this whole ticket is about.
package_ids_of() {
  local repo=$1
  gh api "repos/$repo/git/trees/HEAD?recursive=1" \
    --jq '.tree[] | select(.path | test("^src/.*\\.csproj$")) | .path | split("/") | last | sub("\\.csproj$"; "")' \
    2>/dev/null | grep -v '\.Tests$' | sort -u
}

# Print every version of an id on the feed, one per line. Exit 1 when the id is
# unknown, 2 when the feed did not answer in a way we can read.
feed_versions_of() {
  local id_lower=$1 response status body
  response=$(curl -sS --max-time 30 --retry 3 --retry-delay 2 \
    -w '\n%{http_code}' "$FLATCONTAINER_BASE/$id_lower/index.json" 2>/dev/null)
  status=$(tail -n1 <<<"$response")
  body=$(sed '$d' <<<"$response")

  case $status in
    404) return 1 ;;
    200)
      # utf-8-sig: the feed's own error bodies carry a BOM, and a BOM must never
      # read as "no such version".
      python3 -c '
import json, sys
try:
    versions = json.loads(sys.stdin.buffer.read().decode("utf-8-sig")).get("versions", [])
except (UnicodeDecodeError, json.JSONDecodeError):
    sys.exit(2)
for version in versions:
    print(str(version).lower())
' <<<"$body"
      ;;
    *) return 2 ;;
  esac
}

# English plural of "repository", so the report reads like a sentence.
plural() { [[ $1 -eq 1 ]] && printf 'repository' || printf 'repositories'; }

echo "Auditing ${#repositories[@]} $(plural ${#repositories[@]}) against $FLATCONTAINER_BASE"
echo

drifted=()
in_sync=0

for repo in "${repositories[@]}"; do
  echo "$repo"

  tag=$(gh api "repos/$repo/releases?per_page=100" \
    --jq 'map(select(.draft == false)) | .[0].tag_name' 2>/dev/null)
  if [[ -z $tag || $tag == "null" ]]; then
    echo "  no published release — nothing was promised, nothing to check"
    in_sync=$((in_sync + 1))
    echo
    continue
  fi

  if ! version=$(version_of_tag "$tag"); then
    echo "  DRIFT  latest release is tagged '$tag', which carries no version after a 'v'"
    drifted+=("$repo")
    echo
    continue
  fi
  echo "  latest release: $tag  →  version $version"

  mapfile -t ids < <(package_ids_of "$repo")
  if [[ ${#ids[@]} -eq 0 ]]; then
    # An empty perimeter would make every repository look healthy. Not knowing
    # what a repository ships is exactly the state this audit exists to catch.
    echo "  DRIFT  no packable project found under src/ — cannot tell what this release should have shipped"
    drifted+=("$repo")
    echo
    continue
  fi

  # One request per id, whatever the outcome: the report below reads what this
  # loop recorded rather than asking the feed a second time.
  found=""
  states=()
  version_lower=${version,,}
  for id in "${ids[@]}"; do
    feed_output=$(feed_versions_of "${id,,}")
    case $? in
      0)
        if grep -qxF "$version_lower" <<<"$feed_output"; then
          found=$id
          break
        fi
        states+=("$id — latest online: $(tail -n1 <<<"$feed_output")")
        ;;
      1) states+=("$id — never published on this feed") ;;
      *) states+=("$id — the feed did not answer readably; not knowing is not a pass") ;;
    esac
  done

  if [[ -n $found ]]; then
    echo "  ok     $found $version is online"
    in_sync=$((in_sync + 1))
  else
    echo "  DRIFT  none of the ${#ids[@]} package(s) of this repository carry $version on the feed"
    printf '           %s\n' "${states[@]}"
    drifted+=("$repo")
  fi
  echo
done

if [[ ${#drifted[@]} -gt 0 ]]; then
  echo "${#drifted[@]} $(plural ${#drifted[@]}) released a tag that never reached the feed:" >&2
  printf '  %s\n' "${drifted[@]}" >&2
  echo >&2
  echo "A release that publishes nothing must not be able to look healthy. Either the" >&2
  echo "credential is missing on those repositories, or the push silently did nothing." >&2
  exit 1
fi

echo "Every audited repository has its latest release on the feed ($in_sync checked)."
