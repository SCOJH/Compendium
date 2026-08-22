#!/usr/bin/env bash
# Verify that no .nupkg about to be pushed reuses a version already on the feed.
#
# This replaces `dotnet nuget push --skip-duplicate`. That flag turned a
# republication into a silent no-op: the feed kept the old bits, the run stayed
# green, and the only trace of the divergence was a consumer restoring content
# that did not match the tag. With one train per package a fresh tag always
# yields a fresh version, so a collision means a tag was moved — which is
# exactly the kind of thing that should stop a release, loudly.
#
# Usage:
#   scripts/verify-package-not-published.sh <artifacts-dir|package.nupkg> [...]
#
# Environment:
#   NUGET_FLATCONTAINER_BASE  flat-container base URL.
#                             Defaults to https://api.nuget.org/v3-flatcontainer
#
# Exit status: 0 when every version is new, 1 when any is already published.

set -uo pipefail

FLATCONTAINER_BASE=${NUGET_FLATCONTAINER_BASE:-https://api.nuget.org/v3-flatcontainer}

fail_count=0
pass_count=0

die() {
  echo "verify-package-not-published: $*" >&2
  exit 2
}

for tool in unzip curl python3; do
  command -v "$tool" >/dev/null 2>&1 || die "'$tool' is required but not installed"
done

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
# name is only a convention, the nuspec is what the feed will index.
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

echo "Checking ${#packages[@]} package(s) against $FLATCONTAINER_BASE"
echo

for nupkg in "${packages[@]}"; do
  if ! metadata=$(read_id_and_version "$nupkg"); then
    echo "FAIL  $(basename "$nupkg")"
    echo "      could not read id/version from the package"
    fail_count=$((fail_count + 1))
    continue
  fi
  package_id=$(sed -n 1p <<<"$metadata")
  package_version=$(sed -n 2p <<<"$metadata")

  # The flat container indexes ids and versions lowercased.
  id_lower=${package_id,,}
  version_lower=${package_version,,}

  response=$(curl -sS --max-time 30 --retry 3 --retry-delay 2 \
    -w '\n%{http_code}' "$FLATCONTAINER_BASE/$id_lower/index.json" 2>/dev/null)
  status=$(tail -n1 <<<"$response")
  body=$(sed '$d' <<<"$response")

  case $status in
    404)
      # Unknown package id: nothing can collide, this is a first publication.
      echo "ok    $package_id $package_version — new package on this feed"
      pass_count=$((pass_count + 1))
      ;;
    200)
      python3 -c '
import json, sys
wanted = sys.argv[1]
try:
    versions = json.load(sys.stdin).get("versions", [])
except json.JSONDecodeError:
    sys.exit(2)
sys.exit(0 if wanted in {str(v).lower() for v in versions} else 1)
' "$version_lower" <<<"$body"
      case $? in
        0)
          echo "FAIL  $package_id $package_version"
          echo "      $package_id $package_version is already published on this feed."
          echo "      Republishing a version with different content is not allowed —"
          echo "      a moved tag is the usual cause. Tag a new version instead."
          fail_count=$((fail_count + 1))
          ;;
        1)
          echo "ok    $package_id $package_version — version is free"
          pass_count=$((pass_count + 1))
          ;;
        *)
          echo "FAIL  $package_id $package_version"
          echo "      the feed returned an index that is not valid JSON — cannot prove the version is free"
          fail_count=$((fail_count + 1))
          ;;
      esac
      ;;
    *)
      # Anything else means we do not know. Not knowing is not permission.
      echo "FAIL  $package_id $package_version"
      echo "      feed answered HTTP $status — cannot prove the version is free, refusing to push"
      fail_count=$((fail_count + 1))
      ;;
  esac
done

echo
if [[ $fail_count -gt 0 ]]; then
  echo "$fail_count package(s) did not clear the republication check, $pass_count are new. Nothing is published." >&2
  exit 1
fi

echo "All $pass_count package(s) carry a version this feed has never seen."
