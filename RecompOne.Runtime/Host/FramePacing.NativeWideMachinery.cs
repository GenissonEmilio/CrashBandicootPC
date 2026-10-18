using System.Numerics;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Host;

public static partial class FramePacing
{
    // Heavy Machinery (level 6) is a side view. Every mesh is backed by a
    // riveted wall in the absolute plane Z 0, built from 400-unit cells, with
    // machinery, pillars and walkways in front of it. Its rows were culled per
    // triangle to the 4:3 view: wherever the camera stops moving sideways (the
    // level start, the vertical shafts, the turns) the 16:9 bands saw past
    // their ends and through cells missing between them. Each row is filled
    // with copies of its own cells, in every free slot of its grid and a few
    // past both ends. Extensions only reach the side bands and are drawn
    // behind all real scenery, so they show only where the 16:9 view saw
    // nothing. Meshes overlap, and so do their fills: each fill is ranked by
    // its distance to its row's own cells, so one of them wins (#136).
    const float NativeWideMachineryCell = 400;
    const int NativeWideMachinerySlots = 10;
    // A new cell repeats the cell this far towards the row's middle (or a
    // multiple of it), so the wall's panels stay lined up from row to row.
    const float NativeWideMachineryPeriod = 1600;

    static List<NativeWideRepair> NativeWideMachineryRepairs(IMemory m, NativeWideWorld world)
    {
        float plane = -(int)m.ReadU32(world.Header + 8);
        float cell = NativeWideMachineryCell, slack = cell / 25;
        var rects = new List<(float X0, float X1, float Y0, float Y1)>();
        var rows = new Dictionary<(float Y0, float Y1), SortedDictionary<float, List<NativeWideRepair>>>();
        var repairs = new List<NativeWideRepair>();
        for (int pi = 0; pi < world.PolyCount; pi++)
        {
            uint poly = world.Polygons + (uint)pi * 8;
            NativeWidePolygonVertices(FastU32(m, poly), FastU32(m, poly + 4), out int a, out int b, out int c);
            NativeWideClipVertex[] t = [ReadNativeWideLocal(m, world, a), ReadNativeWideLocal(m, world, b),
                ReadNativeWideLocal(m, world, c)];
            if (t[0].Z != plane || t[1].Z != plane || t[2].Z != plane) continue;
            float x0 = t.Min(v => v.X), x1 = t.Max(v => v.X), y0 = t.Min(v => v.Y), y1 = t.Max(v => v.Y);
            rects.Add((x0, x1, y0, y1));
            if (Math.Abs(x1 - x0 - cell) > slack * 2 || !TryNativeWideMaterial(m, world, pi, 0, out _, out _,
                out short u0, out short v0, out short u1, out short v1, out short u2, out short v2)) continue;
            if (!rows.TryGetValue((y0, y1), out var cells)) rows[(y0, y1)] = cells = [];
            if (!cells.TryGetValue(x0, out var tris)) cells[x0] = tris = [];
            tris.Add(new(pi, t[0] with { U = u0, V = v0 }, t[1] with { U = u1, V = v1 }, t[2] with { U = u2, V = v2 }));
        }
        // A slot is free when no face of the wall, original or added, overlaps it.
        bool Free(float x0, float x1, float y0, float y1) => !rects.Any(r =>
            r.X0 < x1 - slack && x0 < r.X1 - slack && r.Y0 < y1 - slack && y0 < r.Y1 - slack);
        // Whole cells; a half cell left by the cull gets its missing corner.
        var wholes = new Dictionary<(float Y0, float Y1), SortedDictionary<float, List<NativeWideRepair>>>();
        var lits = new Dictionary<(float Y0, float Y1), List<float>>();
        foreach (var (row, cells) in rows)
        {
            var whole = new SortedDictionary<float, List<NativeWideRepair>>();
            foreach (var (x0, tris) in cells)
            {
                if (tris.Count >= 2) { whole[x0] = tris; continue; }
                var t = tris[0];
                if (!NativeWideOppositeCorner([t.A, t.B, t.C], out var p, out var q, out var corner)) continue;
                repairs.Add(new(t.Polygon, p, q, corner, Rank: 1));
                whole[x0] = [t, repairs[^1]];
            }
            if (whole.Count == 0) continue;
            wholes[row] = whole;
            // Cells hidden behind pillars and machinery are shaded black; copies
            // come from lit ones.
            lits[row] = [.. whole.Where(e => e.Value.All(t => NativeWideLit(t.A) && NativeWideLit(t.B) && NativeWideLit(t.C)))
                .Select(e => e.Key)];
        }
        // Rows with lit cells claim free slots first: a wall's edge cells can
        // form a row of their own, 8 units off and shaded black, whose copies
        // would otherwise take the main row's slots.
        foreach (var ((y0, y1), cells) in rows.OrderByDescending(e => lits.TryGetValue(e.Key, out var l) ? l.Count : -1)
            .ThenByDescending(e => e.Value.Count))
        {
            if (!wholes.TryGetValue((y0, y1), out var whole)) continue;
            var lit = lits[(y0, y1)];
            // A row with no lit cell between lit rows is a band of shadow under a
            // walkway or ledge, never seen in 4:3; past the walkway's end its
            // copies looked like holes (#136). They take the light of the lit
            // rows around it instead. A row fading out with no lit row on one
            // side (a shaft's dark bottom) keeps its darkness.
            NativeWideClipVertex? light = null;
            if (lit.Count == 0)
            {
                lit = [.. whole.Keys];
                light = NativeWideMachineryLight(wholes, lits, y0, y1);
            }
            float first = cells.Keys.First(), last = cells.Keys.Last(), middle = (first + last) / 2;
            // The row's grid follows most of its cells (edge cells can be 408 wide).
            float phase = cells.Keys.GroupBy(x => ((x - first) % cell + cell) % cell).MaxBy(g => g.Count())!.Key;
            float start = first + phase - (phase > 0 ? cell : 0);
            int slots = (int)MathF.Ceiling((last - start) / cell);
            for (int i = -NativeWideMachinerySlots; i <= slots + NativeWideMachinerySlots; i++)
            {
                float x = start + i * cell;
                if (!Free(x, x + cell, y0, y1)) continue;
                // Towards the middle by whole periods, the nearest lit cell otherwise.
                float inward = x < middle ? 1 : -1, source = float.NaN;
                for (float s = x + inward * NativeWideMachineryPeriod; float.IsNaN(source)
                    && (inward > 0 ? s <= last + slack : s >= first - slack); s += inward * NativeWideMachineryPeriod)
                    source = lit.FirstOrDefault(e => Math.Abs(e - s) <= slack, float.NaN);
                if (float.IsNaN(source)) source = lit.MinBy(e => Math.Abs(e - x));
                float dx = x - source;
                // Where meshes' fills overlap, the one nearer its own wall cells shows.
                float rank = 1 + MathF.Round(cells.Keys.Min(e => Math.Abs(e - x)) / cell);
                NativeWideClipVertex Move(NativeWideClipVertex v) => light is { } l
                    ? v with { X = v.X + dx, R = l.R, G = l.G, B = l.B } : v with { X = v.X + dx };
                foreach (var t in whole[source])
                    repairs.Add(new(t.Polygon, Move(t.A), Move(t.B), Move(t.C), Rank: rank));
                rects.Add((x, x + cell, y0, y1));
            }
        }
        NativeWideMachineryHalfCells(m, world, plane, repairs);
        return repairs;
    }

