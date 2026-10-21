#!/usr/bin/env python3
"""Validate an owned Crash disc, cache invalidation, gameplay and representative scenes locally."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
from model import Library


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("--program", type=Path, required=True)
    parser.add_argument("--data-dir", type=Path, required=True)
    args = parser.parse_args()
    library = Library(args.data_dir, args.program)
    library.prepare(args.source, print)
    manifests = list((library.player_directory / "game").glob("*/manifest.json"))
    assert len(manifests) == 1
    manifest = manifests[0]
    original = json.loads(manifest.read_text())["PipelineVersion"]
    compiled = Path(json.loads(manifest.read_text())["DllPath"])
    compiled_time = compiled.stat().st_mtime_ns
    manifest_bytes = manifest.read_bytes()
    library.prepare(args.source, print)
    assert compiled.stat().st_mtime_ns == compiled_time and manifest.read_bytes() == manifest_bytes
    with tempfile.TemporaryDirectory(prefix="engine-copy-", dir=library.directory) as work:
        root = Path(work)
        shutil.copytree(library.engine().parent, root / "engine")
        config = root / "engine/Recomp/CrashBandicoot.json"
        config.write_bytes(config.read_bytes() + b"\n")
        Library(library.directory, root).prepare(args.source, print)
        changed = json.loads(manifest.read_text())["PipelineVersion"]
        assert changed != original, "A changed recomp config must invalidate the generated code"
    library.prepare(args.source, print)
    assert json.loads(manifest.read_text())["PipelineVersion"] == original
    process = library.launch(test=True)
    try:
        assert process.wait(timeout=150) == 0
    finally:
        if process.poll() is None:
            process.terminate()
            process.wait(timeout=10)
    gameplay = json.loads((library.directory / "game-test/report.json").read_text())
    assert gameplay["passed"]
    scenes = []
    for level in (25, 46, 31):
        output = library.directory / f"scene-{level}"
        with (library.directory / f"scene-{level}.log").open("w") as log:
            subprocess.run([str(library.engine()), "--desktop-test", str(library.disc), str(output), str(level)],
                           env=library.game_environment(), cwd=library.program, stdout=log,
                           stderr=subprocess.STDOUT, timeout=150, check=True)
        report = json.loads((output / "report.json").read_text())
        assert report["passed"]
        scenes.append({"level": level, "passed": True, "scope": report["scope"]})
    report = {"passed": True, "disc": "SCUS-94900", "cache_reused": True,
              "cache_invalidated_on_config_change": original != changed,
              "gameplay": gameplay, "scenes": scenes,
              "scope": "Native movement/jump/spin inputs and save/load on N. Sanity Beach; "
                       "boot/render of title/map, Slippery Climb and Cortex. Not a full campaign playthrough."}
    path = library.directory / "validation.json"
    path.write_text(json.dumps(report, indent=2) + "\n")
    print(f"Validation passed: {path}")


if __name__ == "__main__":
    main()
