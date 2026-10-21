#!/usr/bin/env python3
"""Import an owned USA disc from BIN/IMG or nested RAR/ZIP, without executing archive contents."""
from __future__ import annotations

import argparse
import ctypes
import ctypes.util
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import stat
import tempfile
import sys
import zipfile

from disc import DiscImage, GameFilesError

ROOT = Path(__file__).resolve().parents[2]
EXECUTABLE_SHA256 = "aabf1464f90b2e0b81e712b77aebbdb88f303b16ce830535e2b0cd886ee280f2"
MAX_BYTES = 2 * 1024**3
MAX_ENTRIES = 4096
MAX_DEPTH = 3


class ImportError_(ValueError):
    pass


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def safe_name(name: str) -> Path:
    path = PurePosixPath(name.replace("\\", "/"))
    if not name or path.is_absolute() or ".." in path.parts or any(":" in p for p in path.parts):
        raise ImportError_(f"Archive entry outside its folder: {name!r}")
    if not path.parts or "\x00" in name:
        raise ImportError_(f"Invalid archive entry: {name!r}")
    return Path(*path.parts)


class Budget:
    def __init__(self, maximum=MAX_BYTES):
        self.remaining = maximum
        self.entries = MAX_ENTRIES

    def entry(self, size):
        self.entries -= 1
        if size < 0 or size > self.remaining or self.entries < 0:
            raise ImportError_("Archive exceeds the import size or entry limit")

    def consume(self, size):
        self.remaining -= size
        if self.remaining < 0:
            raise ImportError_("Archive exceeds the import size limit")


def extract_zip(source, destination, budget):
    seen = set()
    with zipfile.ZipFile(source) as archive:
        for entry in archive.infolist():
            relative = safe_name(entry.filename)
            key = relative.as_posix().casefold()
            if key in seen:
                raise ImportError_(f"Duplicate archive path: {relative}")
            seen.add(key)
            mode = entry.external_attr >> 16
            if stat.S_ISLNK(mode):
                raise ImportError_(f"Archive links are not accepted: {relative}")
            budget.entry(entry.file_size)
            target = destination / relative
            if entry.is_dir():
                target.mkdir(parents=True, exist_ok=True)
                continue
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(entry) as stream, target.open("xb") as out:
                for block in iter(lambda: stream.read(1024 * 1024), b""):
                    budget.consume(len(block))
                    out.write(block)


def libarchive():
    bundled = Path(getattr(sys, "_MEIPASS", "")) / "libarchive.so.13"
    name = str(bundled) if getattr(sys, "frozen", False) and bundled.is_file() else ctypes.util.find_library("archive")
    if not name:
        raise ImportError_("RAR import needs libarchive (Linux: libarchive; Ubuntu: libarchive13). "
                           "Alternatively unpack the RAR yourself and import its IMG/BIN file.")
    lib = ctypes.CDLL(name)
    declarations = {
        "archive_read_new": (ctypes.c_void_p, []),
        "archive_read_support_filter_all": (ctypes.c_int, [ctypes.c_void_p]),
        "archive_read_support_format_all": (ctypes.c_int, [ctypes.c_void_p]),
        "archive_read_open_filename": (ctypes.c_int, [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_size_t]),
        "archive_read_next_header": (ctypes.c_int, [ctypes.c_void_p, ctypes.POINTER(ctypes.c_void_p)]),
        "archive_entry_pathname": (ctypes.c_char_p, [ctypes.c_void_p]),
        "archive_entry_size": (ctypes.c_longlong, [ctypes.c_void_p]),
        "archive_entry_filetype": (ctypes.c_uint, [ctypes.c_void_p]),
        "archive_entry_hardlink": (ctypes.c_char_p, [ctypes.c_void_p]),
        "archive_read_data": (ctypes.c_ssize_t, [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t]),
        "archive_error_string": (ctypes.c_char_p, [ctypes.c_void_p]),
        "archive_read_free": (ctypes.c_int, [ctypes.c_void_p]),
    }
    for name, (result, arguments) in declarations.items():
        function = getattr(lib, name)
        function.restype, function.argtypes = result, arguments
    return lib


def extract_archive(source, destination, budget):
    lib = libarchive()
    archive = lib.archive_read_new()
    if not archive:
        raise ImportError_("Cannot allocate archive reader")
    def fail():
        message = lib.archive_error_string(archive)
        raise ImportError_(message.decode("utf-8", "replace") if message else "Cannot read RAR")
    try:
        lib.archive_read_support_filter_all(archive)
        lib.archive_read_support_format_all(archive)
        if lib.archive_read_open_filename(archive, os.fsencode(source), 65536) != 0:
            fail()
        entry = ctypes.c_void_p()
        buffer = ctypes.create_string_buffer(1024 * 1024)
        seen = set()
        while True:
            code = lib.archive_read_next_header(archive, ctypes.byref(entry))
            if code == 1:  # ARCHIVE_EOF
                break
            if code != 0:
                fail()
            name = lib.archive_entry_pathname(entry)
            if not name:
                raise ImportError_("Archive entry has no name")
            relative = safe_name(os.fsdecode(name))
            key = relative.as_posix().casefold()
            if key in seen:
                raise ImportError_(f"Duplicate archive path: {relative}")
            seen.add(key)
            kind = lib.archive_entry_filetype(entry)
            if kind not in (stat.S_IFREG, stat.S_IFDIR) or lib.archive_entry_hardlink(entry):
                raise ImportError_(f"Archive links or special files are not accepted: {relative}")
            budget.entry(lib.archive_entry_size(entry))
            target = destination / relative
            if kind == stat.S_IFDIR:
                target.mkdir(parents=True, exist_ok=True)
                continue
            target.parent.mkdir(parents=True, exist_ok=True)
            with target.open("xb") as out:
                while True:
                    count = lib.archive_read_data(archive, buffer, len(buffer))
                    if count == 0:
                        break
                    if count < 0:
                        fail()
                    budget.consume(count)
                    out.write(buffer.raw[:count])
    finally:
        lib.archive_read_free(archive)


