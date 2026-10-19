# linux-arm64 native libraries

`publish_release.py --platform linux-arm64` packs these two files into the single-file build:

| File | For | Source |
|---|---|---|
| `libcimgui.so` | ImGui.NET 1.90.8.1 | [cimgui](https://github.com/cimgui/cimgui) tag `1.90.8dock` (commit `7c16d31`) |
| `libnfd.so` | NativeFileDialogSharp 0.5.0 | [milleniumbug/nativefiledialog](https://github.com/milleniumbug/nativefiledialog) commit `258a885` (GTK 3) |

Their NuGet packages ship no linux-arm64 build; every other native dependency does.

Both files come only from this repository's own workflow, `.github/workflows/release.yml` (Actions > **Release** > **Run workflow**). Its first job builds them on `ubuntu-22.04-arm` and checks that they export the same C functions as the linux-x64 libraries in the NuGet packages; the linux-arm64 build then takes them from there. They are not committed.

For a local `publish_release.py --platform linux-arm64`, copy `libcimgui.so` and `libnfd.so` from the `linux-arm64-natives` artifact of a Release run into this folder. `SOURCES.txt` in the artifact records the run, the commits and the SHA-256 of each file.

Never put libraries built by anyone else here.
