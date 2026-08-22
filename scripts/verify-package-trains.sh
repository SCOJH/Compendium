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
# Exit status: 0 when every packable project declares a train, 1 otherwise.

set -uo pipefail

src_dir=${1:-src}

[[ -d $src_dir ]] || { echo "verify-package-trains: no such directory: $src_dir" >&2; exit 2; }

missing=()
declare -A train_of=()

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
    train_of[$name]=$prefix
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

echo "${#train_of[@]} packable project(s), all on a declared train:"
for name in $(printf '%s\n' "${!train_of[@]}" | sort); do
  printf '  %-28s %s\n' "${train_of[$name]}" "$name"
done | sort