    // Blocks, columns and ledges in front of the wall lost single triangles to
    // the same cull, and the wall fill behind showed through the gap as a
    // wrongly coloured triangle (#136). A lone right triangle whose legs are
    // one cell along two axes, whose long edge no other triangle shares and
    // whose missing half no face near its plane covers gets its missing corner,
    // at its true depth.
    static void NativeWideMachineryHalfCells(IMemory m, NativeWideWorld world, float plane, List<NativeWideRepair> repairs)
    {
        var faces = new List<(int Polygon, NativeWideClipVertex[] T)>();
        var edges = new Dictionary<NativeWideEdge, int>();
        for (int pi = 0; pi < world.PolyCount; pi++)
        {
            uint poly = world.Polygons + (uint)pi * 8;
            NativeWidePolygonVertices(FastU32(m, poly), FastU32(m, poly + 4), out int a, out int b, out int c);
            if (!TryNativeWideMaterial(m, world, pi, 0, out _, out _,
                out short u0, out short v0, out short u1, out short v1, out short u2, out short v2)) continue;
            NativeWideClipVertex[] t = [ReadNativeWideLocal(m, world, a) with { U = u0, V = v0 },
                ReadNativeWideLocal(m, world, b) with { U = u1, V = v1 }, ReadNativeWideLocal(m, world, c) with { U = u2, V = v2 }];
            faces.Add((pi, t));
            for (int k = 0; k < 3; k++)
            {
                var key = NativeWideEdgeKey(t[k], t[(k + 1) % 3]);
                edges[key] = edges.GetValueOrDefault(key) + 1;
            }
        }
        static bool Leg(Vector3 d) => (d.X != 0 ? 1 : 0) + (d.Y != 0 ? 1 : 0) + (d.Z != 0 ? 1 : 0) == 1
            && d.Length() is >= 380 and <= 420;
        static int AxisOf(NativeWideClipVertex[] t) => t[0].X == t[1].X && t[1].X == t[2].X ? 0
            : t[0].Y == t[1].Y && t[1].Y == t[2].Y ? 1 : t[0].Z == t[1].Z && t[1].Z == t[2].Z ? 2 : -1;
        static float Along(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
        static Vector2 Across(Vector3 v, int axis) => axis == 0 ? new(v.Y, v.Z) : axis == 1 ? new(v.X, v.Z) : new(v.X, v.Y);
        static float Cross(Vector2 a, Vector2 b, Vector2 p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
        foreach (var (pi, t) in faces)
        {
            int axis = AxisOf(t);
            if (axis < 0 || (axis == 2 && t[0].Z == plane)) continue;
            if (!t.Any(NativeWideLit)) continue;
            if (!NativeWideOppositeCorner(t, out var p, out var q, out var corner)) continue;
            Vector3 right = Position(p) + Position(q) - Position(corner);
            if (!Leg(Position(p) - right) || !Leg(Position(q) - right)) continue;
            if (edges.GetValueOrDefault(NativeWideEdgeKey(p, q)) != 1) continue;
            Vector3 centre = (Position(p) + Position(q) + Position(corner)) / 3;
            Vector2 at = Across(centre, axis);
            // Covered also by a slightly slanted face just off the plane.
            bool covered = faces.Any(f => f.Polygon != pi
                && f.T.All(v => Math.Abs(Along(Position(v), axis) - Along(centre, axis)) <= NativeWideMachineryCell / 2)
                && Inside(f.T));
            bool Inside(NativeWideClipVertex[] f)
            {
                Vector2 a = Across(Position(f[0]), axis), b = Across(Position(f[1]), axis), c = Across(Position(f[2]), axis);
                float d1 = Cross(a, b, at), d2 = Cross(b, c, at), d3 = Cross(c, a, at);
                return (d1 >= 0 && d2 >= 0 && d3 >= 0) || (d1 <= 0 && d2 <= 0 && d3 <= 0);
            }
            if (!covered) repairs.Add(new(pi, p, q, corner, Boundary: true));
        }
    }

    // The mean vertex colour of the lit cells in the nearest rows with lit
    // cells above and below a row (each within one period), or null.
    static NativeWideClipVertex? NativeWideMachineryLight(
        Dictionary<(float Y0, float Y1), SortedDictionary<float, List<NativeWideRepair>>> wholes,
        Dictionary<(float Y0, float Y1), List<float>> lits, float y0, float y1)
    {
        var lit = lits.Where(e => e.Value.Count > 0).ToList();
        var above = lit.Where(e => e.Key.Y1 <= y0 + 8 && e.Key.Y1 >= y0 - NativeWideMachineryPeriod)
            .OrderByDescending(e => e.Key.Y1).FirstOrDefault();
        var below = lit.Where(e => e.Key.Y0 >= y1 - 8 && e.Key.Y0 <= y1 + NativeWideMachineryPeriod)
            .OrderBy(e => e.Key.Y0).FirstOrDefault();
        if (above.Value == null || below.Value == null) return null;
        float r = 0, g = 0, b = 0; int n = 0;
        foreach (var e in new[] { above, below })
            foreach (float x in e.Value)
                foreach (var t in wholes[e.Key][x])
                    foreach (var v in new[] { t.A, t.B, t.C }) { r += v.R; g += v.G; b += v.B; n++; }
        return new NativeWideClipVertex(0, 0, 0, (byte)(r / n), (byte)(g / n), (byte)(b / n), 0, 0);
    }

    static bool NativeWideLit(NativeWideClipVertex v) => Math.Max(v.R, Math.Max(v.G, v.B)) > 24;
}
