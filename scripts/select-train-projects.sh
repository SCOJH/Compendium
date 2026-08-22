#!/usr/bin/env bash
# Print the projects that belong to one release train.
#
# The release job used to pack every Compendium.* project under src/ on every
# tag, which is why a fix in one package shipped as a release of all of them.
# It now packs one train. Given the tag that triggered the run, this prints the
# .csproj files whose <MinVerTagPrefix> matches that tag's train.
#
# Usage:
#   scripts/select-train-projects.sh <tag-or-prefix> [src-directory]
#
#   scripts/select-train-projects.sh geo-v1.0.6-preview.1   -> the Geo project
#   scripts/select-train-projects.sh core-v                 -> the core train
#
# Exit status: 0 when the train has at least one project, 1 when it has none
# (an unknown train is a typo in a tag, not an empty release).

set -uo pipefail

tag=${1:-}
src_dir=${2:-src}

[[ -n $tag ]] || { echo "usage: $0 <tag-or-prefix> [src-directory]" >&2; exit 2; }
[[ -d $src_dir ]] || { echo "select-train-projects: no such directory: $src_dir" >&2; exit 2; }

# A tag is "<prefix>-v<version>"; a bare prefix already ends in "-v". Strip the
# version off the former and take the latter as-is.
if [[ $tag =~ ^(.*-v)[0-9] ]]; then
  prefix=${BASH_REMATCH[1]}
else
  prefix=$tag
fi

matches=()
while IFS= read -r csproj; do
  name=$(basename "$csproj" .csproj)
  [[ $name == Compendium.* && $name != *.Tests ]] || continue
  declared=$(sed -n 's/.*<MinVerTagPrefix>\(.*\)<\/MinVerTagPrefix>.*/\1/p' "$csproj" | head -1)
  [[ $declared == "$prefix" ]] && matches+=("$csproj")
done < <(find "$src_dir" -name '*.csproj' | sort)

if [[ ${#matches[@]} -eq 0 ]]; then
  echo "select-train-projects: no project declares the train '$prefix' (from tag '$tag')." >&2
  echo "Known trains:" >&2
  find "$src_dir" -name '*.csproj' -exec sed -n 's/.*<MinVerTagPrefix>\(.*\)<\/MinVerTagPrefix>.*/  \1/p' {} + | sort -u >&2
  exit 1
fi

printf '%s\n' "${matches[@]}"
