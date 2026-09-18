#!/usr/bin/env python3
import os
import re
import sys
import uuid
from pathlib import Path


def regular_path(root: Path, relative: str) -> Path:
    path = root
    for part in Path(relative).parts:
        path /= part
        if path.is_symlink():
            raise ValueError(
                f"Refusing a symlink in inactive boot configuration: {relative}"
            )
    if not path.is_file():
        raise ValueError(f"Inactive OS image is missing {relative}")
    return path


def rebind(root: Path, partition_uuid: str) -> None:
    partition_uuid = str(uuid.UUID(partition_uuid))
    if root.is_symlink() or not root.is_dir():
        raise ValueError("Inactive root must be a real directory.")
    boot = regular_path(root, "boot/extlinux/extlinux.conf")
    fstab = regular_path(root, "etc/fstab")
    lines = boot.read_text().splitlines(keepends=True)
    changed = []
    append_count = 0
    for line in lines:
        if re.match(r"^\s*APPEND\s+", line, re.IGNORECASE):
            append_count += 1
            if re.search(r"\broot=\S+", line):
                line = re.sub(r"\broot=\S+", f"root=PARTUUID={partition_uuid}", line)
            else:
                line = line.rstrip() + f" root=PARTUUID={partition_uuid}\n"
        changed.append(line)
    if not append_count:
        raise ValueError("Inactive OS image has no extlinux kernel arguments.")
    mounts = []
    for line in fstab.read_text().splitlines(keepends=True):
        fields = line.split()
        if len(fields) >= 2 and not fields[0].startswith("#") and fields[1] == "/":
            line = re.sub(r"^\s*\S+", f"PARTUUID={partition_uuid}", line)
        mounts.append(line)
    for path, content in ((boot, "".join(changed)), (fstab, "".join(mounts))):
        # The inactive slot is not activated until all writes and fsck complete.
        with path.open("w", encoding="utf-8") as stream:
            stream.write(content)
            stream.flush()
            os.fsync(stream.fileno())


if __name__ == "__main__":
    try:
        if len(sys.argv) != 3:
            raise ValueError("Usage: lucia-rebind-rootfs.py INACTIVE_ROOT PARTUUID")
        rebind(Path(sys.argv[1]), sys.argv[2])
    except (OSError, ValueError) as error:
        raise SystemExit(f"OS boot configuration rejected: {error}") from error
