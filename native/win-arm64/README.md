# win-arm64 native libraries

`publish_release.py --platform windows-arm64` packs these two files into the single-file build:

| File | For | Source |
|---|---|---|
| `nfd.dll` | NativeFileDialogSharp 0.5.0 | [milleniumbug/nativefiledialog](https://github.com/milleniumbug/nativefiledialog) commit `258a885` (Win32 COM dialogs) |
| `rabbitizer.dll` | RabbitizerSharp 0.1.0 | [Decompollaborate/rabbitizer](https://github.com/Decompollaborate/rabbitizer) commit `5b95b96` (the submodule of [RabbitizerSharp](https://github.com/flafmg/RabbitizerSharp) `a6b7c22`) |

Their NuGet packages ship no win-arm64 build; every other native dependency (GLFW, SDL, OpenAL Soft, cimgui) does.

Both files come only from this repository's own workflow, `.github/workflows/release.yml` (Actions > **Release** > **Run workflow**). Its win-arm64 job builds them with MSVC on `windows-11-arm`, checks that they export the same functions as the win-x64 libraries in the NuGet packages, and loads them in a native arm64 process; the win-arm64 build then takes them from there. They are not committed.

For a local `publish_release.py --platform windows-arm64`, copy `nfd.dll` and `rabbitizer.dll` from the `win-arm64-natives` artifact of a Release run into this folder. `SOURCES.txt` in the artifact records the run, the commits and the SHA-256 of each file.

Never put libraries built by anyone else here.
