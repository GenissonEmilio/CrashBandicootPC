using System.Numerics;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Host;

public static partial class FramePacing
{
    // The Great Gate (level 18, #122). Its climbs run up between tree trunks
    // and wooden walls cut along the 4:3 view; behind them stand untextured
    // black backing planes, so the 16:9 bands showed black wedges where a
    // trunk or wall stopped. Each listed open edge, a 4:3 cut found offline,
    // is continued in its own face's plane with that face's texture (a skirt,
    // as in Jungle Rollers), only as far as the black it has to cover: the
    // sky seen past the trunks stays. Polygon, edge (a-b, b-c, c-a) and reach
    // per mesh, keyed by the mesh translation.
    static readonly Dictionary<(int X, int Y, int Z), (int Polygon, int Edge, int Reach)[]> NativeWideGateSkirts = new()
    {
        // a__iW: first climb.
        [(46280, -45049, -2060)] =
        [
            (542, 1, 1000), (572, 2, 800), (584, 0, 1000), (829, 2, 1000), (830, 1, 1200), (833, 2, 1200),
            (889, 0, 1200), (1062, 0, 1000),
        ],
        // b2_iW and c__iW: the climb past the gem path.
        [(83975, -44201, -3604)] =
        [
            (924, 0, 3200), (928, 0, 3200), (932, 2, 3200), (974, 2, 1400), (980, 2, 800), (982, 2, 3000),
            (986, 2, 2800),
        ],
        [(85083, -33016, -3604)] = [(848, 2, 2000), (963, 1, 1000), (970, 2, 800), (980, 2, 1200)],
        // e__iW and f__iW: the long wall walk.
        [(47169, -32670, -2060)] = [(903, 0, 600), (1353, 0, 600)],
        [(29001, -32670, -2060)] = [(1778, 1, 600)],
        // g__iW and h__iW: the third climb.
        [(7002, -31902, -2060)] = [(1690, 0, 2600), (1692, 0, 1200), (1702, 0, 1400)],
        [(7002, -18013, -2060)] = [(1151, 2, 2200), (1159, 0, 3200), (1257, 0, 3200), (1688, 0, 1200)],
    };

    // The hut beside the last climb (l__iW): the lower half of each of its
    // legs kept one triangle per face, so the legs ended in spikes.
    static readonly Dictionary<(int X, int Y, int Z), int[]> NativeWideGateCells = new()
    {
        [(82406, -15016, -2060)] = [1027, 1028, 1041, 1048],
    };

    static void NativeWideGateRepairs(IMemory m, NativeWideWorld world, List<NativeWideRepair> output)
    {
        var key = ((int)m.ReadU32(world.Header), (int)m.ReadU32(world.Header + 4), (int)m.ReadU32(world.Header + 8));
        if (NativeWideGateSkirts.TryGetValue(key, out var edges))
            NativeWideSkirtRepairs(m, world, null, output,
                edgeReach: edges.ToDictionary(e => e.Polygon * 3 + e.Edge, e => (float)e.Reach));
        if (NativeWideGateCells.TryGetValue(key, out var cells))
            output.AddRange(NativeWideCellRepairs(m, world, [.. cells.Select(c => new NativeWideCell(c, []))]));
        NativeWideBushCells(m, world, output);
    }

    // The teal bushes are sheets of 64x64 texture cells, the bush's two halves
    // side by side (materials 134 and 135). A cell's low U and high V sides are
    // its transparent fringe; its high U and low V sides run on into the next
    // cell. Where the 4:3 view never saw that next cell, the sheet ended in a
    // straight cut. Each such side gets the cell mirrored across it (and the
    // diagonal cell where two meet), so the sheet ends in its own fringe.
    const int NativeWideBushMaterialA = 134, NativeWideBushMaterialB = 135;

