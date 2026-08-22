#!/usr/bin/env bash
# Seed one git tag per release train, so MinVer keeps counting from where the
# single-train history stopped instead of restarting at 0.0.0-alpha.
#
# Splitting one train into many leaves every new train without a tag. MinVer
# would then version those packages 0.0.0-alpha.0.N — a silent downgrade below
# everything already published. Each train needs a starting tag on the commit
# the last global tag pointed at.
#
# Prints the commands by default and changes nothing. Pass --create to make the
# tags locally. Pushing them stays a separate, deliberate step: pushing a
# release tag triggers a publication.
#
# Usage:
#   scripts/bootstrap-train-tags.sh [--create] [--from <tag>] [src-directory]
#
# Exit status: 0 on success, 1 when a train tag already exists.

set -uo pipefail

create=0
from_tag=""
src_dir="src"

while [[ $# -gt 0 ]]; do
  case $1 in
    --create) create=1; shift ;;
    --from) from_tag=${2:-}; shift 2 ;;
    -*) echo "unknown option: $1" >&2; exit 2 ;;
    *) src_dir=$1; shift ;;
  esac
done

git rev-parse --git-dir >/dev/null 2>&1 || { echo "not inside a git repository" >&2; exit 2; }
[[ -d $src_dir ]] || { echo "no such directory: $src_dir" >&2; exit 2; }

# Default to the newest tag of the old single train: that is the version every
# package is at today, so every train starts from the same place.
if [[ -z $from_tag ]]; then
  from_tag=$(git tag --list 'v*' --sort=-v:refname | head -1)
  [[ -n $from_tag ]] || { echo "no v* tag found; pass --from <tag>" >&2; exit 2; }
fi

git rev-parse -q --verify "refs/tags/$from_tag" >/dev/null || {
  echo "unknown tag: $from_tag" >&2; exit 2; }

commit=$(git rev-list -n1 "$from_tag")
version=${from_tag#v}

echo "Seeding trains at $from_tag ($commit), version $version"
echo

conflicts=0
planned=0
while IFS= read -r prefix; do
  [[ -n $prefix ]] || continue
  new_tag="${prefix}${version}"
  if git rev-parse -q --verify "refs/tags/$new_tag" >/dev/null; then
    echo "EXISTS  $new_tag" >&2
    conflicts=$((conflicts + 1))
    continue
  fi
  if [[ $create -eq 1 ]]; then
    git tag "$new_tag" "$commit" && echo "created $new_tag"
  else
    echo "git tag $new_tag $commit"
  fi
  planned=$((planned + 1))
done < <(find "$src_dir" -name '*.csproj' -exec sed -n 's/.*<MinVerTagPrefix>\(.*\)<\/MinVerTagPrefix>.*/\1/p' {} + | sort -u)

echo
if [[ $conflicts -gt 0 ]]; then
  echo "$conflicts train tag(s) already exist — refusing to guess. Delete them or pass another --from." >&2
  exit 1
fi

if [[ $create -eq 1 ]]; then
  echo "$planned tag(s) created locally. Review them, then push one train at a time:"
  echo "  git push origin <train-tag>"
  echo "Pushing a train tag triggers its release."
else
  echo "$planned tag(s) would be created. Re-run with --create to make them."
fi
