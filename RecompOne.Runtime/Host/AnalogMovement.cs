using RecompOne.Runtime.Config;
using RecompOne.Runtime.Hardware;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Host;

/// <summary>
/// Analog movement (#131). Crash 1 only reads the D-pad: GoolObjectPhysics
/// (c1 GoolObjectControlDir) turns pads[0].held into one of eight move_states
/// entries {dir, angle, speed_scale} and steers to angle + cam_rot_xz.
/// While the left stick is out of its dead zone, the entry the pad selects
/// this frame gets the stick angle for that one physics call and is restored
/// after it. Acceleration, turn limits and camera stay the original code.
/// GoolTestControls only reads .dir, which is never touched.
/// Off (ViewConfig.AnalogMovement): the stick is a D-pad like before.
/// </summary>
internal static class AnalogMovement
{
    // c1 gool.c move_states[16] (NTSC-U 80052B64): {dir, angle, speed_scale}.
    const uint MoveStatesAddr = 0x80052B64u;
    const uint MoveStateSize = 12u;
    const uint MoveAngleOff = 4u;
    const uint MoveScaleOff = 8u;
    const uint DirNone = 8u;
    // pads[0].held; direction bits 0xF000 = up, right, down, left.
    const uint PadHeldAddr = 0x8005E720u;
    const uint GameStateAddr = 0x800618D0u;
    const uint GameStatePlaying = 0x100u;
    // pbak_state: attract demo replays recorded D-pad input.
    const uint PbakStateAddr = 0x80061A30u;
    const uint ObjStatusBOff = 0xCCu;
    const uint FlagDpadControl = 0x80u;

    /// <summary>Stick radius (of full tilt) where the stick starts to steer.</summary>
    const float DeadZone = 0.35f;
    /// <summary>sin 22.5°: eight 45° sectors for the D-pad bits.</summary>
    const float SectorSin = 0.38268343f;
    /// <summary>
    /// D-pad diagonals use speed_scale 0x147, cardinals 0x100. Between them the
    /// scale follows the straight edge of that octagon, so no stick angle moves
    /// faster along any axis than the D-pad already can.
    /// </summary>
    const float CardinalScale = 0x100;
    const float DiagonalScale = 0x147;

    static uint _patchedEntry;
    static uint _savedAngle;
    static uint _savedScale;

    /// <summary>
    /// InputManager.Poll, after every pad source. Presses the D-pad sector the
    /// stick points at, so the game always sees a held direction while the
    /// stick steers (GoolObjectControlDir skips "none") and menus follow it.
    /// </summary>
    public static void ApplyStickDirections()
    {
        if (!ConfigManager.View.AnalogMovement || !TryStick(out float x, out float y, out float len))
            return;
        ushort bits = 0;
        if (x >= SectorSin * len) bits |= Controller.Right;
        else if (x <= -SectorSin * len) bits |= Controller.Left;
        if (y >= SectorSin * len) bits |= Controller.Down;
        else if (y <= -SectorSin * len) bits |= Controller.Up;
        Controller.State = (ushort)(Controller.State & ~bits);
    }

    /// <summary>FramePacing.PrePhysics (8001F30C), every object.</summary>
    public static void PrePhysics(IMemory m, uint obj)
    {
        if (_patchedEntry != 0) return;
        try
        {
            if ((m.ReadU32(obj + ObjStatusBOff) & FlagDpadControl) == 0) return;
            if (!ConfigManager.View.AnalogMovement) return;
            if (m.ReadU32(GameStateAddr) != GameStatePlaying || m.ReadU32(PbakStateAddr) != 0) return;
            if (!TryStick(out float x, out float y, out _)) return;
            uint index = (m.ReadU32(PadHeldAddr) >> 12) & 0xFu;
            uint entry = MoveStatesAddr + index * MoveStateSize;
            if (m.ReadU32(entry) == DirNone) return;

            // move_states angles: down 0, right 0x400, up 0x800, left 0xC00
            // (12-bit, before cam_rot_xz). Stick y is positive down.
            float rad = MathF.Atan2(x, y);
            uint angle = (uint)(int)MathF.Round(rad * (4096f / (2f * MathF.PI))) & 0xFFFu;
            _savedAngle = m.ReadU32(entry + MoveAngleOff);
            _savedScale = m.ReadU32(entry + MoveScaleOff);
            m.WriteU32(entry + MoveAngleOff, angle);
            m.WriteU32(entry + MoveScaleOff, SpeedScale(angle));
            _patchedEntry = entry;
        }
        catch
        {
            // object freed / unmapped
        }
    }

    /// <summary>FramePacing.PostObjectPhysics: put the original entry back.</summary>
    public static void PostPhysics(IMemory m)
    {
        uint entry = _patchedEntry;
        if (entry == 0) return;
        _patchedEntry = 0;
        try
        {
            m.WriteU32(entry + MoveAngleOff, _savedAngle);
            m.WriteU32(entry + MoveScaleOff, _savedScale);
        }
        catch
        {
            // unmapped
        }
    }

    static bool TryStick(out float x, out float y, out float len)
    {
        x = y = len = 0f;
        if (!InputManager.IsConnected && !Controller.PhysicalConnected) return false;
        x = Math.Clamp((Controller.LeftX - 127.5f) / 127.5f, -1f, 1f);
        y = Math.Clamp((Controller.LeftY - 127.5f) / 127.5f, -1f, 1f);
        len = MathF.Sqrt(x * x + y * y);
        return len >= DeadZone;
    }

    static uint SpeedScale(uint angle)
    {
        // Offset from the nearest cardinal, folded to 0..45°.
        uint d = angle & 0x3FFu;
        if (d > 0x200u) d = 0x400u - d;
        float rad = d * (2f * MathF.PI / 4096f);
        float diag = DiagonalScale / CardinalScale * MathF.Sqrt(0.5f);
        float k = (1f - diag) / diag;
        return (uint)MathF.Round(CardinalScale / (MathF.Cos(rad) + k * MathF.Sin(rad)));
    }
}
