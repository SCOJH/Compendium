#!/usr/bin/env python3
"""Decide whether any published version satisfies a NuGet dependency range.

Used by verify-package-dependencies-exist.sh. Kept as its own file rather than a
heredoc so that `--self-test` can exercise it without a network and without a
packed .nupkg.

A nuspec dependency `version` is a RANGE, not an equality. `1.0.5-preview.7`
means `>= 1.0.5-preview.7`. Checking for the exact string would reject a
legitimate release the moment a higher version exists and the exact floor does
not — a false red on the only irreversible step of a release, which is worse
than no check at all.

Reads the flat-container index JSON on stdin.

Usage:
    nuget-version-satisfies.py <range> [--self-test]

Exit status:
    0  at least one published version satisfies the range
    1  none does
    2  the range could not be parsed, or the index is unreadable
       (not knowing is not permission — the caller must treat this as a failure)
"""
import io
import json
import re
import sys

# --------------------------------------------------------------------------- #
# NuGet version ordering
# --------------------------------------------------------------------------- #

_VERSION = re.compile(
    r"^\s*(?P<release>\d+(?:\.\d+){0,3})"
    r"(?:-(?P<pre>[0-9A-Za-z.-]+))?"
    r"(?:\+(?P<meta>[0-9A-Za-z.-]+))?\s*$"
)


def parse(text):
    """Return a sort key, or None when `text` is not a version."""
    m = _VERSION.match(text or "")
    if not m:
        return None

    release = [int(p) for p in m.group("release").split(".")]
    # NuGet compares on four parts; a missing part is zero.
    release += [0] * (4 - len(release))

    pre = m.group("pre")
    if pre is None:
        # No prerelease sorts ABOVE any prerelease of the same release.
        return (release, 1, [])

    parts = []
    for ident in pre.split("."):
        if ident.isdigit():
            # Numeric identifiers compare numerically and below alphanumeric ones.
            parts.append((0, int(ident), ""))
        else:
            parts.append((1, 0, ident.lower()))
    return (release, 0, parts)


def less(a, b):
    """a < b, on keys produced by parse()."""
    if a[0] != b[0]:
        return a[0] < b[0]
    if a[1] != b[1]:
        return a[1] < b[1]
    # Prerelease identifiers: compare element by element; a shorter run of
    # identifiers sorts below a longer one with the same prefix, which is why
    # `preview.5` < `preview.5.11` — the exact trap that shipped a package
    # nobody could install on 2026-09-29.
    for x, y in zip(a[2], b[2]):
        if x != y:
            return x < y
    return len(a[2]) < len(b[2])


# --------------------------------------------------------------------------- #
# Range parsing
# --------------------------------------------------------------------------- #

def bounds(spec):
    """Return (low, low_inclusive, high, high_inclusive) or None if unparseable.

    `low`/`high` are parse() keys, or None for "unbounded on that side".
    """
    spec = (spec or "").strip()
    if not spec:
        return None

    if not (spec[0] in "[(" and spec[-1] in "])"):
        # Bare version: a MINIMUM, inclusive. This is what `dotnet pack` writes.
        key = parse(spec)
        return None if key is None else (key, True, None, False)

    low_inc = spec[0] == "["
    high_inc = spec[-1] == "]"
    inner = spec[1:-1]

    if "," not in inner:
        # `[1.2.3]` — an exact version. `(1.2.3)` is not legal NuGet.
        if not (low_inc and high_inc):
            return None
        key = parse(inner)
        return None if key is None else (key, True, key, True)

    low_text, _, high_text = inner.partition(",")
    low = parse(low_text) if low_text.strip() else None
    high = parse(high_text) if high_text.strip() else None
    if (low_text.strip() and low is None) or (high_text.strip() and high is None):
        return None
    return (low, low_inc, high, high_inc)


def satisfies(key, rng):
    low, low_inc, high, high_inc = rng
    if low is not None:
        if less(key, low):
            return False
        if not low_inc and not less(low, key):
            return False
    if high is not None:
        if less(high, key):
            return False
        if not high_inc and not less(key, high):
            return False
    return True


# --------------------------------------------------------------------------- #
# Self-test — offline, no network, no .nupkg
# --------------------------------------------------------------------------- #

class _Stdin:
    """Minimal stand-in for sys.stdin exposing the `.buffer` main() reads."""

    def __init__(self, payload):
        self.buffer = io.BytesIO(payload)



