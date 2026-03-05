#!/usr/bin/env python3
"""Validate files are UTF-8 encoded without BOM."""

from __future__ import annotations

import argparse
from pathlib import Path
import sys

UTF8_BOM = b"\xef\xbb\xbf"


def validate_file(path: Path) -> bool:
    """Return True when file is UTF-8 and has no BOM."""
    if not path.exists():
        print(f"[encoding] Missing file: {path}")
        return False

    data = path.read_bytes()
    if data.startswith(UTF8_BOM):
        print(f"[encoding] BOM detected (must be UTF-8 without BOM): {path}")
        return False

    try:
        data.decode("utf-8")
    except UnicodeDecodeError as err:
        print(f"[encoding] Not valid UTF-8: {path} ({err})")
        return False

    return True


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Validate one or more files are UTF-8 without BOM."
    )
    parser.add_argument("paths", nargs="+", help="File paths to validate")
    args = parser.parse_args()

    all_valid = True
    for raw_path in args.paths:
        file_path = Path(raw_path)
        all_valid = validate_file(file_path) and all_valid

    if all_valid:
        print("[encoding] OK")
        return 0

    return 1


if __name__ == "__main__":
    sys.exit(main())
