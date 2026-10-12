using System.Globalization;
using System.IO.Compression;
using RecompOne.Runtime.Bios;
using RecompOne.Runtime.Catalogs;
using RecompOne.Runtime.Config;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Diagnostics;
using RecompOne.Runtime.Host.Cheats;
using RecompOne.Runtime.Host.Window;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Sdk;

namespace RecompOne.Runtime.Host;

/// <summary>
/// Quick save / quick load (#130). Recompiled code cannot resume at an
/// arbitrary PC: the C# call stack is the MIPS return chain. Both run only at
/// the end of the GpuUpdate that CoreLoop calls once per frame, where that
/// chain never changes, so registers + RAM + devices resume the saved frame.
/// Requests from the pause menu / hotkeys wait for that point (same frame).
/// </summary>
public static class SaveStates
{
    public const int SlotCount = 10;

    const uint Magic = 0x53534243u; // "CBSS"
    const int Version = 1;
    const int MaxRawBytes = 64 << 20;

    /// <summary>
    /// CoreLoop (func_80011FC4) calls GpuUpdate from 0x80012508 every frame and
    /// GpuUpdate restores RA before it returns. 0x80012534 / 0x8001253C are the
    /// shutdown path.
    /// </summary>
    const uint CoreLoopGpuUpdateRa = 0x80012510u;

    static int _saveSlot;
    static int _loadSlot;
    static Task _writes = Task.CompletedTask;
    static readonly object _writeGate = new();
    static readonly Dictionary<int, SlotInfo?> _infoCache = [];

    public readonly record struct SlotInfo(uint LevelId, DateTime SavedUtc, bool Compatible)
    {
        public string Describe()
        {
            if (!Compatible) return "Other version";
            string level = Catalog.Levels.TryGet(LevelId, out var info) ? info.Name : $"Level {LevelId}";
            return $"{level} · {SavedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}";
        }
    }

    /// <summary>Selected slot, 1–<see cref="SlotCount"/> (kept in settings).</summary>
    public static int Slot
    {
        get => ConfigManager.View.SaveStateSlot;
        set
        {
            if (ConfigManager.View.SaveStateSlot == value) return;
            ConfigManager.View.SaveStateSlot = value;
            Runtime.SaveView();
        }
    }

    public static void RequestSave(int slot) => _saveSlot = Math.Clamp(slot, 1, SlotCount);

    public static void RequestLoad(int slot) => _loadSlot = Math.Clamp(slot, 1, SlotCount);

    /// <summary>New game session: a request from the last one must not fire here.</summary>
    public static void CancelRequests() => _saveSlot = _loadSlot = 0;

    /// <summary>Steps the selected slot (wraps) and shows what it holds.</summary>
    public static void StepSlot(int delta)
    {
        Slot = (Slot - 1 + delta + SlotCount * 2) % SlotCount + 1;
        DevHudOverlay.Flash($"Slot {Slot} · {GetInfo(Slot)?.Describe() ?? "Empty"}");
    }

    /// <summary>Header of a slot file, or null when the slot is empty.</summary>
    public static SlotInfo? GetInfo(int slot)
    {
        lock (_infoCache)
            if (_infoCache.TryGetValue(slot, out var cached))
                return cached;

        SlotInfo? info = null;
        try
        {
            string path = SlotPath(slot);
            if (File.Exists(path))
            {
                using var fs = File.OpenRead(path);
                using var br = new BinaryReader(fs);
                info = ReadHeader(br, out _);
            }
        }
        catch (Exception ex)
        {
            SessionLog.Warn($"save state slot {slot} header: {ex.Message}");
            info = new SlotInfo(0, DateTime.MinValue, false);
        }
        lock (_infoCache) _infoCache[slot] = info;
        return info;
    }

    static string SlotPath(int slot) => Path.Combine(AppPaths.StatesDir, $"slot{slot}.state");

    /// <summary>FramePacing.PostGpuUpdate, every GpuUpdate.</summary>
    internal static void AtSafePoint(CpuContext c, IMemory m)
    {
        if ((_saveSlot | _loadSlot) == 0) return;
        if (c.RA != CoreLoopGpuUpdateRa || m is not PSMemory psm) return;
        int save = _saveSlot, load = _loadSlot;
        _saveSlot = _loadSlot = 0;
        if (save != 0) Save(save, c, psm);
        if (load != 0) Load(load, c, psm);
    }

    static void Save(int slot, CpuContext c, PSMemory psm)
    {
        if (BiosA.CardFileOpen)
        {
            DevHudOverlay.Flash("Memory card busy, state not saved");
            return;
        }
        if (LibCdStream.InUse)
        {
            DevHudOverlay.Flash("Can't save a state here");
            return;
        }

        byte[] raw;
        try
        {
            raw = Capture(c, psm);
        }
        catch (Exception ex)
        {
            SessionLog.Exception($"save state slot {slot}", ex);
            DevHudOverlay.Flash("Save state failed");
            return;
        }

        var info = new SlotInfo(psm.ReadU32(Catalog.LevelIdAddr), DateTime.UtcNow, true);
        // Deflate + disk on a worker: the game thread only paid for the copy.
        lock (_writeGate)
            _writes = _writes.ContinueWith(_ => WriteSlot(slot, raw, info), TaskScheduler.Default);
    }

