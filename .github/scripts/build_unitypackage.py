#!/usr/bin/env python3
"""Build a Unity .unitypackage from an Assets folder using its checked-in .meta files."""

from __future__ import annotations

import argparse
import gzip
import io
import re
import tarfile
from pathlib import Path


GUID_RE = re.compile(r"^guid:\s*([0-9a-fA-F]{32})\s*$", re.MULTILINE)


def add_bytes(archive: tarfile.TarFile, name: str, data: bytes) -> None:
    info = tarfile.TarInfo(name)
    info.size = len(data)
    info.mode = 0o644
    info.mtime = 0
    archive.addfile(info, io.BytesIO(data))


def guid_from_meta(meta_path: Path) -> str:
    contents = meta_path.read_text(encoding="utf-8")
    match = GUID_RE.search(contents)
    if not match:
        raise ValueError(f"Missing valid GUID in {meta_path}")
    return match.group(1).lower()


def build(source: Path, output: Path) -> int:
    source = source.resolve()
    if not source.is_dir():
        raise FileNotFoundError(f"Assets folder does not exist: {source}")

    entries: list[tuple[str, Path, bool]] = []
    for path in source.rglob("*"):
        if path.name.endswith(".meta"):
            continue
        is_directory = path.is_dir()
        meta = Path(str(path) + ".meta")
        if not meta.is_file():
            raise FileNotFoundError(f"Missing Unity .meta file: {meta}")
        entries.append((path.relative_to(source.parent.parent).as_posix(), path, is_directory))

    if not entries:
        raise ValueError(f"No assets found under {source}")

    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("wb") as raw:
        with gzip.GzipFile(fileobj=raw, mode="wb", mtime=0) as compressed:
            with tarfile.open(fileobj=compressed, mode="w|", format=tarfile.USTAR_FORMAT) as archive:
                seen_guids: set[str] = set()
                for asset_path, path, is_directory in sorted(entries, key=lambda item: item[0]):
                    meta_path = Path(str(path) + ".meta")
                    guid = guid_from_meta(meta_path)
                    if guid in seen_guids:
                        raise ValueError(f"Duplicate asset GUID {guid} at {path}")
                    seen_guids.add(guid)

                    prefix = f"{guid}/"
                    add_bytes(archive, prefix + "pathname", (asset_path + "\n").encode("utf-8"))
                    add_bytes(archive, prefix + "asset.meta", meta_path.read_bytes())
                    if not is_directory:
                        add_bytes(archive, prefix + "asset", path.read_bytes())

    return len(entries)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True, help="Unity Assets folder to package")
    parser.add_argument("--output", type=Path, required=True, help="Output .unitypackage path")
    args = parser.parse_args()

    count = build(args.source, args.output)
    print(f"Created {args.output} with {count} Unity assets")


if __name__ == "__main__":
    main()
