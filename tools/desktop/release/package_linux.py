#!/usr/bin/env python3
"""Assemble the frozen desktop and self-contained runtime, never generated game code."""
import hashlib
from pathlib import Path
import shutil
import sys
import tarfile

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "tools/desktop"))
from model import VERSION


def main():
    stage = ROOT / "dist/CrashBandicootPC"
    engine = stage / "engine"
    if engine.exists():
        shutil.rmtree(engine)
    shutil.copytree(ROOT / "artifacts/engine", engine)
    shutil.copy2(ROOT / "LICENSE", stage / "LICENSE-engine.txt")
    shutil.copy2(ROOT / "THIRD-PARTY-NOTICES.txt", stage / "THIRD-PARTY-NOTICES.txt")
    shutil.copy2(ROOT / "tools/desktop/LICENSE", stage / "LICENSE-desktop.txt")
    licenses = stage / "licenses"
    licenses.mkdir(exist_ok=True)
    for path in Path("/usr/share/doc").glob("*/copyright"):
        shutil.copy2(path, licenses / (path.parent.name + ".txt"))
    shutil.copy2("/opt/libarchive/COPYING", licenses / "libarchive.txt")
    for path in stage.rglob("*"):
        if path.is_file() and (path.suffix.lower() in (".bin", ".cue", ".img", ".iso", ".chd", ".7z", ".rar", ".nsd", ".nsf", ".sav", ".state")
                               or path.name in ("game.recomp.dll", "main.cs", "Entry.cs", "settings.json")):
            raise SystemExit(f"Refusing game/player data in release: {path}")
    (stage / "LEIA-ME.txt").write_text(f"""CRASH BANDICOOT PC {VERSION} — Linux x86-64

1. Extraia a pasta inteira para um local permanente.
2. Abra CrashBandicootPC com dois cliques.
3. Clique em Escolher arquivo e selecione sua ROM americana SCUS-94900.
   Aceita BIN/IMG, CUE com BIN, ZIP, RAR e 7Z (inclusive formato com nome errado).
4. Aguarde a primeira preparação local. Depois, clique em Iniciar jogo.

Nas próximas vezes, seu disco e o jogo preparado já estarão lembrados.
Sobre o port > Adicionar ao menu de aplicativos instala o atalho no Linux.

Requer Linux x86-64, glibc 2.35+, sessão gráfica e OpenGL 3.3+ ou ES 3.0+.
Não exige .NET, Python ou compilador instalados. Funciona offline.
O pacote inclui o runtime/recompilador. O código do jogo é gerado apenas
no seu computador a partir do seu disco; ele e a ROM não estão no release.

Dados: ~/.local/share/CrashBandicootPC (ou $XDG_DATA_HOME/CrashBandicootPC).
Discos preparados: discs/. Saves, estados e configurações: player/.
A ROM original não é alterada. Para atualizar, recrie o atalho na nova versão.

Controles: setas mover; Z pular/confirmar; X girar; Enter Start.
Esc abre pausa/configurações. F5 salva estado; F8 carrega; F6/F7 trocam slot.
F11 alterna tela cheia. Também há suporte a controles USB/Bluetooth.

https://github.com/GenissonEmilio/CrashBandicootPC
Base: Matteo842/CrashBandicoot-Launcher e RecompOne, sob MIT.
Interface baseada em ForbiddenMemoriesPC. Créditos e licenças preservados.
Crash Bandicoot pertence a seus respectivos titulares.
""", encoding="utf-8")
    archive = ROOT / f"dist/CrashBandicootPC-{VERSION}-linux-x86_64.tar.gz"
    with tarfile.open(archive, "w:gz", compresslevel=6) as bundle:
        bundle.add(stage, arcname="CrashBandicootPC")
    checksum = hashlib.sha256(archive.read_bytes()).hexdigest()
    archive.with_name(archive.name + ".sha256").write_text(checksum + "  " + archive.name + "\n")
    print(archive)


if __name__ == "__main__":
    main()
