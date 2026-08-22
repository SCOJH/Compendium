#!/usr/bin/env bash
# Verify that every packable project under src/ declares a release train.
#
# Versions used to come from one global MinVerTagPrefix, so all 35 packages
# moved together and the version number said nothing about what had changed.
# Each project now declares its own <MinVerTagPrefix>. A project that forgets
# to would not fail to build — MinVer would quietly hand it 0.0.0-alpha.0.N and
# the release job would publish that. This script is the thing that says no.
#
# Usage:
#   scripts/verify-package-trains.sh [src-directory]   (default: src)
#
# Exit status: 0 when every packable project declares a train, 1 otherwise —
# including when it finds no project at all. A gate that passes on an empty scan
# is not a gate: it reports "all clear" precisely when it has checked nothing.
#
# Deliberately bash-3.2 compatible (no `declare -A`): macOS ships bash 3.2, and
# a gate a developer cannot run on their own machine only ever gets run where it
# is too late to be useful.

set -uo pipefail

src_dir=${1:-src}

[[ -d $src_dir ]] || { echo "verify-package-trains: no such directory: $src_dir" >&2; exit 2; }

missing=()
# "prefix<TAB>name" per line — a plain array, so this runs on bash 3.2 too.
found=()

while IFS= read -r csproj; do
  name=$(basename "$csproj" .csproj)
  [[ $name == Compendium.* && $name != *.Tests ]] || continue
  # An explicit opt-out is not a missing train.
  if grep -qi '<IsPackable>[[:space:]]*false[[:space:]]*</IsPackable>' "$csproj"; then
    continue
  fi
  prefix=$(sed -n 's/.*<MinVerTagPrefix>\(.*\)<\/MinVerTagPrefix>.*/\1/p' "$csproj" | head -1)
  if [[ -z $prefix ]]; then
    missing+=("$csproj")
  else
    found+=("$prefix	$name")
  fi
done < <(find "$src_dir" -name '*.csproj' | sort)

if [[ ${#missing[@]} -gt 0 ]]; then
  echo "These packable projects declare no <MinVerTagPrefix>, so MinVer would" >&2
  echo "version them 0.0.0-alpha and the release job would publish that:" >&2
  printf '  %s\n' "${missing[@]}" >&2
  echo >&2
  echo "Add a <MinVerTagPrefix> next to <PackageId>. Use an existing train when" >&2
  echo "the package only ever ships with it, a new one otherwise." >&2
  exit 1
fi

# Zero is a failure, not a clean bill of health. It means the scan found nothing
# to check — a wrong directory, a `find` that returned nothing, a repository
# layout that moved — and reporting success there is how a broken gate stays
# invisible for months.
if [[ ${#found[@]} -eq 0 ]]; then
  echo "verify-package-trains: found no packable Compendium.* project under '$src_dir'." >&2
  echo "This is reported as a failure on purpose: the scan checked nothing, so it" >&2
  echo "cannot say anything is correct. Check the directory, or the layout." >&2
  exit 1
fi

echo "${#found[@]} packable project(s), all on a declared train:"
printf '%s\n' "${found[@]}" | sort | while IFS='	' read -r prefix name; do
  printf '  %-28s %s\n' "$prefix" "$name"
done
