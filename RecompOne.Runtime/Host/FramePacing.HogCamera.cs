using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Host;

/// <summary>
/// Hog Wild / Whole Hog run Crash at a fixed speed, so the original ties
/// every spawn-timed obstacle to his progress: a rotisserie starts its
/// spit cycle when its zone spawns, and the third one in Whole Hog only
/// leaves a ~230 ms takeoff window at that exact phase (#140).
/// Unlocked, CamFollow ran every present: CamAdjustProgress picks its
/// catch-up step from the current gap (delta/2 below 0x200, then 0x200),
/// and finer steps settle closer to Crash; it also sampled him half a
/// frame later. The camera ran ~0.9 frames ahead at 60 FPS and ~1.3 at
/// 120, and the zone switch spawned the next obstacles that much early.
/// Newborns then got their first update one present after the switch
/// instead of one frame. So while Crash rides the hog: CamFollow runs as
/// an original 34-tick step only on presents where the 30 Hz gate frame
/// advances, gated newborns wait for the next gate frame, and the view
/// lerps the last two 30 Hz camera poses.
/// </summary>
public static partial class FramePacing
{
    /// <summary>NTSC-U <c>cam_trans</c> / <c>cam_rot</c>. GfxUpdateMatrices copies them for objects.</summary>
    const uint CamTransAddr = 0x80057864u;
    const uint CamRotAddr = 0x80057870u;
    /// <summary>Hog camera moves ~40k per 30 Hz step; a respawn or path snap is far more.</summary>
    const int HogCamPoseTeleport = 0x80000;
    const int HogCamRotTeleport = 0x200;

    static bool _hogGateReady;
    static bool _hogGateBoundary;
    static uint _hogGateFrame = uint.MaxValue;
    static bool _hogCamFullStep;
    static bool _hogPoseValid;
    static bool _hogPosePatched;
    static readonly int[] _hogPoseFrom = new int[6];
    static readonly int[] _hogPoseTo = new int[6];
    static readonly int[] _hogPoseSaved = new int[6];
    static readonly int[] _hogPoseShown = new int[6];

    /// <summary>Unlocked, unpaused, Crash on the warthog (not the death cine).</summary>
    static bool HogCamSyncOn(IMemory m)
    {
        if (!IsActive(m) || GamePaused(m) || _exactTicks >= RefTicks - 0.01) return false;
        return TryReadCrash(m, out uint crash) && CrashOnWarthog(m, crash);
    }

    /// <summary>
    /// This present starts a new 30 Hz gate frame: the same test gated
    /// objects use (GatedShouldInterpret), taken once per game loop.
    /// </summary>
    static bool HogGateBoundary(IMemory m)
    {
        if (_hogGateReady) return _hogGateBoundary;
        EnsureFrameTime(m);
        uint frame = CurrentGateFrame(out _);
        _hogGateBoundary = frame != _hogGateFrame;
        _hogGateFrame = frame;
        _hogGateReady = true;
        return _hogGateBoundary;
    }

    static void ClearHogCam()
    {
        _hogGateReady = false;
        _hogGateFrame = uint.MaxValue;
        _hogCamFullStep = false;
        _hogPoseValid = false;
        // A patched pose stays restorable: Reset may run off the game thread.
    }

    /// <summary>
    /// PreCamFollow on Crash. False skips CamFollow this present; true with
    /// <see cref="_hogCamFullStep"/> runs it unscaled (seeks, catch-up, LevelUpdate).
    /// </summary>
    static bool HogCamFollow(IMemory m, uint obj, out bool run)
    {
        run = true;
        if (!TryReadCrash(m, out uint crash) || obj != crash) return false;
        if (!HogCamSyncOn(m))
        {
            _hogPoseValid = false;
            return false;
        }
        if (!HogGateBoundary(m))
        {
            run = false;
            return true;
        }
        try
        {
            ReadCamPose(m, _hogPoseFrom);
        }
        catch
        {
            return false;
        }
        _hogCamFullStep = true;
        return true;
    }

    static void EndHogCamFollow(IMemory m)
    {
        _hogCamFullStep = false;
        try
        {
            ReadCamPose(m, _hogPoseTo);
            _hogPoseValid = true;
        }
        catch
        {
            _hogPoseValid = false;
        }
    }

    /// <summary>
    /// Gated newborn while the hog camera is on the 30 Hz grid: the zone
    /// spawned it one present after the switch, the original one frame
    /// after. Its first update waits for the next gate frame.
    /// </summary>
    static bool DeferHogNewborn(IMemory m) =>
        _newObj && HogCamSyncOn(m) && !HogGateBoundary(m);

    /// <summary>
    /// Before GfxUpdateMatrices: lerp the last two 30 Hz poses, sampled at
    /// the end of this present (60 FPS: t = 1/2, then the step's own pose),
    /// like the mesh lerp. Extrapolating past the step overshot whenever the
    /// catch-up speed changed and snapped back on the next step. Real pose
    /// comes back before objects run.
    /// </summary>
    static void ApplyHogCamPose(IMemory m)
    {
        RestoreHogCamPose(m);
        if (!_hogPoseValid || !HogCamSyncOn(m)) return;
        try
        {
            CurrentGateFrame(out double phase);
            double s = Math.Clamp((phase + _exactTicks) / RefTicks, 0, 1);
            if (s >= 1) return;
            ReadCamPose(m, _hogPoseSaved);
            for (int i = 0; i < 6; i++)
                if (_hogPoseSaved[i] != _hogPoseTo[i]) return; // pose moved since the step
            for (int i = 0; i < 3; i++)
            {
                long d = (long)_hogPoseTo[i] - _hogPoseFrom[i];
                if (d > HogCamPoseTeleport || d < -HogCamPoseTeleport) return;
                _hogPoseShown[i] = _hogPoseTo[i] - (int)Math.Round(d * (1 - s));
            }
            for (int i = 3; i < 6; i++)
            {
                int d = AngDelta(_hogPoseFrom[i], _hogPoseTo[i]);
                if (d > HogCamRotTeleport || d < -HogCamRotTeleport) return;
                _hogPoseShown[i] = _hogPoseTo[i] - (int)Math.Round(d * (1 - s));
            }
            WriteCamPose(m, _hogPoseShown);
            _hogPosePatched = true;
        }
        catch
        {
            // overlay swap
        }
    }

    static void RestoreHogCamPose(IMemory m)
    {
        if (!_hogPosePatched) return;
        _hogPosePatched = false;
        try
        {
            WriteCamPose(m, _hogPoseSaved);
        }
        catch
        {
            // overlay swap
        }
    }

    static void ReadCamPose(IMemory m, int[] pose)
    {
        for (int i = 0; i < 3; i++)
        {
            pose[i] = (int)m.ReadU32(CamTransAddr + (uint)i * 4u);
            pose[3 + i] = (int)m.ReadU32(CamRotAddr + (uint)i * 4u);
        }
    }

    static void WriteCamPose(IMemory m, int[] pose)
    {
        for (int i = 0; i < 3; i++)
        {
            m.WriteU32(CamTransAddr + (uint)i * 4u, (uint)pose[i]);
            m.WriteU32(CamRotAddr + (uint)i * 4u, (uint)pose[3 + i]);
        }
    }
}
