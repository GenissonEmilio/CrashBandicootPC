using RecompOne.Runtime.Catalogs;
using RecompOne.Runtime.Config;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Host;

public static partial class FramePacing
{
    /// <summary>
    /// #171 story intro on New Game (ViewConfig.IntroOnNewGame). Retail only plays the
    /// intro (lid 0x38) as attract demo 0: GamOC <c>Title_Willy_Start_Demo</c> sets
    /// GAMESTATE = CUTSCENE and loads it. New Game is GamOC <c>Title_Menu</c> option 0,
    /// GAMESCREEN (c1 <c>title_state</c>) = 15: the title fades out and TitleUpdate
    /// loads the map (title state 15) in the same level.
    ///
    /// At the end of that fade (transition 7, one GpuUpdate before the map loads) the
    /// intro is loaded instead, the demo way. Its WinGC (<c>Intro_Master_Stance</c> at
    /// the end, <c>Intro_Master_Skip</c> on Start/Select) sets GAMESTATE = TITLE and
    /// loads the title level, where TitleLoadNextState would open the title screen
    /// (state 5). GAMESTATE = CUTSCENE at that NSInit makes it open <c>title_state</c>
    /// (still 15) instead, like a return from a level. The main menu already ran
    /// LevelResetGlobals, and the intro touches no progress globals.
    /// </summary>
    const uint LidTitle = 25u;
    const uint LidIntro = 56u;
    /// <summary>c1 ns.c <c>next_lid</c>: GOOL loadlevel writes it, the main loop loads it.</summary>
    const uint NextLidAddr = 0x80056714u;
    /// <summary>c1 globals <c>game_state</c> (24.8; GAME_STATE_CUTSCENE = 0).</summary>
    const uint GameStateAddr = 0x800618D0u;
    const uint GameStateCutscene = 0u;
    /// <summary>c1 globals <c>title_state</c> (gooc GAMESCREEN).</summary>
    const uint TitleStateAddr = 0x800618D4u;
    /// <summary>c1 title.c <c>title</c> (gp+0x2E4): title_struct of the title level.</summary>
    const uint TitlePtrAddr = 0x800566E0u;
    const uint TitleTransitionOff = 0x3598u;
    const uint TitleCurStateOff = 0x35D0u;
    const uint TitleNextStateOff = 0x35D4u;
    const uint TitleMainMenu = 5u;
    const uint TitleMap = 15u;
    const uint TitleFadedOut = 7u;

    static bool _introNewGame;

    /// <summary>PreGpuUpdate: after this loop's TitleUpdate, before the next loop's level load.</summary>
    static void NoteIntroOnNewGame(IMemory m)
    {
        if (!ConfigManager.View.IntroOnNewGame) return;
        try
        {
            if (m.ReadU32(Catalog.LevelIdAddr) != LidTitle
                || m.ReadU32(NextLidAddr) != uint.MaxValue
                || m.ReadU32(TitleStateAddr) != TitleMap)
                return;
            uint title = m.ReadU32(TitlePtrAddr);
            if ((title & 0xFF000000u) != 0x80000000u
                || m.ReadU32(title + TitleCurStateOff) != TitleMainMenu
                || m.ReadU32(title + TitleNextStateOff) != TitleMap
                || m.ReadU32(title + TitleTransitionOff) != TitleFadedOut)
                return;

            _introNewGame = true;
            m.WriteU32(GameStateAddr, GameStateCutscene);
            m.WriteU32(NextLidAddr, LidIntro);
        }
        catch
        {
        }
    }

    /// <summary>
    /// NSInit pre-hook: A1 is the level about to load. cur_lid is already 0 here
    /// (NSKill runs TitleKill, which clears it), so the flag alone says the load
    /// after our intro load is the intro's own exit.
    /// </summary>
    static void IntroOnNewGameLevelLoad(CpuContext c, IMemory m)
    {
        if (!_introNewGame) return;
        uint lid = c.A1;
        if (lid == LidIntro) return;
        _introNewGame = false;
        if (lid != LidTitle) return;
        m.WriteU32(GameStateAddr, GameStateCutscene);
        m.WriteU32(TitleStateAddr, TitleMap);
    }
}
