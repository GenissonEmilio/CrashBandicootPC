using System.Numerics;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Host;

public static partial class FramePacing
{
    // Sunset Vista's temple (level 35): climbing shafts and the corridors
    // between them. Every mesh has one big back wall (local Z plane) with
    // ledges in front; the shafts' side walls (X planes) are only 1600 deep
    // and their ends towards the camera were culled to the 4:3 view, as were
    // the back walls beside them. Rows of whole stone cells are continued
    // with copies of their outermost cells (no stretched or mirrored texture),
    // drawn behind all real scenery (renderer), so they only fill what the
    // 16:9 view saw nothing of. They still cover the sky: inside the shafts
    // the sky only shows past the culled wall ends.
    static List<NativeWideRepair> NativeWideSunsetRepairs(IMemory m, NativeWideWorld world)
    {
        var repairs = new List<NativeWideRepair>();
        var faces = NativeWideFacesOf(m, world);
        int x = (int)m.ReadU32(world.Header);
        // The rooftops (j, l), the tops of the second and third shafts (i, n)
        // and the ending (o) open onto the sky, where their low fronts and
        // floors really end.
        bool roof = x is 81600 or 103600 or 142400 or 151235 or 175200;
        NativeWideRowRepairs(faces, roof ? NativeWideSunsetRoofRows : NativeWideSunsetRows, repairs);
        // The first two shafts share the temple's outer left wall (absolute X
        // 111200). Rooms and forest lie beyond it, so its continuation keeps
        // its true depth and hides them.
        float outerWall = 111200 - x;
        bool shaft = x is 110000 or 113200;
        for (int i = 0; shaft && i < repairs.Count; i++)
        {
            var r = repairs[i];
            if (r.A.X == outerWall && r.B.X == outerWall && r.C.X == outerWall)
                repairs[i] = r with { Boundary = true };
        }
        return repairs;
    }

    // The sunset skies (skyzW over the rooftops, sk2zW over the ending) are
    // camera-centred panoramas, rows of cells ending at about +/-47° (the
    // lowest row at +/-26°), so the 16:9 corners saw past them. Every cell
    // has its own picture, so nothing repeats. Like the other skies, each
    // row's end cell is mirrored about its outer edge, and the mirror again
    // about its own, until the row reaches past +/-75°; the seams stay
    // continuous. The end edges lean, so the mirror plane passes through the
    // edge and the camera. A cell the 4:3 cull halved along its diagonal is
    // completed first. Rows mirrored about slightly different planes would
    // split where they meet: each mirrored corner takes the mean of its
    // close images, then the position of a corner already there within 2°.
    static bool NativeWideSunsetSky(IMemory m, NativeWideWorld world) =>
        m.ReadU32(world.Header + 0x1C) == 1
        && (world.PolyCount, world.VertexCount) is (35, 27) or (42, 32);

