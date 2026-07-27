# -*- coding: utf-8 -*-
"""Rewrite git index paths under dist/ so ASCII path segments are lowercase."""
from __future__ import annotations

import os
import re
import subprocess
import sys
import tempfile

REPO = sys.argv[1] if len(sys.argv) > 1 else os.getcwd()
DIST = sys.argv[2] if len(sys.argv) > 2 else "dist"


def run(args: list[str], input_bytes: bytes | None = None) -> subprocess.CompletedProcess:
    return subprocess.run(
        args,
        cwd=REPO,
        input=input_bytes,
        capture_output=True,
        check=False,
    )


def canonical(path: str) -> str:
    parts = path.split("/")
    out = []
    for seg in parts:
        if re.fullmatch(r"[A-Za-z0-9._\-]+", seg):
            out.append(seg.lower())
        else:
            out.append(seg)
    return "/".join(out)


def main() -> int:
    nested = os.path.join(REPO, DIST, ".git")
    if os.path.exists(nested):
        import shutil

        shutil.rmtree(nested, ignore_errors=True)
        print(f"Removed nested repository: {DIST}/.git")

    # Detect gitlink
    ls = run(["git", "-c", "core.quotepath=false", "ls-files", "-s", "--", DIST])
    text = ls.stdout.decode("utf-8", "surrogateescape")
    if any(line.startswith("160000 ") for line in text.splitlines()):
        print(f"Removing embedded gitlink for {DIST} ...")
        run(["git", "rm", "--cached", "-f", "--", DIST])

    ls = run(["git", "-c", "core.quotepath=false", "ls-files", "-s", "--", DIST])
    if ls.returncode != 0:
        print(ls.stderr.decode("utf-8", "replace"), file=sys.stderr)
        return ls.returncode

    lines = ls.stdout.decode("utf-8", "surrogateescape").splitlines()
    payload = bytearray()
    fix_count = 0
    for line in lines:
        # mode SP sha SP stage TAB path
        m = re.match(r"^(\d{6}) ([0-9a-f]{40}) (\d+)\t(.*)$", line)
        if not m:
            continue
        mode, sha, _stage, path = m.group(1), m.group(2), m.group(3), m.group(4)
        canon = canonical(path)
        if path != canon:
            # remove old, add new (same blob)
            payload.extend(f"0 {'0'*40}\t{path}\n".encode("utf-8"))
            payload.extend(f"{mode} {sha}\t{canon}\n".encode("utf-8"))
            fix_count += 1

    if fix_count:
        print(f"Repairing Git index path casing under {DIST}/ ({fix_count} paths, e.g. AI/ -> ai/)...")
        upd = run(["git", "update-index", "--index-info"], input_bytes=payload)
        if upd.returncode != 0:
            sys.stderr.write(upd.stderr.decode("utf-8", "replace"))
            return upd.returncode
        print("Git path casing repaired.")

    add = run(["git", "add", "--", DIST])
    if add.returncode != 0:
        sys.stderr.write(add.stderr.decode("utf-8", "replace"))
        return add.returncode
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
