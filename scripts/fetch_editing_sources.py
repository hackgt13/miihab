#!/usr/bin/env python3
"""Download optional Blender/reference sources from the private team release."""
import argparse
import hashlib
from pathlib import Path, PurePosixPath
import shutil
import subprocess
import tarfile
import tempfile

REPOSITORY = "hackgt13/rehabmii"
RELEASE = "team-art-sources-v1"
ARCHIVE = "rehabmii-editing-sources-v1.tar.gz"
SHA256 = "6f6b02d92dbd35a63f893b61aedb4635dc30a9358b1154d741304f0474ab5247"


def digest(source):
    result = hashlib.sha256()
    for block in iter(lambda: source.read(1024 * 1024), b""):
        result.update(block)
    return result.hexdigest()


def install(archive_path, output_dir, overwrite):
    with archive_path.open("rb") as source:
        if digest(source) != SHA256:
            raise RuntimeError("Archive checksum mismatch; no source files were changed")
    with tarfile.open(archive_path, "r:gz") as archive:
        files = []
        for member in archive.getmembers():
            path = PurePosixPath(member.name)
            if (not member.isfile() or path.is_absolute() or ".." in path.parts
                    or len(path.parts) < 3 or path.parts[0] != "art"
                    or path.parts[1] not in ("mii", "course-reference")):
                raise RuntimeError("Unexpected archive entry: " + member.name)
            target = output_dir.joinpath(*path.parts)
            if output_dir.resolve() not in target.resolve().parents or target.is_symlink():
                raise RuntimeError("Unsafe destination: " + str(target))
            if target.exists() and not overwrite:
                with target.open("rb") as existing, archive.extractfile(member) as incoming:
                    if digest(existing) != digest(incoming):
                        raise RuntimeError("Preserved edited file: " + str(target)
                                           + "; use --overwrite to replace it")
                continue
            files.append((member, target))
        for member, target in files:
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.extractfile(member) as source, target.open("wb") as output:
                shutil.copyfileobj(source, output)
        print(f"Verified editing sources; installed {len(files)} files")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--overwrite", action="store_true", help="Replace locally edited source files")
    parser.add_argument("--output-dir", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    try:
        with tempfile.TemporaryDirectory(prefix="rehabmii-art-download-") as temporary:
            subprocess.run(["gh", "release", "download", RELEASE, "--repo", REPOSITORY,
                            "--pattern", ARCHIVE, "--dir", temporary], check=True)
            install(Path(temporary) / ARCHIVE, args.output_dir, args.overwrite)
    except FileNotFoundError:
        parser.exit(1, "Install GitHub CLI and run gh auth login with access to hackgt13/rehabmii.\n")
    except Exception as error:
        parser.exit(1, f"Source setup failed: {error}\n")


if __name__ == "__main__":
    main()