def self_test():
    ordering = [
        # (lower, higher) — every pair must hold in this direction.
        ("1.0.4", "1.0.5"),
        ("1.0.5-preview.1", "1.0.5-preview.2"),
        ("1.0.5-preview.2", "1.0.5-preview.10"),          # numeric, not lexical
        ("1.0.5-preview.5", "1.0.5-preview.5.11"),        # THE 2026-09-29 trap
        ("1.0.5-preview.7", "1.0.5"),                     # release beats prerelease
        ("1.0.5-alpha", "1.0.5-beta"),
        ("1.0.5-preview.5", "1.0.5-preview.5.0"),
    ]
    cases = [
        # (range, published versions, expected exit)
        ("1.0.5-preview.7", ["1.0.5-preview.7"], 0),
        ("1.0.5-preview.7", ["1.0.5-preview.8"], 0),       # higher satisfies a minimum
        ("1.0.5-preview.7", ["1.0.5"], 0),
        ("1.0.5-preview.5.11", ["1.0.5-preview.5"], 1),    # the real failure, reproduced
        ("1.0.5-preview.5.11", ["1.0.5-preview.5",
                                "1.0.5-preview.4"], 1),
        ("1.0.5-preview.5.11", ["1.0.5-preview.6"], 0),
        ("1.0.5-preview.7", [], 1),
        ("[1.0.5-preview.7]", ["1.0.5-preview.8"], 1),     # exact range, nothing matches
        ("[1.0.5-preview.7]", ["1.0.5-preview.7"], 0),
        ("[1.0.0,2.0.0)", ["2.0.0"], 1),
        ("[1.0.0,2.0.0)", ["1.9.9"], 0),
        ("(1.0.0,)", ["1.0.0"], 1),                        # exclusive lower bound
        ("(1.0.0,)", ["1.0.1"], 0),
        ("(,1.0.0]", ["0.9.0"], 0),                        # no lower bound
        ("not-a-version", ["1.0.0"], 2),
        ("", ["1.0.0"], 2),
    ]

    failures = []

    for lower, higher in ordering:
        a, b = parse(lower), parse(higher)
        if a is None or b is None:
            failures.append(f"ordering: unparseable pair {lower} / {higher}")
        elif not less(a, b):
            failures.append(f"ordering: expected {lower} < {higher}")
        elif less(b, a):
            failures.append(f"ordering: {higher} < {lower} must not hold")

    # Exercised THROUGH main(), deliberately — not through bounds() and
    # satisfies() directly. The first version of this self-test called the
    # internals and passed 23/23 while main() was broken by an off-by-one on
    # argv: every real invocation exited 2 and the guard refused every package,
    # valid ones included. A self-test that skips the entry point does not test
    # the thing that runs.
    import io

    for spec, published, expected in cases:
        # stderr is captured too: two cases exercise the unparseable-range path
        # on purpose, and their diagnostics are expected output, not a problem.
        # A self-test that prints them on success buries its own verdict.
        saved = (sys.stdin, sys.stdout, sys.stderr)
        sys.stdin = _Stdin(json.dumps({"versions": published}).encode("utf-8"))
        sys.stdout, sys.stderr = io.StringIO(), io.StringIO()
        try:
            got = main([spec])
        finally:
            sys.stdin, sys.stdout, sys.stderr = saved
        if got != expected:
            failures.append(
                f"main([{spec!r}]) against {published}: expected exit {expected}, got {got}")

    for problem in failures:
        print(f"FAIL  {problem}", file=sys.stderr)

    total = len(ordering) + len(cases)
    if failures:
        print(f"\n{len(failures)} of {total} self-test case(s) failed.", file=sys.stderr)
        return 1
    print(f"nuget-version-satisfies: all {total} self-test case(s) pass.")
    return 0


# --------------------------------------------------------------------------- #

def main(argv):
    if "--self-test" in argv:
        return self_test()

    if len(argv) != 1:
        # Terse, not the whole docstring: this is called in a loop, and dumping
        # the module doc per dependency buried the real verdict when it happened.
        print("usage: nuget-version-satisfies.py <range> [--self-test]", file=sys.stderr)
        return 2

    rng = bounds(argv[0])
    if rng is None:
        print(f"unparseable version range: {argv[0]!r}", file=sys.stderr)
        return 2

    try:
        # utf-8-sig: the flat container is served from blob storage and its
        # bodies can carry a BOM (measured 2026-09-04) — same reason as
        # verify-package-not-published.sh.
        index = json.loads(sys.stdin.buffer.read().decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        print(f"unreadable flat-container index: {exc}", file=sys.stderr)
        return 2

    published = index.get("versions", [])
    if not isinstance(published, list):
        print("flat-container index has no version list", file=sys.stderr)
        return 2

    for text in published:
        key = parse(str(text))
        if key is not None and satisfies(key, rng):
            print(text)
            return 0
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