    static void NativeWideBushCells(IMemory m, NativeWideWorld world, List<NativeWideRepair> output)
    {
        var halves = new List<(int Polygon, NativeWideClipVertex[] V)>();
        for (int pi = 0; pi < world.PolyCount; pi++)
        {
            uint poly = world.Polygons + (uint)pi * 8;
            uint p0 = FastU32(m, poly);
            int texinfo = (int)((p0 >> 8) & 0xFFFu);
            if (texinfo >= world.TexinfoCount) continue;
            int material = (int)(FastU32(m, world.Texinfos + (uint)texinfo * 4) >> 24);
            if (material is not (NativeWideBushMaterialA or NativeWideBushMaterialB)) continue;
            if (!TryNativeWideMaterial(m, world, pi, 0, out _, out _,
                out short u0, out short v0, out short u1, out short v1, out short u2, out short v2)) continue;
            NativeWidePolygonVertices(p0, FastU32(m, poly + 4), out int a, out int b, out int c);
            halves.Add((pi, [ReadNativeWideLocal(m, world, a) with { U = u0, V = v0 },
                ReadNativeWideLocal(m, world, b) with { U = u1, V = v1 },
                ReadNativeWideLocal(m, world, c) with { U = u2, V = v2 }]));
        }
        if (halves.Count == 0) return;

        // Cells: corner [low/high U, low/high V] of a tile. A triangle holds three
        // corners; its partner across the diagonal, or p + q - r, the fourth.
        var cells = new List<(int Polygon, NativeWideClipVertex[,] Corner)>();
        var used = new bool[halves.Count];
        for (int i = 0; i < halves.Count; i++)
        {
            if (used[i]) continue;
            var t = halves[i].V;
            float uLow = Math.Min(t[0].U, Math.Min(t[1].U, t[2].U)), uHigh = Math.Max(t[0].U, Math.Max(t[1].U, t[2].U));
            float vLow = Math.Min(t[0].V, Math.Min(t[1].V, t[2].V)), vHigh = Math.Max(t[0].V, Math.Max(t[1].V, t[2].V));
            if (uHigh - uLow < 1 || vHigh - vLow < 1) continue;
            var corner = new NativeWideClipVertex[2, 2];
            var have = new bool[2, 2];
            void Put(NativeWideClipVertex v)
            {
                int cu = v.U == uHigh ? 1 : 0, cv = v.V == vHigh ? 1 : 0;
                corner[cu, cv] = v;
                have[cu, cv] = true;
            }
            if (t.Any(v => (v.U != uLow && v.U != uHigh) || (v.V != vLow && v.V != vHigh))) continue;
            foreach (var v in t) Put(v);
            int missU = -1, missV = -1, corners = 0;
            for (int cu = 0; cu < 2; cu++) for (int cv = 0; cv < 2; cv++) if (have[cu, cv]) corners++;
            if (corners != 3) continue;
            for (int cu = 0; cu < 2; cu++) for (int cv = 0; cv < 2; cv++)
                if (!have[cu, cv]) (missU, missV) = (cu, cv);
            if (missU < 0) continue;
            used[i] = true;
            for (int j = i + 1; j < halves.Count && !have[missU, missV]; j++)
            {
                if (used[j]) continue;
                var o = halves[j].V;
                var diagonalA = corner[1 - missU, missV];
                var diagonalB = corner[missU, 1 - missV];
                int shared = 0;
                NativeWideClipVertex? fourth = null;
                foreach (var v in o)
                {
                    if (Position(v) == Position(diagonalA) || Position(v) == Position(diagonalB)) shared++;
                    else if (v.U == (missU == 1 ? uHigh : uLow) && v.V == (missV == 1 ? vHigh : vLow)) fourth = v;
                }
                if (shared != 2 || fourth is not { } found) continue;
                corner[missU, missV] = found;
                have[missU, missV] = true;
                used[j] = true;
            }
            if (!have[missU, missV])
            {
                // A lone half: the right-angle corner is opposite the missing one.
                var right = corner[1 - missU, 1 - missV];
                var p = corner[1 - missU, missV];
                var q = corner[missU, 1 - missV];
                var made = new NativeWideClipVertex(p.X + q.X - right.X, p.Y + q.Y - right.Y, p.Z + q.Z - right.Z,
                    p.R, p.G, p.B, missU == 1 ? uHigh : uLow, missV == 1 ? vHigh : vLow);
                corner[missU, missV] = made;
                output.Add(new(halves[i].Polygon, p, q, made));
            }
            cells.Add((halves[i].Polygon, corner));
        }

        // A side is open when no other cell has both of its corners.
        static (Vector3, Vector3) Side(NativeWideClipVertex a, NativeWideClipVertex b)
        {
            Vector3 pa = Position(a), pb = Position(b);
            bool swap = pa.X > pb.X || (pa.X == pb.X && (pa.Y > pb.Y || (pa.Y == pb.Y && pa.Z > pb.Z)));
            return swap ? (pb, pa) : (pa, pb);
        }
        var count = new Dictionary<(Vector3, Vector3), int>();
        foreach (var (_, c) in cells)
            foreach (var side in new[] { Side(c[0, 0], c[0, 1]), Side(c[1, 0], c[1, 1]), Side(c[0, 0], c[1, 0]), Side(c[0, 1], c[1, 1]) })
                count[side] = count.GetValueOrDefault(side) + 1;
        bool Open(NativeWideClipVertex a, NativeWideClipVertex b) => count.GetValueOrDefault(Side(a, b)) == 1;
        // Corner `at` moved on by `at - from` (twice for the diagonal), with the
        // texture and colour of `look`: the cell mirrored across that side.
        static NativeWideClipVertex Beyond(NativeWideClipVertex at, Vector3 shift, NativeWideClipVertex look) =>
            look with { X = at.X + shift.X, Y = at.Y + shift.Y, Z = at.Z + shift.Z };
        void Quad(int polygon, NativeWideClipVertex a, NativeWideClipVertex b, NativeWideClipVertex c, NativeWideClipVertex d)
        {
            output.Add(new(polygon, a, b, c));
            output.Add(new(polygon, a, c, d));
        }
        foreach (var (polygon, c) in cells)
        {
            bool uSide = Open(c[1, 0], c[1, 1]);
            bool vSide = Open(c[0, 0], c[1, 0]);
            Vector3 across0 = Position(c[1, 0]) - Position(c[0, 0]), across1 = Position(c[1, 1]) - Position(c[0, 1]);
            Vector3 up0 = Position(c[0, 0]) - Position(c[0, 1]), up1 = Position(c[1, 0]) - Position(c[1, 1]);
            if (uSide)
                Quad(polygon, c[1, 0], Beyond(c[1, 0], across0, c[0, 0]), Beyond(c[1, 1], across1, c[0, 1]), c[1, 1]);
            if (vSide)
                Quad(polygon, c[0, 0], c[1, 0], Beyond(c[1, 0], up1, c[1, 1]), Beyond(c[0, 0], up0, c[0, 1]));
            if (uSide && vSide)
                Quad(polygon, c[1, 0], Beyond(c[1, 0], up1, c[1, 1]), Beyond(c[1, 0], across0 + up1, c[0, 1]),
                    Beyond(c[1, 0], across0, c[0, 0]));
        }
    }
}
