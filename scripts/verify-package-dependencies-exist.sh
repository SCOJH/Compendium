#!/usr/bin/env bash
# Verify that every Compendium dependency of a packed .nupkg can actually be
# resolved from the feed BEFORE the package is pushed.
#
# WHY THIS EXISTS — measured, on 2026-09-29.
#
# `testing-v1.0.5-preview.6` was tagged ALONE, on a commit eleven commits past
# `core-v1.0.5-preview.5`. MinVer therefore stamped the sibling dependencies of
# Compendium.Testing as `1.0.5-preview.5.11` — a version that was never
# published, and which sorts ABOVE `1.0.5-preview.5` (a longer run of prerelease
# identifiers with the same prefix wins), so the `>=` it expresses cannot be met
# by anything on the feed. The release went GREEN and produced a package nobody
# can install:
#
#     error NU1102: Unable to find package Compendium.Abstractions
#
# nuget.org does not allow deletion. The bad version is still there.
#
# None of the three existing gates can see this: verify-package-trains.sh checks
# that every packable project declares a train, verify-package-provenance.sh that
# the bits come from a tagged commit, verify-package-not-published.sh that a
# version is not being reused. All three look at the package. None looks at what
# the package DEPENDS ON.
#
# The rule this encodes: a downstream train must be tagged together with its
# upstream train, on the same commit. That constraint was already written — in the
# `$comment` of Nexus's compendium-floor-gaps.json, dated 2026-09-05 — but only in
# prose, where it could be, and was, missed.
#
# WHAT IT DOES NOT DO. It only judges `Compendium.*` dependencies: those are the
# ones this repository's own tagging can get wrong. Third-party dependencies come
# from versions the build already restored, so their existence is proven by the
# build itself.
#
# Usage:
#   scripts/verify-package-dependencies-exist.sh <artifacts-dir|package.nupkg> [...]
#   scripts/verify-package-dependencies-exist.sh --self-test
#
# Environment:
#   NUGET_FLATCONTAINER_BASE  flat-container base URL.
#                             Defaults to https://api.nuget.org/v3-flatcontainer
#
# Exit status: 0 when every Compendium dependency is resolvable, 1 when any is
# not, 2 on a usage or environment problem.

set -uo pipefail

