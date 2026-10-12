using Silk.NET.SDL;

namespace RecompOne.Runtime.Host;

/// <summary>
/// Extra SDL GameController mappings for pads missing from SDL's built-in database.
/// SDL ignores a joystick without a mapping (IsGameController = false), so such pads
/// gave no input at all.
/// </summary>
public static class GamepadMappings
{
    // PlayStation Classic controller (USB HID 054C:0CDA, #142). Lines from the community
    // SDL_GameControllerDB (also bundled by DuckStation); the button layout matches
    // RetroArch's dinput autoconfig: 0 Triangle, 1 Circle, 2 Cross, 3 Square, 4 L2, 5 R2,
    // 6 L1, 7 R1, 8 Select, 9 Start; D-pad on axes 0/1. The GUID differs per OS
    // (Windows DirectInput, Linux evdev), so adding both is safe.
    static readonly string[] Mappings =
    [
        "030000004c050000da0c000000000000,Sony PlayStation Classic Controller,a:b2,b:b1,back:b8,dpdown:+a1,dpleft:-a0,dpright:+a0,dpup:-a1,leftshoulder:b6,lefttrigger:b4,rightshoulder:b7,righttrigger:b5,start:b9,x:b3,y:b0,",
        "030000004c050000da0c000011010000,Sony PlayStation Classic Controller,a:b2,b:b1,back:b8,dpdown:+a1,dpleft:-a0,dpright:+a0,dpup:-a1,leftshoulder:b6,lefttrigger:b4,rightshoulder:b7,righttrigger:b5,start:b9,x:b3,y:b0,",
    ];

    /// <summary>Call after the GameController subsystem is up and before scanning pads.</summary>
    public static void Add(Sdl sdl)
    {
        foreach (var mapping in Mappings)
        {
            try { sdl.GameControllerAddMapping(mapping); }
            catch { /* never let a bad mapping disable gamepads */ }
        }
    }
}