    static List<NativeWideRepair> NativeWideSunsetSkyRepairs(IMemory m, NativeWideWorld world)
    {
        const float Degree = MathF.PI / 180;
        static float Angle(NativeWideClipVertex v) => MathF.Atan2(v.X, -v.Z);
        static Vector3 Key(NativeWideClipVertex v) => new(MathF.Round(v.X), MathF.Round(v.Y), MathF.Round(v.Z));
        var vertices = Enumerable.Range(0, world.VertexCount).Select(i => ReadNativeWideLocal(m, world, i)).ToArray();
        var sky = new List<(int Polygon, NativeWideClipVertex[] T)>();
        for (int pi = 0; pi < world.PolyCount; pi++)
        {
            uint poly = world.Polygons + (uint)pi * 8;
            NativeWidePolygonVertices(m.ReadU32(poly), m.ReadU32(poly + 4), out int a, out int b, out int c);
            if (!TryNativeWideMaterial(m, world, pi, 0, out _, out _,
                out short u0, out short v0, out short u1, out short v1, out short u2, out short v2)) continue;
            sky.Add((pi, [vertices[a] with { U = u0, V = v0 }, vertices[b] with { U = u1, V = v1 },
                vertices[c] with { U = u2, V = v2 }]));
        }
        var repairs = new List<NativeWideRepair>();
        // Edges used once, as (triangle, edge index).
        static List<(int T, int E)> Open(List<(int Polygon, NativeWideClipVertex[] T)> all)
        {
            static (Vector3, Vector3) EdgeOf(NativeWideClipVertex[] t, int e)
            {
                Vector3 p = Key(t[e]), q = Key(t[(e + 1) % 3]);
                return p.X < q.X || (p.X == q.X && (p.Y < q.Y || (p.Y == q.Y && p.Z < q.Z))) ? (p, q) : (q, p);
            }
            var count = new Dictionary<(Vector3, Vector3), int>();
            foreach (var (_, t) in all)
                for (int e = 0; e < 3; e++) count[EdgeOf(t, e)] = count.GetValueOrDefault(EdgeOf(t, e)) + 1;
            var open = new List<(int, int)>();
            for (int ti = 0; ti < all.Count; ti++)
                for (int e = 0; e < 3; e++)
                    if (count[EdgeOf(all[ti].T, e)] == 1) open.Add((ti, e));
            return open;
        }
        // Half cells: an open diagonal (wide in angle and in height) gets the
        // missing corner p + q - r, in position and texture.
        foreach (var (ti, e) in Open(sky))
        {
            var t = sky[ti].T;
            var p = t[e]; var q = t[(e + 1) % 3]; var r = t[(e + 2) % 3];
            if (MathF.Abs(Angle(p) - Angle(q)) < 5 * Degree || MathF.Abs(p.Y - q.Y) < 200) continue;
            repairs.Add(new(sky[ti].Polygon, q, p, p with
            {
                X = p.X + q.X - r.X, Y = p.Y + q.Y - r.Y, Z = p.Z + q.Z - r.Z, U = p.U + q.U - r.U, V = p.V + q.V - r.V,
            }));
        }
        List<(int Polygon, NativeWideClipVertex[] T)> cells = [.. sky, .. repairs.Select(r => (r.Polygon, new[] { r.A, r.B, r.C }))];
        // Each row's end: an open edge nearly along a column, and the cell
        // inside it (triangles within one column's width of the edge, in the
        // edge's height range).
        var ends = new List<(NativeWideClipVertex A, NativeWideClipVertex B, List<(int Polygon, NativeWideClipVertex[] T)> Cell)>();
        foreach (var (ti, e) in Open(cells))
        {
            var t = cells[ti].T;
            var a = t[e]; var b = t[(e + 1) % 3];
            if (MathF.Abs(Angle(a) - Angle(b)) > 3 * Degree || MathF.Abs(a.Y - b.Y) < 200) continue;
            float edge = (Angle(a) + Angle(b)) / 2, side = Math.Sign(edge - Angle(t[(e + 2) % 3]));
            float yLow = Math.Min(a.Y, b.Y) - 64, yHigh = Math.Max(a.Y, b.Y) + 64;
            var cell = cells.Where(c => c.T.All(v => v.Y >= yLow && v.Y <= yHigh
                && (edge - Angle(v)) * side >= -3 * Degree && (edge - Angle(v)) * side <= 21 * Degree)).ToList();
            if (cell.Count > 0) ends.Add((a, b, cell));
        }
        var corners = cells.SelectMany(c => c.T).ToList();
        for (int step = 0; step < 4 && ends.Count > 0; step++)
        {
            // Every end cell mirrored about the plane through its edge and the camera.
            var images = new Dictionary<Vector3, List<Vector3>>();
            foreach (var (a, b, cell) in ends)
            {
                var normal = Vector3.Normalize(Vector3.Cross(Position(a), Position(b)));
                foreach (var v in cell.SelectMany(c => c.T))
                {
                    if (!images.TryGetValue(Key(v), out var list)) images[Key(v)] = list = [];
                    list.Add(Position(v) - 2 * Vector3.Dot(Position(v), normal) * normal);
                }
            }
            // A corner shared by two rows ending at the same column has two
            // close images; rows ending elsewhere mirror it far away.
            NativeWideClipVertex Image(NativeWideClipVertex v, Vector3 normal)
            {
                var own = Position(v) - 2 * Vector3.Dot(Position(v), normal) * normal;
                var close = images[Key(v)].Where(x => Vector3.Distance(x, own) < 120).ToList();
                var p = close.Aggregate(Vector3.Zero, (s, x) => s + x) / close.Count;
                var moved = v with { X = p.X, Y = p.Y, Z = p.Z };
                foreach (var o in corners)
                    if (MathF.Abs(Angle(o) - Angle(moved)) < 2 * Degree && MathF.Abs(o.Y - moved.Y) <= 48)
                        return moved with { X = o.X, Y = o.Y, Z = o.Z };
                return moved;
            }
            var next = new List<(NativeWideClipVertex A, NativeWideClipVertex B, List<(int Polygon, NativeWideClipVertex[] T)> Cell)>();
            foreach (var (a, b, cell) in ends)
            {
                float edge = (Angle(a) + Angle(b)) / 2;
                var normal = Vector3.Normalize(Vector3.Cross(Position(a), Position(b)));
                var copy = cell.Select(c => (c.Polygon, T: c.T.Select(v => Image(v, normal)).ToArray()))
                    .Where(c => Key(c.T[0]) != Key(c.T[1]) && Key(c.T[1]) != Key(c.T[2]) && Key(c.T[2]) != Key(c.T[0]))
                    .ToList();
                if (copy.Count == 0) continue;
                foreach (var (polygon, t) in copy) repairs.Add(new(polygon, t[0], t[1], t[2]));
                var points = copy.SelectMany(c => c.T).ToList();
                corners.AddRange(points);
                // The copy's outer edge: its corners farthest from this edge,
                // at the top and at the bottom of the row.
                float side = Math.Sign(Angle(points.MaxBy(v => MathF.Abs(Angle(v) - edge))) - edge);
                float yMid = (a.Y + b.Y) / 2;
                var top = points.Where(v => v.Y < yMid).MaxBy(v => (Angle(v) - edge) * side);
                var bottom = points.Where(v => v.Y >= yMid).MaxBy(v => (Angle(v) - edge) * side);
                if (MathF.Abs(Angle(top)) < 75 * Degree) next.Add((top, bottom, copy.Select(c => (c.Polygon, c.T)).ToList()));
            }
            ends = next;
        }
        return repairs;
    }

