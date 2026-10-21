import hashlib
import json
from pathlib import Path
import stat
import struct
import sys
import tempfile
import unittest
from unittest import mock
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/desktop"))
import import_disc as disc
from model import Library


def record(name, sector, size, directory=False):
    name = name.encode("ascii")
    length = 33 + len(name)
    length += length % 2
    entry = bytearray(length)
    entry[0] = length
    struct.pack_into("<I", entry, 2, sector)
    struct.pack_into("<I", entry, 10, size)
    entry[25] = 2 if directory else 0
    entry[32] = len(name)
    entry[33:33 + len(name)] = name
    return entry


def image(path):
    """Original synthetic ISO data, not a game executable or disc dump."""
    boot = b"PS-X EXE" + bytes(290816 - 8)
    sectors = [bytearray(2048) for _ in range(180)]
    sectors[16][0:7] = b"\x01CD001\x01"
    sectors[16][156:190] = record("\0", 20, 2048, True)
    cnf = b"BOOT = cdrom:\\SCUS_949.00;1\r\n"
    entries = record("SYSTEM.CNF;1", 21, len(cnf)) + record("SCUS_949.00;1", 22, len(boot))
    for i in range(4):
        entries += record(f"S{i}", 170 + i, 2048, True)
        content = record("S0000000.NSD;1", 175, 16) + record("S0000000.NSF;1", 176, 16)
        sectors[170 + i][:len(content)] = content
    sectors[20][:len(entries)] = entries
    sectors[21][:len(cnf)] = cnf
    for i in range((len(boot) + 2047) // 2048):
        chunk = boot[i * 2048:(i + 1) * 2048]
        sectors[22 + i][:len(chunk)] = chunk
    with path.open("wb") as stream:
        for sector in sectors:
            stream.write(bytes(24) + sector + bytes(280))
    return hashlib.sha256(boot).hexdigest()


class ImportChecks(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.bin = self.root / "synthetic.bin"
        self.sha = image(self.bin)

    def test_supported_structure_full_executable_hash_and_stream_bounds(self):
        with mock.patch.object(disc, "EXECUTABLE_SHA256", self.sha):
            self.assertEqual(disc.inspect_disc(self.bin)["serial"], "SCUS-94900")
            with self.bin.open("r+b") as stream:
                stream.seek(22 * 2352 + 24 + 400)
                stream.write(b"X")
            with self.assertRaises(disc.ImportError_):
                disc.inspect_disc(self.bin)

    def test_wrong_game_rejected(self):
        with self.assertRaises(disc.ImportError_):
            disc.inspect_disc(self.bin)

    def test_misnamed_zip_detected_and_cue_normalized(self):
        archive = self.root / "archive.rar"
        with zipfile.ZipFile(archive, "w") as out:
            out.write(self.bin, "Crash/synthetic.bin")
            out.writestr("Crash/misleading.cue", 'FILE "../../outside.bin" BINARY')
        with mock.patch.object(disc, "EXECUTABLE_SHA256", self.sha):
            cue = disc.import_disc(archive, self.root / "data")
        self.assertIn('FILE "crash.bin" BINARY', cue.read_text())
        self.assertEqual(cue.with_suffix(".bin").read_bytes(), self.bin.read_bytes())

    def test_traversal_and_links_are_rejected(self):
        for name in ("../escape.bin", "/escape.bin", "C:/escape.bin", "..\\escape.bin"):
            with self.subTest(name=name), self.assertRaises(disc.ImportError_):
                disc.safe_name(name)
        archive = self.root / "symlink.zip"
        with zipfile.ZipFile(archive, "w") as out:
            link = zipfile.ZipInfo("link.bin")
            link.create_system = 3
            link.external_attr = (stat.S_IFLNK | 0o777) << 16
            out.writestr(link, "outside")
        with self.assertRaises(disc.ImportError_):
            disc.import_disc(archive, self.root / "data")

    def test_limits_and_multiple_discs_are_rejected(self):
        with self.assertRaises(disc.ImportError_):
            disc.Budget(maximum=10).consume(11)
        archive = self.root / "pair.zip"
        with zipfile.ZipFile(archive, "w") as out:
            out.write(self.bin, "one.bin")
            out.write(self.bin, "two.bin")
        with self.assertRaises(disc.ImportError_):
            disc.import_disc(archive, self.root / "data")

    def test_invalid_selection_preserves_previous_config(self):
        library = Library(self.root / "library", ROOT)
        library.state = {"disc": str(self.bin), "name": "previous"}
        with self.assertRaises(ValueError):
            library.prepare(self.root / "missing.zip")
        self.assertEqual(library.state["name"], "previous")

    def test_corrupted_managed_disc_is_repaired_from_source(self):
        library = Library(self.root / "library", ROOT)
        with mock.patch.object(disc, "EXECUTABLE_SHA256", self.sha), \
             mock.patch.object(library, "engine", return_value=Path("/mock-engine")), \
             mock.patch("model.subprocess.run", return_value=mock.Mock(returncode=0)):
            cue = library.prepare(self.bin)
            cue.with_suffix(".bin").write_bytes(b"broken")
            library.prepare(self.bin)
            self.assertEqual(cue.with_suffix(".bin").read_bytes(), self.bin.read_bytes())

    def test_engine_environment_does_not_inherit_frozen_gtk(self):
        library = Library(self.root / "library", ROOT)
        with mock.patch.dict("os.environ", {"LD_LIBRARY_PATH": "/frozen", "GI_TYPELIB_PATH": "/frozen"}):
            env = library.game_environment()
        self.assertNotIn("LD_LIBRARY_PATH", env)
        self.assertNotIn("GI_TYPELIB_PATH", env)
        self.assertEqual(env["CRASH_PC_DATA_DIR"], str(library.player_directory))

    def test_desktop_entry_quotes_paths(self):
        library = Library(self.root / "library", ROOT)
        path = library.install_desktop_entry(self.root / "menu with spaces")
        self.assertIn("Name=Crash Bandicoot PC", path.read_text())
        self.assertIn('Exec="', path.read_text())


if __name__ == "__main__":
    unittest.main()
