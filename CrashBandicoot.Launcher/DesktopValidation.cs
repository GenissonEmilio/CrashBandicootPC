using System.Reflection;
using System.Text.Json;
using MonoMod.RuntimeDetour;
using RecompOne.Runtime;
using RecompOne.Runtime.Config;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Hardware;
using RecompOne.Runtime.Hle;
using RecompOne.Runtime.Host;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Sdk;
using StbImageWriteSharp;

namespace CrashBandicoot.Launcher;

/// <summary>Opt-in local gameplay check; generated code, disc and output stay local.</summary>
internal static class DesktopValidation
{
    public static int Run(string dll, string disc, string output, uint? level)
    {
        Directory.CreateDirectory(output);
        AppPaths.SetRoot(Path.Combine(output, "state"));
        AppPaths.EnsureCreated();
        ConfigManager.Load();
        ConfigManager.Game.CdPath = disc;
        ConfigManager.Game.Muted = true;
        ConfigManager.View.Fullscreen = false;
        ConfigManager.View.Widescreen = false;
        ConfigManager.View.InternalResolution = 1;
        ConfigManager.View.FrameRate = 30;
        ConfigManager.View.SaveStates = true;
        ConfigManager.SaveGame();
        ConfigManager.SaveView([]);
        int frames = 0, captures = 0, nextSample = 0;
        bool booted = false;
        bool gameplay = level == 9;
        uint observedLevel = 0;
        int[] saved = [], displaced = [], restored = [];
        var samples = new List<int[]>();
        string? error = null;
        using var limit = new Timer(_ => Environment.Exit(3), null, TimeSpan.FromSeconds(120), Timeout.InfiniteTimeSpan);
        using var boot = new Hook(typeof(FramePacing).GetMethod(nameof(FramePacing.PreNsInit))!,
            (Func<Func<CpuContext, IMemory, bool>, CpuContext, IMemory, bool>)((original, c, m) =>
            {
                if (!booted && level.HasValue) c.A1 = level.Value;
                booted = true;
                return original(c, m);
            }));
        Event.AddListener<VSyncEvent>(_ => frames++);
        Event.AddListener<PadReadEvent>(e =>
        {
            if (e.Port != 0) return;
            ushort buttons = 0;
            if (gameplay && frames is >= 200 and < 400 or >= 500 and < 560) buttons |= Controller.Up;
            if (gameplay && frames is >= 280 and < 310) buttons |= Controller.Cross;
            if (gameplay && frames is >= 320 and < 340) buttons |= Controller.Circle;
            e.Buttons = (ushort)~buttons;
        });
        using var draw = new Hook(typeof(LibGpu).GetMethod(nameof(LibGpu.DrawOTag))!,
            (Action<Action<CpuContext, IMemory>, CpuContext, IMemory>)((original, c, m) =>
            {
                original(c, m);
                observedLevel = m.ReadU32(RecompOne.Runtime.Catalogs.Catalog.LevelIdAddr);
                var position = Position(m);
                if (frames >= nextSample && position.Length == 3)
                { samples.Add(position); nextSample = frames + 30; }
                if (gameplay && frames >= 450 && saved.Length == 0 && position.Length == 3)
                {
                    saved = position; SaveStates.RequestSave(1);
                }
                if (gameplay && frames >= 590 && displaced.Length == 0 && position.Length == 3) displaced = position;
                if (gameplay && frames >= 600 && frames < 605) SaveStates.RequestLoad(1);
                if (gameplay && frames >= 660 && restored.Length == 0 && position.Length == 3) restored = position;
                if (GpuHle.Backend is GlBackend backend && frames >= 240 + captures * 250)
                {
                    Capture(backend, Path.Combine(output, $"frame-{captures}.png")); captures++;
                }
                if (frames >= 800) throw new GameSessionEndedException();
            }));
        try { Recomp.GameLoader.Run(dll, disc); }
        catch (Exception ex) { error = ex.ToString(); }
        finally { Runtime.Shutdown(); }
        long Distance(int[] a, int[] b) => a.Length == 3 && b.Length == 3
            ? Math.Abs((long)a[0] - b[0]) + Math.Abs((long)a[2] - b[2]) : 0;
        bool moved = samples.Count > 3 && samples.Any(p => Distance(p, samples[0]) > 512);
        bool stateWritten = File.Exists(Path.Combine(AppPaths.StatesDir, "slot1.state"));
        bool stateRestored = Distance(saved, displaced) > 512 && Distance(saved, restored) < Distance(saved, displaced) / 2;
        bool passed = error == null && captures >= 2 && booted && observedLevel == level &&
                      (!gameplay || (moved && stateWritten && stateRestored));
        File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
        { passed, level, observedLevel, frames, captures, scope=gameplay?"movement-input-save-load":"level-boot-render",
          moved, stateWritten, stateRestored, saved, displaced, restored, samples, error },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Desktop gameplay check: {(passed ? "PASS" : "FAIL")} ({output})");
        return passed ? 0 : 1;
    }

    private static int[] Position(IMemory memory)
    {
        uint pointer = memory.ReadU32(0x800566B4u);
        return (pointer & 0xFF000000u) == 0x80000000u
            ? [(int)memory.ReadU32(pointer + 0x80), (int)memory.ReadU32(pointer + 0x84), (int)memory.ReadU32(pointer + 0x88)] : [];
    }

    private static void Capture(GlBackend backend, string path)
    {
        var env = Runtime.Gpu!.CurrentHleDrawEnv;
        backend.PresentDisplay(env.ClipX0, env.ClipY0, env.ClipX1 - env.ClipX0 + 1, env.ClipY1 - env.ClipY0 + 1);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        int width = (int)typeof(GlBackend).GetField("_presentW", flags)!.GetValue(backend)!;
        int height = (int)typeof(GlBackend).GetField("_presentH", flags)!.GetValue(backend)!;
        var pixels = new int[width * height];
        if (!backend.ReadPresentArgb(pixels)) throw new IOException("Framebuffer could not be read");
        var rgba = new byte[pixels.Length * 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            rgba[i * 4] = (byte)(pixels[i] >> 16); rgba[i * 4 + 1] = (byte)(pixels[i] >> 8);
            rgba[i * 4 + 2] = (byte)pixels[i]; rgba[i * 4 + 3] = 255;
        }
        using var stream = File.Create(path);
        new ImageWriter().WritePng(rgba, width, height, ColorComponents.RedGreenBlueAlpha, stream);
    }
}
