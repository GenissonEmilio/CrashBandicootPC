using RecompOne.Runtime.Config;
using RecompOne.Runtime.Catalogs;

namespace RecompOne.Runtime.Host;

/// <summary>
/// Box counter (#131): boxes broken in the current level against the count the
/// level's gem needs. Read-only, the game is not changed. Off by default
/// (ViewConfig.BoxCounter); the desktop overlay and the Android HUD draw it.
/// </summary>
public static class BoxCounter
{
    /// <param name="Lost">A death after boxes were broken before the last checkpoint:
    /// Crash 1 respawns with the counter cleared and those boxes stay broken, so this
    /// run can't get the gem.</param>
    public readonly record struct Reading(int Broken, int Total, bool Lost)
    {
        public bool Complete => Broken >= Total;
    }

    // c1 globals.h box_count (GOOL BOXCOUNT, 24.8 fixed point): BoxsC BoxBreak adds 1.0,
    // LevelInitMisc (0x800262DC) clears it on every level load and death, LevelRestart
    // restores the saved copy only when returning from a bonus round.
    const uint BoxCountAddr = 0x80061984u;
    // c1 LevelRestart: += 1.0 on every death respawn, never on a bonus return.
    const uint RespawnCountAddr = 0x800618A0u;
    // c1 level.c savestate._box_count (level_state 0x80057974 + 0x4F8): the count at the
    // last checkpoint (BoxsC Box_Continue_Open saves before counting its own box), bonus
    // entry or level start. A death clears box_count without restoring it; the boxes
    // after the checkpoint respawn and the checkpoint box counts itself again.
    const uint SavedBoxCountAddr = 0x80057E6Cu;
    const uint GameStateAddr = 0x800618D0u;
    // Attract demo playback (c1 pbak.c); the demos run real levels.
    const uint PbakStateAddr = 0x80061A30u;

    // Gameplay leaves game_state at 0 (IsldC) or PLAYING; these are the screens with
    // no level running: game over, continue, new gem, title.
    const uint StateGameOver = 0x200u, StateContinue = 0x300u, StateNewGem = 0x500u, StateTitle = 0x600u;

    // goolstdlib LEVEL_BonusTawna1 'A', LEVEL_BonusBrio 'B', LEVEL_BonusTawna2 'P', LEVEL_BonusCortex 'Q'.
    static bool IsBonus(uint lid) => lid is 36 or 37 or 51 or 52;

    // NTSC-U GamOC Level_End_Willy_Spawn (the #ifnreg "ntsc-j" table): PREVBOXCOUNT must
    // equal this for the gem. Keyed by level ID (levels.scus94900.json).
    static readonly Dictionary<uint, int> Totals = new()
    {
        [9] = 49,   // N. Sanity Beach
        [12] = 38,  // Jungle Rollers
        [18] = 26,  // The Great Gate
        [17] = 24,  // Hog Wild
        [30] = 24,  // Whole Hog
        [26] = 42,  // Native Fortress
        [15] = 14,  // Upstream
        [21] = 46,  // Rolling Stones
        [14] = 16,  // Boulders
        [19] = 35,  // Boulder Dash
        [24] = 15,  // Up The Creek
        [28] = 67,  // Temple Ruins
        [32] = 26,  // The Lost City
        [35] = 50,  // Sunset Vista
        [29] = 65,  // Jaws of Darkness
        [3] = 41,   // Cortex Power
        [6] = 33,   // Heavy Machinery
        [5] = 44,   // Generator Room
        [7] = 26,   // Toxic Waste
        [20] = 33,  // Road to Nowhere
        [22] = 18,  // The High Road
        [40] = 15,  // Lights Out
        [34] = 34,  // Stormy Ascent
        [42] = 18,  // Fumbling in the Dark
        [46] = 31,  // Slippery Climb
        [55] = 24,  // Castle Machinery
        [41] = 24,  // The Lab
    };

    const uint NoLevel = uint.MaxValue;
    static uint _level = NoLevel;
    static uint _respawns;
    static bool _lost;

    /// <summary>Boxes the gem of level <paramref name="lid"/> needs.</summary>
    internal static bool TryGetTotal(uint lid, out int total) => Totals.TryGetValue(lid, out total);

    /// <summary>Forget the current run (a save state was loaded).</summary>
    public static void Reset() => _level = NoLevel;

    /// <summary>
    /// Current level's counter, or false when there is none to show: setting off,
    /// no box gem in this level, bonus round, demo, menus.
    /// </summary>
    public static bool TryRead(out Reading reading)
    {
        reading = default;
        return ConfigManager.View.BoxCounter && TryReadRun(out reading);
    }

    /// <summary>
    /// <see cref="TryRead"/> without the setting: gem hints follow the same run
    /// (and its <see cref="Reading.Lost"/>) when the counter is hidden.
    /// </summary>
    internal static bool TryReadRun(out Reading reading)
    {
        reading = default;
        var mem = Runtime.Mem;
        if (mem == null) return false;

        uint lid, count, respawns, saved, state, pbak;
        try
        {
            lid = mem.ReadU32(Catalog.LevelIdAddr);
            count = mem.ReadU32(BoxCountAddr);
            respawns = mem.ReadU32(RespawnCountAddr);
            saved = mem.ReadU32(SavedBoxCountAddr);
            state = mem.ReadU32(GameStateAddr);
            pbak = mem.ReadU32(PbakStateAddr);
        }
        catch
        {
            return false;
        }

        int broken = (int)(count >> 8);
        if (!Totals.TryGetValue(lid, out int total))
        {
            // A bonus round belongs to the level's run; the map, title and level end close it.
            if (!IsBonus(lid)) _level = NoLevel;
            return false;
        }

        if (lid != _level)
        {
            _level = lid;
            _respawns = respawns;
            _lost = false;
        }
        else if (respawns != _respawns)
        {
            _respawns = respawns;
            if ((int)saved > 0) _lost = true;
        }

        if (pbak != 0 || state is StateGameOver or StateContinue or StateNewGem or StateTitle)
            return false;
        reading = new Reading(broken, total, _lost);
        return true;
    }
}
