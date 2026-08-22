#!/usr/bin/env bash
# Verify that every .nupkg was packed from a commit that is reachable from a tag.
#
# A published package is only reproducible if the commit recorded in its nuspec
# still exists in the history and belongs to a release. Two packages have already
# been published from a commit that lives on no tag and on no release ancestry:
# nobody can rebuild what was shipped. This gate makes that unpublishable.
#
# Usage:
#   scripts/verify-package-provenance.sh <artifacts-dir|package.nupkg> [...]
#
# Environment:
#   EXPECTED_COMMIT   commit every package must have been packed from.
#                     Defaults to $GITHUB_SHA. Unset on both -> check skipped.
#
# Exit status: 0 when every package passes, 1 when any package fails.

set -uo pipefail

fail_count=0
pass_count=0

die() {
  echo "verify-package-provenance: $*" >&2
  exit 2
}

reject() {
  local package=$1 reason=$2
  echo "FAIL  $(basename "$package")"
  echo "      $reason"
  fail_count=$((fail_count + 1))
}

for tool in unzip git python3; do
  command -v "$tool" >/dev/null 2>&1 || die "'$tool' is required but not installed"
done

[[ $# -gt 0 ]] || die "usage: $0 <artifacts-dir|package.nupkg> [...]"

git rev-parse --git-dir >/dev/null 2>&1 || die "not inside a git repository"

if [[ "$(git rev-parse --is-shallow-repository)" == "true" ]]; then
  die "shallow clone: 'git tag --contains' cannot be trusted here. Check out with fetch-depth: 0."
fi

expected_commit=${EXPECTED_COMMIT:-${GITHUB_SHA:-}}

# Collect the packages to inspect.
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

# Read repository/@commit out of the nuspec embedded in a .nupkg.
# Printed empty when the attribute is absent, so the caller can reject it.
read_repository_commit() {
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
# The nuspec namespace varies with the schema version, so match on local name.
for element in root.iter():
    if element.tag.rsplit("}", 1)[-1] == "repository":
        print(element.attrib.get("commit", "").strip())
        break
'
}

echo "Checking provenance of ${#packages[@]} package(s)"
[[ -n $expected_commit ]] && echo "Expected commit: $expected_commit"
echo

for nupkg in "${packages[@]}"; do
  commit=$(read_repository_commit "$nupkg")
  if [[ $? -ne 0 ]]; then
    reject "$nupkg" "no readable .nuspec inside the package"
    continue
  fi

  # 1. The attribute must be there. A SourceLink regression would otherwise
  #    disarm this whole gate without a single red build.
  if [[ -z $commit ]]; then
    reject "$nupkg" "nuspec carries no <repository commit=\"...\"> — SourceLink is off, provenance cannot be established"
    continue
  fi

  # 2. It must be the tree we think we packed. Catches a pack from a stale
  #    working copy, which the tag check alone would happily accept.
  if [[ -n $expected_commit && $commit != "$expected_commit" ]]; then
    reject "$nupkg" "packed from $commit but this run is building $expected_commit"
    continue
  fi

  # 3. The commit has to exist here at all before we can ask about tags.
  if ! git cat-file -e "${commit}^{commit}" 2>/dev/null; then
    reject "$nupkg" "commit $commit is unknown to this repository — it was packed from a tree that was never pushed"
    continue
  fi

  # 4. The literal ticket criterion: reachable from at least one tag.
  tags=$(git tag --contains "$commit" 2>/dev/null)
  if [[ -z $tags ]]; then
    reject "$nupkg" "commit $commit is on no tag — publishing it would ship a build nobody can reconstruct"
    continue
  fi

  echo "ok    $(basename "$nupkg")"
  echo "      $commit — $(echo "$tags" | tr '\n' ' ' | sed 's/ $//')"
  pass_count=$((pass_count + 1))
done

echo
if [[ $fail_count -gt 0 ]]; then
  echo "$fail_count package(s) failed provenance, $pass_count passed. Nothing is published." >&2
  exit 1
fi

echo "All $pass_count package(s) trace back to a tagged commit."
