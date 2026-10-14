using System.Text;
using System.Text.RegularExpressions;

namespace CrashBandicoot.Launcher.Recomp;

/// <summary>
/// Applies the known Crash SCUS-94900 control-flow fixes that the recompiler
/// does not yet emit (decompressor merge, Duff devices, bgez jump tables,
/// NSD realloc hash-pointer retarget, geom clip jump-table jr→return).
/// </summary>
public static class PostPassApplier
{
    public static void Apply(string mainCsPath, string patchPath)
    {
        if (!File.Exists(mainCsPath))
            throw new FileNotFoundException("Generated main.cs not found.", mainCsPath);
        if (!File.Exists(patchPath))
            throw new FileNotFoundException("Post-pass patch not found.", patchPath);

        var original = NormalizeLf(File.ReadAllText(mainCsPath));
        var patch = NormalizeLf(File.ReadAllText(patchPath));
        var patched = ApplyUnified(original, patch);
        patched = FixGeomJumpTableReturns(patched);
        File.WriteAllText(mainCsPath, patched.Replace("\n", Environment.NewLine));
    }

    /// <summary>
    /// SCUS-94900 lightning / dark world geom (80036340, 80036BF4): recompiler
    /// emits <c>return</c> instead of <c>jr</c> into an fx-bit jump-table after
    /// clobbering GP/SP as temps, and splits 80036BF4/80036D9C so the triangle
    /// loop's continue at 80036CAC becomes an unmapped <c>Dispatcher.Call</c>.
    /// </summary>
    static string FixGeomJumpTableReturns(string src)
    {
        if (src.Contains("func_80036BF4_impl", StringComparison.Ordinal) &&
            src.Contains("L80036CAC:", StringComparison.Ordinal))
            return src; // already merged

        // 80036340: jr into the inlined table body (restores SP/GP at epilogue).
        src = ReplaceOnce(src,
            """
                    c.RA = 0x80030000u;
                    c.RA = c.RA + 0x64D4u;
                    c.SP = c.At << 7;
                    c.RA = c.RA + c.SP;
                    c.FP = ~(c.GP | 0u);
                    return;
                    RecompOne.Runtime.Gte.Write(6, c.S3);
            """,
            """
                    c.RA = 0x80030000u;
                    c.RA = c.RA + 0x64D4u;
                    c.SP = c.At << 7;
                    c.RA = c.RA + c.SP;
                    c.FP = ~(c.GP | 0u);
                    L800364D4: ;
                    RecompOne.Runtime.Gte.Write(6, c.S3);
            """);
        src = RouteFxJumpTable(src,
            """
                    c.RA = c.RA + 0x64D4u;
                    c.SP = c.At << 7;
                    c.RA = c.RA + c.SP;
                    c.FP = ~(c.GP | 0u);

            """, 0x800364D4u, "L800368C8");

        // Merge 80036BF4 + 80036D9C (loop continue at 80036CAC must be a goto).
        src = ReplaceOnce(src,
            """
                public static void func_80036BF4(CpuContext c, IMemory m)
                {
                    c.At = 0x1F800000u;
                    m.WriteU32(c.At, c.S0);
            """,
            """
                public static void func_80036BF4(CpuContext c, IMemory m) => func_80036BF4_impl(c, m, 0);
                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                public static void func_80036D9C(CpuContext c, IMemory m) => func_80036BF4_impl(c, m, 1);
                // 80036BF4/80036D9C are one geom routine incorrectly split; loop continues at 80036CAC.
                static void func_80036BF4_impl(CpuContext c, IMemory m, int entry)
                {
                    if (entry != 0) goto L80036D9C;
                    c.At = 0x1F800000u;
                    m.WriteU32(c.At, c.S0);
            """);

        src = ReplaceOnce(src,
            """
                    c.A3 = c.At + 0u;
                    c.A0 = c.RA + 0u;
                    c.At = m.ReadU16(c.T2);
                    c.A2 = c.A2 << 2;
                    c.V1 = 0u | 0x1FFCu;
                    c.T2 = c.T2 - 0x2u;
                    c.FP = c.At >> 12;
            """,
            """
                    c.A3 = c.At + 0u;
                    c.A0 = c.RA + 0u;
                    c.At = m.ReadU16(c.T2);
                    c.A2 = c.A2 << 2;
                    c.V1 = 0u | 0x1FFCu;
                    L80036CAC: ;
                    c.T2 = c.T2 - 0x2u;
                    c.FP = c.At >> 12;
            """);

        src = ReplaceOnce(src,
            """
                    c.RA = 0x80030000u;
                    c.RA = c.RA + 0x6D9Cu;
                    c.SP = c.SP << 7;
                    c.RA = c.RA + c.SP;
                    c.FP = ~(c.GP | 0u);
                    return;
                }
                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                public static void func_80036D9C(CpuContext c, IMemory m)
                {
                    RecompOne.Runtime.Gte.Write(6, c.S3);
            """,
            """
                    c.RA = 0x80030000u;
                    c.RA = c.RA + 0x6D9Cu;
                    c.SP = c.SP << 7;
                    c.RA = c.RA + c.SP;
                    c.FP = ~(c.GP | 0u);
                    L80036D9C: ;
                    RecompOne.Runtime.Gte.Write(6, c.S3);
            """);
        src = RouteFxJumpTable(src,
            """
                    c.RA = c.RA + 0x6D9Cu;
                    c.SP = c.SP << 7;
                    c.RA = c.RA + c.SP;
                    c.FP = ~(c.GP | 0u);

            """, 0x80036D9Cu, "L80037190");

        src = ReplaceOnce(src,
            """
                    if (c.T5 != 0u) {
                        c.At = m.ReadU16(c.T2);
                        Dispatcher.Call(c, m, 0x80036CACu);
                        return;
                    }
            """,
            """
                    if (c.T5 != 0u) {
                        c.At = m.ReadU16(c.T2);
                        goto L80036CAC;
                    }
            """);

        return src;
    }