    // The level starts at the west end of a walkway in front of the temple,
    // with forest panels behind. The panels start at absolute X 0, the
    // walkway at X 4800, and the 16:9 view saw past both. The panels repeat
    // every 6400 (four cells of different textures), the walkway (floor and
    // front; not the temple facade above it) every 800: whole periods
    // are copied west. Nothing is added over a face already there, so the
    // cells the 4:3 cull halved at the walkway's end are completed too.
    static bool NativeWideSunsetStart(IMemory m, NativeWideWorld world) =>
        world.PolyCount == 1897 && world.VertexCount == 1954
        && (int)m.ReadU32(world.Header) == 8000 && (int)m.ReadU32(world.Header + 8) == -4400;

    static readonly NativeWideCopy[] NativeWideSunsetStartCopies =
    [
        new(new(12700, -90000, -5700), new(19300, -70000, -2700), new(-12800, 0, 0)),
        new(new(12700, -90000, -5700), new(19300, -70000, -2700), new(-19200, 0, 0)),
        // The walkway's floor strips and front rows repeat every 800.
        new(new(5600, -84500, 0), new(6400, -83190, 1700), new(-800, 0, 0), Opaque: true),
        new(new(5600, -84500, 0), new(6400, -83190, 1700), new(-1600, 0, 0), Opaque: true),
        new(new(5600, -84500, 0), new(6400, -83190, 1700), new(-2400, 0, 0), Opaque: true),
        new(new(5600, -84500, 0), new(6400, -83190, 1700), new(-3200, 0, 0), Opaque: true),
        new(new(5600, -84500, 0), new(6400, -83190, 1700), new(-4000, 0, 0), Opaque: true),
    ];

