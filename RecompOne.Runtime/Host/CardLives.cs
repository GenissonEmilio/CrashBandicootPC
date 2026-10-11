using RecompOne.Runtime.Config;
using RecompOne.Runtime.Diagnostics;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Host;

/// <summary>
/// Crash 2/3-style lives on the memory card (#21). Crash 1 saves init_life_count
/// (always 4) and its load runs LevelResetGlobals (life_count = init_life_count)
/// before restoring it from the block, so every load starts with 4 lives.
/// The BIOS write keeps the current lives in two unused words of the save block
/// and re-signs it; the load read puts them in init_life_count just before that
/// reset copies it. The original game ignores the words and still accepts the block.
/// Off (ViewConfig.SaveLives): saves and loads behave like the original game.
/// </summary>
internal static class CardLives
{
    // c1 psx/card.c: saves are bu00:BASCUS-94900mmMMMMMM — frame 0 header,
    // frames 1–3 icon, frame 4 the 128-byte card_globals block.
    const string SaveNamePrefix = "BASCUS-94900";
    const int BlockPos = 0x200;
    const int BlockSize = 0x80;
    // card_globals: unused[21] starts at +0x28, checksum at +0x7C.
    const int MarkerOff = 0x28;
    const int LivesOff = 0x2C;
    const int ChecksumOff = 0x7C;
    const uint Marker = 0x5356494Cu; // "LIVS"

    // GOOL globals (c1 globals.h): life_count, init_life_count, game_state.
    const uint LifeCountAddr = 0x800618ECu;
    const uint InitLifeCountAddr = 0x80061908u;
    const uint GameStateAddr = 0x800618D0u;
    const uint GameStatePlaying = 0x100u;
    // WillC keeps its own LifeCount (DispC Life_Display reads player field 65) and
    // writes it back to life_count only on SetGameState — a Tawna/Cortex round or the
    // instant save menu saves mid-level, while the global is stale.
    const uint CrashPtrAddr = 0x800566B4u;
    const uint WillyLifeCountOff = 0x164u;
    // c1 psx/card.c card (gp[0x5A]); state 2 = CardPartReadGlobals (Load Game).
    // The slot list (state 5) reads the same block for the progress icons.
    const uint CardPtrAddr = 0x80056564u;
    const uint CardStateReadGlobals = 2u;
    // GOOL counters are 24.8 fixed-point; WillC AddLives caps at 99.
    const uint One = 0x100u;
    const uint MaxLives = 99u * One;

    /// <summary>BIOS write(): <paramref name="data"/> goes to file offset <paramref name="pos"/>.</summary>
    public static void OnWrite(IMemory m, string name, int pos, Span<byte> data)
    {
        if (!ConfigManager.View.SaveLives) return;
        int at = BlockPos - pos;
        if (at < 0 || at + BlockSize > data.Length || !IsCrashSave(name)) return;
        var block = data.Slice(at, BlockSize);
        if (Read32(block, ChecksumOff) != Checksum(block)) return;
        if (!TryReadLives(m, out uint lives)) return;
        Write32(block, MarkerOff, Marker);
        Write32(block, LivesOff, lives);
        Write32(block, ChecksumOff, Checksum(block));
        SessionLog.Info($"card lives saved: {lives >> 8} ({name})");
    }

    /// <summary>BIOS read(): <paramref name="n"/> bytes from file offset <paramref name="pos"/> now at <paramref name="dst"/>.</summary>
    public static void OnRead(IMemory m, string name, int pos, uint dst, int n)
    {
        if (!ConfigManager.View.SaveLives) return;
        int at = BlockPos - pos;
        if (at < 0 || at + BlockSize > n || !IsCrashSave(name)) return;
        if (!IsLoadingGlobals(m)) return;
        Span<byte> block = stackalloc byte[BlockSize];
        for (int i = 0; i < BlockSize; i++) block[i] = m.ReadU8(dst + (uint)(at + i));
        if (Read32(block, ChecksumOff) != Checksum(block)) return;
        if (Read32(block, MarkerOff) != Marker) return;
        uint lives = Read32(block, LivesOff);
        if (!IsLifeCount(lives)) return;
        // CardPartReadGlobals: checksum ok → LevelResetGlobals copies this into
        // life_count → init_life_count comes back from the block (+0x08).
        m.WriteU32(InitLifeCountAddr, lives);
        SessionLog.Info($"card lives loaded: {lives >> 8} ({name})");
    }

    static bool IsCrashSave(string name) => name.StartsWith(SaveNamePrefix, StringComparison.Ordinal);

    static bool IsLoadingGlobals(IMemory m)
    {
        uint card = m.ReadU32(CardPtrAddr);
        if ((card & 0xFF000000u) != 0x80000000u) return false;
        return m.ReadU32(card) == CardStateReadGlobals;
    }

    static bool TryReadLives(IMemory m, out uint lives)
    {
        lives = m.ReadU32(LifeCountAddr);
        if (m.ReadU32(GameStateAddr) == GameStatePlaying)
        {
            uint crash = m.ReadU32(CrashPtrAddr);
            if ((crash & 0xFF000000u) == 0x80000000u)
            {
                uint willy = m.ReadU32(crash + WillyLifeCountOff);
                if (IsLifeCount(willy)) lives = willy;
            }
        }
        return IsLifeCount(lives);
    }

    static bool IsLifeCount(uint v) => v <= MaxLives && v % One == 0;

    /// <summary>func_8003BFFC over the block with the checksum word as zero.</summary>
    static uint Checksum(ReadOnlySpan<byte> block)
    {
        uint sum = 0x12345678u;
        for (int i = 0; i < BlockSize; i++)
        {
            uint b = i >= ChecksumOff ? 0u : block[i];
            sum += b;
            sum = sum << 3 | sum >> 29;
        }
        return sum;
    }

    static uint Read32(ReadOnlySpan<byte> b, int o) =>
        (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24);

    static void Write32(Span<byte> b, int o, uint v)
    {
        b[o] = (byte)v;
        b[o + 1] = (byte)(v >> 8);
        b[o + 2] = (byte)(v >> 16);
        b[o + 3] = (byte)(v >> 24);
    }
}