def candidates(source, work, budget, depth=0):
    if source.suffix.lower() in (".bin", ".img"):
        yield source
        return
    if source.suffix.lower() == ".cue":
        import re
        if source.stat().st_size > 65536:
            raise ImportError_("O arquivo CUE excede o tamanho permitido.")
        text = source.read_text(encoding="utf-8-sig")
        files = re.findall(r'^\s*FILE\s+"([^"]+)"\s+BINARY\s*$', text, re.M | re.I)
        tracks = re.findall(r'^\s*TRACK\s+\d+\s+(\S+)', text, re.M | re.I)
        indexes = re.findall(r'^\s*INDEX\s+01\s+(\S+)', text, re.M | re.I)
        if len(files) != 1 or [t.upper() for t in tracks] != ["MODE2/2352"] or indexes != ["00:00:00"]:
            raise ImportError_("Escolha um disco americano com uma faixa MODE2/2352, começando em 00:00:00.")
        image = source.parent / safe_name(files[0])
        if not image.is_file():
            raise ImportError_("O BIN indicado no CUE não foi encontrado ao lado dele.")
        yield image
        return
    if source.suffix.lower() not in (".zip", ".rar", ".7z"):
        raise ImportError_("Escolha BIN, IMG, CUE, ZIP, RAR ou 7Z do Crash Bandicoot americano.")
    if depth >= MAX_DEPTH:
        raise ImportError_("Há arquivos compactados demais dentro de outros arquivos.")
    stage = Path(tempfile.mkdtemp(prefix="archive-", dir=work))
    # Detect the actual format, rather than trusting the name: the supplied
    # Crash Bandicoot.zip is a 7z. No archive document/program is executed.
    (extract_zip if zipfile.is_zipfile(source) else extract_archive)(source, stage, budget)
    for path in sorted(stage.rglob("*")):
        if path.is_file() and path.suffix.lower() in (".bin", ".img", ".zip", ".rar", ".7z"):
            yield from candidates(path, work, budget, depth + 1)


def inspect_disc(source):
    source = Path(source)
    with DiscImage(source) as disc:
        if (disc.sector, disc.skip) != (2352, 24):
            raise ImportError_("O runtime precisa de um disco Mode 2 com setores de 2352 bytes.")
        executable = disc.find("SCUS_949.00")
        if not executable or executable[1] != 290816:
            raise ImportError_("Este não é o Crash Bandicoot americano SCUS-94900 suportado.")
        payload = disc.read(*executable)
        digest = hashlib.sha256(payload).hexdigest()
        if not payload.startswith(b"PS-X EXE") or digest != EXECUTABLE_SHA256:
            raise ImportError_(f"Executável modificado ou edição incompatível: SHA-256 {digest}")
        location = disc.find("SYSTEM.CNF")
        if not location or b"SCUS_949.00" not in disc.read(*location):
            raise ImportError_("O disco não contém o boot esperado do Crash americano.")
        for folder in ("S0", "S1", "S2", "S3"):
            location = disc.find(folder)
            entries = list(disc._directory(*location)) if location else []
            streams = [(name, start, size) for name, start, size, is_dir in entries
                       if not is_dir and name.endswith((".NSD", ".NSF")) and size > 0]
            if not any(name.endswith(".NSD") for name, _, _ in streams) or not any(name.endswith(".NSF") for name, _, _ in streams):
                raise ImportError_(f"O disco não contém os arquivos de fases esperados em {folder}.")
            for _, start, size in streams:
                disc.read_sector(start + (size - 1) // 2048)
        return {"serial": "SCUS-94900", "executable_sha256": digest, "sector_size": disc.sector}


def import_disc(source, destination):
    source, destination = Path(source).resolve(), Path(destination).resolve()
    destination.mkdir(parents=True, exist_ok=True)
    target, cue = destination / "crash.bin", destination / "crash.cue"
    with tempfile.TemporaryDirectory(prefix=".import-", dir=destination) as temporary:
        work = Path(temporary)
        images = list(candidates(source, work, Budget()))
        if len(images) != 1:
            raise ImportError_(f"Esperado exatamente um disco; encontrados {len(images)}.")
        image = images[0]
        metadata = inspect_disc(image)
        metadata.update(disc_sha256=sha256(image), disc_size=image.stat().st_size)
        if target.exists() and sha256(target) != metadata["disc_sha256"]:
            raise ImportError_("Já existe outro disco nessa pasta. Ele foi preservado.")
        if not target.exists():
            staged = work / "validated.bin"
            if image.is_relative_to(work):
                os.replace(image, staged)
            else:
                shutil.copyfile(image, staged)
            os.replace(staged, target)
        cue.write_text('FILE "crash.bin" BINARY\n  TRACK 01 MODE2/2352\n    INDEX 01 00:00:00\n')
        (destination / "import.json").write_text(json.dumps(metadata, indent=2) + "\n")
    return cue


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    print(import_disc(args.source, args.destination))