    /// <summary>
    /// The <c>jr ra</c> after <paramref name="jump"/> enters an 8-entry table of
    /// 0x80-byte blocks at <paramref name="tableBase"/>, indexed by the fx bits
    /// of the polygon's three vertices (SP = bits &lt;&lt; 7). fx vertices take the
    /// second far colour / IR0 bank: the lightning flash. Entries 0-6 end in
    /// <c>b tail</c>, entry 7 falls into the tail.
    /// </summary>
    static string RouteFxJumpTable(string src, string jump, uint tableBase, string tail)
    {
        jump = NormalizeLf(jump);
        int at = src.IndexOf(jump, StringComparison.Ordinal);
        if (at < 0)
            throw new InvalidDataException(
                $"Fx jump-table pattern for {tableBase:X8} not found. Recompiler output may have changed.");
        at += jump.Length;

        var sw = new StringBuilder("        switch ((c.SP >> 7) & 7u)\n        {\n");
        for (uint k = 1; k < 8; k++)
            sw.Append($"            case {k}u: goto L{tableBase + k * 0x80u:X8};\n");
        sw.Append("        }\n");
        src = src.Insert(at, sw.ToString());
        at += sw.Length;

        var entryEnd = new Regex(
            @"if \(\(int\)0u >= 0\) \{\n[^\n]*\n[ \t]*goto " + tail + @";\n[ \t]*\}\n[^\n]*\n");
        for (uint k = 1; k < 8; k++)
        {
            var end = entryEnd.Match(src, at);
            if (!end.Success)
                throw new InvalidDataException($"Fx jump-table entry {k} of {tableBase:X8} not found.");
            var label = $"        L{tableBase + k * 0x80u:X8}: ;\n";
            src = src.Insert(end.Index + end.Length, label);
            at = end.Index + end.Length + label.Length;
        }

        int tailAt = src.IndexOf(tail + ": ;", at, StringComparison.Ordinal);
        var extra = entryEnd.Match(src, at);
        if (tailAt < 0 || (extra.Success && extra.Index < tailAt))
            throw new InvalidDataException($"Fx jump-table {tableBase:X8} layout changed.");
        return src;
    }

    static string ReplaceOnce(string src, string oldText, string newText)
    {
        oldText = NormalizeLf(oldText);
        newText = NormalizeLf(newText);
        var idx = src.IndexOf(oldText, StringComparison.Ordinal);
        if (idx < 0)
            throw new InvalidDataException(
                "Geom jump-table post-pass pattern not found. Recompiler output may have changed.\n" +
                "Missing:\n" + oldText.Split('\n').FirstOrDefault());
        return src.Substring(0, idx) + newText + src.Substring(idx + oldText.Length);
    }

    static string NormalizeLf(string s) => s.Replace("\r\n", "\n").Replace("\r", "\n");

    static string ApplyUnified(string original, string patch)
    {
        // Match hunks by text, not @@ line numbers. Compile-time FramePacing
        // wrappers insert methods and shift every later line.
        var src = original;
        int searchFrom = 0;
        var lines = patch.Split('\n');
        int idx = 0;
        int hunk = 0;

        while (idx < lines.Length)
        {
            var line = lines[idx];
            if (line.StartsWith("---") || line.StartsWith("+++") || line.StartsWith("diff"))
            {
                idx++;
                continue;
            }

            if (!Regex.IsMatch(line, @"^@@ -\d+(?:,\d+)? \+\d+(?:,\d+)? @@"))
            {
                idx++;
                continue;
            }

            hunk++;
            idx++;
            var oldSb = new StringBuilder();
            var newSb = new StringBuilder();
            while (idx < lines.Length &&
                   !lines[idx].StartsWith("@@") &&
                   !lines[idx].StartsWith("diff"))
            {
                var pl = lines[idx];
                if (pl.StartsWith('\\') || pl.Length == 0)
                {
                    idx++;
                    continue;
                }

                var tag = pl[0];
                var content = pl.Length > 1 ? pl[1..] : "";
                switch (tag)
                {
                    case ' ':
                        oldSb.Append(content).Append('\n');
                        newSb.Append(content).Append('\n');
                        break;
                    case '-':
                        oldSb.Append(content).Append('\n');
                        break;
                    case '+':
                        newSb.Append(content).Append('\n');
                        break;
                    default:
                        throw new InvalidDataException($"Bad patch tag '{tag}' at patch line {idx + 1}");
                }

                idx++;
            }

            var oldText = oldSb.ToString();
            var newText = newSb.ToString();
            if (oldText.Length == 0)
                continue;

            int at = src.IndexOf(oldText, searchFrom, StringComparison.Ordinal);
            if (at < 0)
                at = src.IndexOf(oldText, StringComparison.Ordinal);
            if (at < 0)
            {
                var preview = oldText.Split('\n')[0];
                throw new InvalidDataException(
                    $"Post-pass hunk {hunk} not found. Recompiler output may have changed — regenerate the patch.\nExpected: {preview}");
            }

            src = src[..at] + newText + src[(at + oldText.Length)..];
            searchFrom = at + newText.Length;
        }

        return src;
    }
}