FLATCONTAINER_BASE=${NUGET_FLATCONTAINER_BASE:-https://api.nuget.org/v3-flatcontainer}
SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
SATISFIES=$SCRIPT_DIR/nuget-version-satisfies.py

die() {
  echo "verify-package-dependencies-exist: $*" >&2
  exit 2
}

for tool in unzip curl python3; do
  command -v "$tool" >/dev/null 2>&1 || die "'$tool' is required but not installed"
done

[[ -f $SATISFIES ]] || die "missing helper: $SATISFIES"

# The self-test covers the part that can be wrong in a subtle way — version
# ordering and range parsing. It needs no network and no package, so CI can run
# it on every push rather than only when a release happens.
if [[ ${1:-} == --self-test ]]; then
  exec python3 "$SATISFIES" --self-test
fi

[[ $# -gt 0 ]] || die "usage: $0 <artifacts-dir|package.nupkg> [...] | --self-test"

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

# Read the nuspec, not the file name: the file name is a convention, the nuspec
# is what the feed indexes and what a consumer resolves against.
read_nuspec() {
  local nupkg=$1 entry
  entry=$(unzip -Z1 "$nupkg" '*.nuspec' 2>/dev/null | head -1)
  [[ -n $entry ]] || return 1
  unzip -p "$nupkg" "$entry" 2>/dev/null
}

# Emits `id<TAB>range` for every Compendium dependency, deduplicated. A target
# framework that repeats a dependency is normal; checking it twice is not.
compendium_dependencies() {
  python3 -c '
import sys, xml.etree.ElementTree as ET
try:
    root = ET.fromstring(sys.stdin.buffer.read())
except ET.ParseError as exc:
    sys.stderr.write("unreadable nuspec: %s\n" % exc)
    sys.exit(1)
seen = set()
for element in root.iter():
    if element.tag.rsplit("}", 1)[-1] != "dependency":
        continue
    identifier = (element.get("id") or "").strip()
    version = (element.get("version") or "").strip()
    if not identifier.startswith("Compendium."):
        continue
    if (identifier, version) in seen:
        continue
    seen.add((identifier, version))
    print("%s\t%s" % (identifier, version))
'
}

package_identity() {
  python3 -c '
import sys, xml.etree.ElementTree as ET
root = ET.fromstring(sys.stdin.buffer.read())
found = {}
for element in root.iter():
    name = element.tag.rsplit("}", 1)[-1]
    if name in ("id", "version") and name not in found and (element.text or "").strip():
        found[name] = element.text.strip()
print("%s %s" % (found.get("id", "?"), found.get("version", "?")))
'
}

fail_count=0
pass_count=0
checked=0

echo "Checking Compendium dependencies of ${#packages[@]} package(s) against $FLATCONTAINER_BASE"
echo

for nupkg in "${packages[@]}"; do
  if ! nuspec=$(read_nuspec "$nupkg"); then
    echo "FAIL  $(basename "$nupkg")"
    echo "      no readable .nuspec inside the package"
    fail_count=$((fail_count + 1))
    continue
  fi

  identity=$(printf '%s' "$nuspec" | package_identity 2>/dev/null) || identity="$(basename "$nupkg")"

  if ! deps=$(printf '%s' "$nuspec" | compendium_dependencies); then
    echo "FAIL  $identity"
    echo "      could not read the dependency list"
    fail_count=$((fail_count + 1))
    continue
  fi

  if [[ -z $deps ]]; then
    echo "ok    $identity — no Compendium dependency to check"
    pass_count=$((pass_count + 1))
    continue
  fi

  package_ok=1
  while IFS=$'\t' read -r dep_id dep_range; do
    [[ -n $dep_id ]] || continue
    checked=$((checked + 1))
    # `${var,,}` would need bash 4; macOS still ships 3.2 and this script has to
    # be runnable where it is written, not only on the runner.
    id_lower=$(printf '%s' "$dep_id" | tr '[:upper:]' '[:lower:]')

    response=$(curl -sS --max-time 30 --retry 3 --retry-delay 2 \
      -w '\n%{http_code}' "$FLATCONTAINER_BASE/$id_lower/index.json" 2>/dev/null)
    status=$(tail -n1 <<<"$response")
    body=$(sed '$d' <<<"$response")

    case $status in
      200) ;;
      404)
        echo "FAIL  $identity"
        echo "      depends on $dep_id $dep_range — that package id does not exist on this feed."
        echo "      A train was never seeded, or the id is misspelled."
        package_ok=0
        continue
        ;;
      *)
        # Not knowing is not permission.
        echo "FAIL  $identity"
        echo "      depends on $dep_id $dep_range — feed answered HTTP $status,"
        echo "      cannot prove the dependency is resolvable. Refusing to push."
        package_ok=0
        continue
        ;;
    esac

    resolved=$(printf '%s' "$body" | python3 "$SATISFIES" "$dep_range")
    case $? in
      0)
        echo "ok    $identity -> $dep_id $dep_range (resolves to $resolved)"
        ;;
      1)
        echo "FAIL  $identity"
        echo "      depends on $dep_id $dep_range — NO published version satisfies it."
        echo "      This is the 2026-09-29 failure: a downstream train tagged without its"
        echo "      upstream train makes MinVer stamp a height-suffixed dependency version"
        echo "      that was never published. Tag the upstream train on THIS SAME COMMIT,"
        echo "      then tag this one again with a fresh version."
        package_ok=0
        ;;
      *)
        echo "FAIL  $identity"
        echo "      depends on $dep_id $dep_range — the range or the feed index could not"
        echo "      be read. Cannot prove the dependency is resolvable. Refusing to push."
        package_ok=0
        ;;
    esac
  done <<<"$deps"

  if [[ $package_ok -eq 1 ]]; then
    pass_count=$((pass_count + 1))
  else
    fail_count=$((fail_count + 1))
  fi
done

echo
if [[ $fail_count -gt 0 ]]; then
  echo "$fail_count package(s) carry a Compendium dependency this feed cannot satisfy," \
       "$pass_count are clear. Nothing is published." >&2
  exit 1
fi

# A gate that evaluated nothing must not report success. Without this, an error
# inside the loop — a shell builtin that is not portable, say — silently yields
# `fail_count=0` and a green run, which is the very shape of failure this file
# exists to prevent. Every package must be accounted for, one way or the other.
accounted=$((pass_count + fail_count))
if [[ $accounted -ne ${#packages[@]} ]]; then
  echo "verify-package-dependencies-exist: ${#packages[@]} package(s) were given but only" \
       "$accounted were judged. The check did not complete; refusing to report success." >&2
  exit 2
fi

echo "All $pass_count package(s) clear: $checked Compendium dependency reference(s) resolvable."