    static List<NativeWideRepair> NativeWideSunsetStartRepairs(IMemory m, NativeWideWorld world)
    {
        var origin = new Vector3((int)m.ReadU32(world.Header), (int)m.ReadU32(world.Header + 4),
            (int)m.ReadU32(world.Header + 8));
        var faces = NativeWideFacesOf(m, world);
        // The forest panels are semi-transparent. The walkway's root decals
        // neither block its copies nor are copied (they do not repeat).
        NativeWideTriangleGrid all = new(), opaque = new();
        foreach (var f in faces)
        {
            all.Add(f.T);
            if (!f.SemiTrans) opaque.Add(f.T);
        }
        var repairs = new List<NativeWideRepair>();
        foreach (var copy in NativeWideSunsetStartCopies)
            for (int pi = 0; pi < faces.Length; pi++)
            {
                var t = faces[pi].T;
                if (!faces[pi].Valid || (copy.Opaque && faces[pi].SemiTrans) || t.Any(v => Position(v) + origin != Vector3.Clamp(Position(v) + origin, copy.Min, copy.Max)))
                    continue;
                NativeWideClipVertex[] moved = [.. t.Select(v => v with
                {
                    X = (float.IsNaN(copy.MirrorX) ? v.X : 2 * (copy.MirrorX - origin.X) - v.X) + copy.Offset.X,
                    Y = v.Y + copy.Offset.Y, Z = v.Z + copy.Offset.Z,
                })];
                // Cell corners are off the 1600 grid by up to 16: test a slightly
                // smaller triangle, so neighbours touching by that much still fit.
                var centre = (Position(moved[0]) + Position(moved[1]) + Position(moved[2])) / 3;
                if ((copy.Opaque ? opaque : all).Overlaps([.. moved.Select(v => v with
                {
                    X = centre.X + (v.X - centre.X) * 0.9f, Y = centre.Y + (v.Y - centre.Y) * 0.9f,
                    Z = centre.Z + (v.Z - centre.Z) * 0.9f,
                })])) continue;
                all.Add(moved);
                opaque.Add(moved);
                repairs.Add(new(pi, moved[0], moved[1], moved[2]));
            }
        return repairs;
    }

    // Side walls (X planes) continue towards the camera (+Z); back walls,
    // ledge fronts (Z planes) and floors (Y planes) both ways along X. The
    // shafts' bases (statue fronts and their floors) also stop at the outer
    // walls, and the lowest side wall rows are only two cells deep.
    static readonly NativeWideRows[] NativeWideSunsetRows =
    [
        new(0, 2, Low: false, High: true, Extend: 4800, MinBlock: 1200, MinHeight: 1600, MinLength: 400, MinRun: 400),
        new(2, 0, Low: true, High: true, Extend: 3200, MinBlock: 800, MinHeight: 800, MinLength: 800),
        new(1, 0, Low: true, High: true, Extend: 3200, MinBlock: 800, MinHeight: 400, MinLength: 800),
    ];

    // On the rooftops only tall walls continue.
    static readonly NativeWideRows[] NativeWideSunsetRoofRows =
    [
        new(0, 2, Low: false, High: true, Extend: 4800, MinBlock: 1200, MinHeight: 1600, MinLength: 400, MinRun: 400),
        new(2, 0, Low: true, High: true, Extend: 3200, MinBlock: 800, MinHeight: 1600, MinLength: 1600),
    ];
}
