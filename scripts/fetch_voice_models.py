#!/usr/bin/env python3
"""Fetch the pinned optional Kokoro model files using only Python's standard library."""
import argparse
import hashlib
import os
from pathlib import Path
import tempfile
import urllib.request

RELEASE = "https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/"
FILES = {
    "kokoro-v1.0.onnx": "7d5df8ecf7d4b1878015a32686053fd0eebe2bc377234608764cc0ef3636a6c5",
    "voices-v1.0.bin": "bca610b8308e8d99f32e6fe4197e7ec01679264efed0cac9140fe9c29f1fbf7d",
}


def digest(path):
    result = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            result.update(block)
    return result.hexdigest()


def fetch(name, directory, check_only):
    target = directory / name
    expected = FILES[name]
    if target.is_file() and digest(target) == expected:
        print(f"Verified {name}")
        return
    if check_only:
        raise RuntimeError(f"{target} is missing or has a different checksum")
    directory.mkdir(parents=True, exist_ok=True)
    fd, temporary = tempfile.mkstemp(prefix=name + ".", suffix=".part", dir=directory)
    temporary = Path(temporary)
    try:
        print(f"Downloading {name} from model-files-v1.0…", flush=True)
        request = urllib.request.Request(RELEASE + name, headers={"User-Agent": "rehabmii-model-setup"})
        with os.fdopen(fd, "wb") as output:
            with urllib.request.urlopen(request, timeout=60) as response:
                for block in iter(lambda: response.read(1024 * 1024), b""):
                    output.write(block)
        if digest(temporary) != expected:
            raise RuntimeError(f"Checksum mismatch for {name}; existing files were preserved")
        temporary.replace(target)
        print(f"Verified {name}")
    finally:
        temporary.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("files", nargs="*", help="Optional filenames; defaults to both Kokoro files")
    parser.add_argument("--check", action="store_true", help="Verify existing files without downloading")
    parser.add_argument("--models-dir", type=Path, default=Path(__file__).resolve().parents[1] / "coach/models")
    args = parser.parse_args()
    selected = args.files or list(FILES)
    for name in selected:
        if name not in FILES:
            parser.error(f"Unknown model file: {name}")
    for name in selected:
        try:
            fetch(name, args.models_dir, args.check)
        except Exception as error:
            parser.exit(1, f"Model setup failed: {error}\n")


if __name__ == "__main__":
    main()
