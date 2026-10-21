"""Crash launcher state/process boundary, adapted from ForbiddenMemoriesPC (MIT)."""
from __future__ import annotations
import hashlib
import json
import os
import shutil
from pathlib import Path
import subprocess
import sys
import tempfile
from import_disc import import_disc, inspect_disc

VERSION = "0.1.0"
SOURCE_ROOT = Path(__file__).resolve().parents[2]
APPLICATION_ID = "io.github.GenissonEmilio.CrashBandicootPC"


def assets_directory():
    return Path(getattr(sys, "_MEIPASS", Path(__file__).parent)) / "assets"


def program_directory():
    return Path(sys.executable).resolve().parent if getattr(sys, "frozen", False) else SOURCE_ROOT


def data_directory():
    base = Path(os.environ.get("XDG_DATA_HOME", str(Path.home() / ".local/share"))).expanduser()
    if not base.is_absolute():
        base = Path.home() / ".local/share"
    return base / "CrashBandicootPC"


def atomic_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, temporary = tempfile.mkstemp(prefix=".launcher-", dir=path.parent)
    try:
        with os.fdopen(fd, "w") as stream:
            json.dump(value, stream, ensure_ascii=False, indent=2)
            stream.write("\n")
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


class Library:
    def __init__(self, directory=None, program=None):
        self.directory = Path(directory or data_directory()).resolve()
        self.program = Path(program or program_directory()).resolve()
        self.config = self.directory / "launcher.json"
        self.state = {}
        try:
            state = json.loads(self.config.read_text())
            if isinstance(state, dict):
                self.state = state
        except (OSError, ValueError):
            pass

    @property
    def disc(self):
        path = self.state.get("disc")
        return Path(path) if isinstance(path, str) and Path(path).is_file() else None

    @property
    def player_directory(self):
        return self.directory / "player"

    def engine(self):
        for path in (self.program / "engine/CrashBandicoot", self.program / "artifacts/engine/CrashBandicoot"):
            if path.is_file():
                return path
        raise ValueError("O runtime não foi encontrado. Extraia a pasta inteira do release.")

    def game_environment(self, test=False):
        environment = dict(os.environ)
        original_ld = environment.get("LD_LIBRARY_PATH_ORIG")
        for name in ("LD_LIBRARY_PATH", "LD_LIBRARY_PATH_ORIG", "GI_TYPELIB_PATH", "GTK_DATA_PREFIX",
                     "GTK_EXE_PREFIX", "GDK_PIXBUF_MODULE_FILE", "GDK_PIXBUF_MODULEDIR", "GIO_EXTRA_MODULES",
                     "FONTCONFIG_FILE", "DOTNET_ROOT", "DOTNET_ROOT_X64"):
            environment.pop(name, None)
        if original_ld:
            environment["LD_LIBRARY_PATH"] = original_ld
        environment["CRASH_PC_DATA_DIR"] = str(self.player_directory)
        environment["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1"
        environment.setdefault("DOTNET_PROCESSOR_COUNT", "2")
        environment.setdefault("LP_NUM_THREADS", "2")
        return environment

    def prepare(self, source, progress=lambda text: None):
        source = Path(source).expanduser().resolve()
        if not source.is_file():
            raise ValueError("O arquivo escolhido não foi encontrado.")
        stamp = f"{source}\0{source.stat().st_size}\0{source.stat().st_mtime_ns}"
        identifier = hashlib.sha256(stamp.encode()).hexdigest()[:20]
        destination = self.directory / "discs" / identifier
        cue = destination / "crash.cue"
        progress("Lendo e validando o disco…")
        valid = False
        if cue.is_file():
            try:
                inspect_disc(cue.with_suffix(".bin"))
                valid = True
            except (ValueError, OSError):
                # This directory is an application-owned disc cache. The
                # selected source and all player saves are elsewhere.
                shutil.rmtree(destination)
        if not valid:
            cue = import_disc(source, destination)
        inspect_disc(cue.with_suffix(".bin"))
        progress("Preparando o jogo a partir do seu disco. Só é necessário na primeira vez…")
        self.directory.mkdir(parents=True, exist_ok=True)
        log = self.directory / "last-prepare.log"
        with log.open("w") as output:
            result = subprocess.run([str(self.engine()), "--prepare", str(cue)], cwd=self.program,
                                    env=self.game_environment(), stdout=output, stderr=subprocess.STDOUT,
                                    timeout=900)
        if result.returncode:
            raise ValueError(f"A preparação falhou. Os detalhes estão em:\n{log}")
        state = {"disc": str(cue), "name": source.name, "serial": "SCUS-94900"}
        atomic_json(self.config, state)
        self.state = state
        return cue

    def verify(self):
        cue = self.disc
        if not cue:
            raise ValueError("Escolha sua ROM para continuar.")
        inspect_disc(cue.with_suffix(".bin"))
        return cue

    def launch(self, test=False):
        cue = self.verify()
        self.directory.mkdir(parents=True, exist_ok=True)
        command = [str(self.engine()), "--run", str(cue)]
        if test:
            command = [str(self.engine()), "--desktop-test", str(cue), str(self.directory / "game-test")]
        with (self.directory / "last-game.log").open("w") as output:
            return subprocess.Popen(command, cwd=self.program, env=self.game_environment(test),
                                    stdout=output, stderr=subprocess.STDOUT, start_new_session=True)

    def install_desktop_entry(self, applications=None):
        destination = Path(applications) if applications else Path(
            os.environ.get("XDG_DATA_HOME", str(Path.home() / ".local/share"))) / "applications"
        destination.mkdir(parents=True, exist_ok=True)
        command = [str(Path(sys.executable).resolve())] if getattr(sys, "frozen", False) else [
            sys.executable, str(SOURCE_ROOT / "tools/desktop/main.py")]
        def quote(value):
            return '"' + value.replace('%', '%%').replace('\\', '\\\\').replace('"', '\\"').replace('`', '\\`').replace('$', '\\$') + '"'
        path = destination / (APPLICATION_ID + ".desktop")
        path.write_text("[Desktop Entry]\nType=Application\nName=Crash Bandicoot PC\n"
                        "Comment=Sua próxima aventura começa aqui\nTerminal=false\nCategories=Game;ActionGame;\n"
                        f"Exec={' '.join(map(quote, command))}\nIcon={assets_directory() / 'icon.png'}\n"
                        "StartupWMClass=crash-bandicoot-pc\n")
        path.chmod(0o755)
        return path
