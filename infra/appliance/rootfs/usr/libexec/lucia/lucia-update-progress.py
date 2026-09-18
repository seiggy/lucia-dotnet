#!/usr/bin/env python3
import argparse
import json
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path


def save_progress(
    phase: str, completed: int | None = None, total: int | None = None
) -> None:
    operation_id = os.environ.get("LUCIA_UPDATE_OPERATION_ID")
    if not operation_id:
        # Direct recovery commands have no manager operation to report against.
        return
    path = (
        Path(os.environ.get("LUCIA_UPDATE_ROOT", "/var/lib/lucia/updates"))
        / "state/operation.json"
    )
    descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
    with os.fdopen(descriptor, encoding="utf-8") as stream:
        status = json.load(stream)
    pascal_case = "OperationId" in status

    def key(name: str) -> str:
        return name if pascal_case else name[0].lower() + name[1:]

    if status.get(key("OperationId")) != operation_id:
        raise ValueError("Update progress does not belong to the active operation.")
    if total is not None:
        if total <= 0 or completed is None or completed < 0:
            raise ValueError("Update progress requires nonnegative bytes and a positive total.")
        if status.get(key("Phase")) == phase:
            if status.get(key("TotalBytes")) not in (None, total):
                raise ValueError("The update phase total changed.")
            completed = max(completed, status.get(key("CompletedBytes")) or 0)
        completed = min(completed, total)
    elif completed is not None:
        raise ValueError("Measured update progress requires a total.")
    status.update(
        {
            key("Phase"): phase,
            key("CompletedBytes"): completed,
            key("TotalBytes"): total,
        }
    )
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="w", encoding="utf-8", dir=path.parent, delete=False
        ) as stream:
            temporary = Path(stream.name)
            json.dump(status, stream, separators=(",", ":"))
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
        descriptor = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
        try:
            os.fsync(descriptor)
        finally:
            os.close(descriptor)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def monitor_dd(offset: int, total: int, command: list[str]) -> int:
    if offset < 0 or offset >= total or not command:
        raise ValueError("Invalid OS write progress arguments.")
    save_progress("writing", offset, total)
    with subprocess.Popen(
        command,
        stderr=subprocess.PIPE,
        text=True,
        env={**os.environ, "LC_ALL": "C"},
    ) as process:
        for line in process.stderr:
            sys.stderr.write(line)
            match = re.match(r"^(\d+) bytes\b", line)
            if match:
                save_progress("writing", offset + int(match[1]), total)
        return process.wait()


def main() -> int:
    parser = argparse.ArgumentParser(description="Persist measured update phase progress.")
    commands = parser.add_subparsers(dest="command", required=True)
    phase = commands.add_parser("phase")
    phase.add_argument("name")
    phase.add_argument("completed", type=int, nargs="?")
    phase.add_argument("total", type=int, nargs="?")
    dd = commands.add_parser("dd")
    dd.add_argument("offset", type=int)
    dd.add_argument("total", type=int)
    dd.add_argument("arguments", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    if args.command == "phase":
        save_progress(args.name, args.completed, args.total)
        return 0
    arguments = args.arguments
    if arguments and arguments[0] == "--":
        arguments = arguments[1:]
    return monitor_dd(args.offset, args.total, arguments)


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError) as error:
        raise SystemExit(f"Update progress failed: {error}") from error
