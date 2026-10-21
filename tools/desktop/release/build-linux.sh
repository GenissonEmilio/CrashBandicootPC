#!/bin/sh
set -eu
cd -- "$(dirname -- "$0")/../../.."
export DOTNET_PROCESSOR_COUNT=2
dotnet publish CrashBandicoot.Launcher -c Release -f net10.0 -r linux-x64 \
  --self-contained true -p:TargetFrameworks=net10.0 -p:PublishSingleFile=false \
  -p:BuildInParallel=false -m:2 -o artifacts/engine
docker build -t crash-bandicoot-desktop:jammy tools/desktop/release
docker run --rm --cpus=2 -u "$(id -u):$(id -g)" -e HOME=/tmp -v "$PWD:/src:z" -w /src \
  crash-bandicoot-desktop:jammy sh -ec '
    python3 -m PyInstaller --noconfirm --clean --distpath dist --workpath artifacts/freeze tools/desktop/release/launcher.spec
    python3 tools/desktop/release/package_linux.py
  '