    static void WriteSlot(int slot, byte[] raw, SlotInfo info)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.StatesDir);
            string path = SlotPath(slot);
            string tmp = path + ".tmp";
            using (var fs = File.Create(tmp))
            {
                using (var bw = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    bw.Write(Magic);
                    bw.Write(Version);
                    bw.Write(info.LevelId);
                    bw.Write(info.SavedUtc.ToBinary());
                    bw.Write(raw.Length);
                }
                using var z = new ZLibStream(fs, CompressionLevel.Fastest);
                z.Write(raw);
            }
            File.Move(tmp, path, overwrite: true);
            lock (_infoCache) _infoCache[slot] = info;
            SessionLog.Info($"save state slot {slot} saved ({raw.Length} bytes, level {info.LevelId})");
            DevHudOverlay.Flash($"State saved · slot {slot}");
        }
        catch (Exception ex)
        {
            SessionLog.Exception($"save state slot {slot} write", ex);
            DevHudOverlay.Flash("Save state failed");
        }
    }

    static void Load(int slot, CpuContext c, PSMemory psm)
    {
        Task pending;
        lock (_writeGate) pending = _writes;
        pending.Wait();

        byte[] raw;
        try
        {
            if (!File.Exists(SlotPath(slot)))
            {
                DevHudOverlay.Flash($"Slot {slot} is empty");
                return;
            }
            raw = ReadSlot(slot);
        }
        catch (Exception ex)
        {
            SessionLog.Exception($"save state slot {slot} read", ex);
            DevHudOverlay.Flash(ex is InvalidDataException ? $"Slot {slot} can't be loaded" : "Load state failed");
            return;
        }

        // Known-good copy of this frame: a bad file must not leave half a machine.
        byte[] backup;
        try
        {
            backup = Capture(c, psm);
        }
        catch (Exception ex)
        {
            SessionLog.Exception($"save state slot {slot} backup", ex);
            DevHudOverlay.Flash("Load state failed");
            return;
        }
        try
        {
            Restore(raw, c, psm);
            SessionLog.Info($"save state slot {slot} loaded (level {psm.ReadU32(Catalog.LevelIdAddr)})");
            DevHudOverlay.Flash($"State loaded · slot {slot}");
        }
        catch (Exception ex)
        {
            SessionLog.Exception($"save state slot {slot} restore", ex);
            Restore(backup, c, psm);
            DevHudOverlay.Flash($"Slot {slot} can't be loaded");
        }
        AfterLoad();
    }

    static byte[] Capture(CpuContext c, PSMemory psm)
    {
        using var ms = new MemoryStream(4 << 20);
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            c.SaveState(w);
            psm.SaveState(w);
            Gte.SaveState(w);
            BiosA.SaveState(w);
            BiosB.SaveState(w);
            LibCd.SaveState(w);
            LibPad.SaveState(w);
            w.Write(Magic);
        }
        return ms.ToArray();
    }

    static void Restore(byte[] raw, CpuContext c, PSMemory psm)
    {
        using var ms = new MemoryStream(raw, writable: false);
        using var r = new BinaryReader(ms);
        c.LoadState(r);
        psm.LoadState(r);
        Gte.LoadState(r);
        BiosA.LoadState(r);
        BiosB.LoadState(r);
        LibCd.LoadState(r);
        LibPad.LoadState(r);
        if (r.ReadUInt32() != Magic || ms.Position != ms.Length)
            throw new InvalidDataException("save state layout does not match");
    }

    /// <summary>Host state that described the replaced RAM.</summary>
    static void AfterLoad()
    {
        FramePacing.OnStateLoaded();
        CheatManager.CancelWarp();
        ExitToMapInjector.Cancel();
    }

    static byte[] ReadSlot(int slot)
    {
        using var fs = File.OpenRead(SlotPath(slot));
        int rawLength;
        using (var br = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            var info = ReadHeader(br, out rawLength);
            if (!info.Compatible)
                throw new InvalidDataException("save state from another version");
        }
        var raw = new byte[rawLength];
        using var z = new ZLibStream(fs, CompressionMode.Decompress);
        z.ReadExactly(raw);
        return raw;
    }

    static SlotInfo ReadHeader(BinaryReader br, out int rawLength)
    {
        if (br.ReadUInt32() != Magic)
            throw new InvalidDataException("not a save state");
        int version = br.ReadInt32();
        uint level = br.ReadUInt32();
        var saved = DateTime.FromBinary(br.ReadInt64());
        rawLength = br.ReadInt32();
        if (rawLength is <= 0 or > MaxRawBytes)
            throw new InvalidDataException($"bad save state size {rawLength}");
        return new SlotInfo(level, saved, version == Version);
    }

    internal static void ReadExactly(BinaryReader r, Span<byte> dst)
    {
        while (!dst.IsEmpty)
        {
            int n = r.Read(dst);
            if (n <= 0) throw new EndOfStreamException("save state is truncated");
            dst = dst[n..];
        }
    }
}
