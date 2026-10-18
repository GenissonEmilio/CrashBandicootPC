using System.Numerics;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Host;

public static partial class FramePacing
{
    // Cortex Power (level 3) is walked through narrow corridors and rooms seen
    // steeply from above (about -57°). Their side walls are single vertical
    // faces with nothing modelled behind them, cut by the 4:3 cull at the top
    // and toward the camera. The 16:9 bands looked over the walls and past
    // their near ends: at black, or at the parallel path's rooms and toxic
    // pools (#109). Wall tops rise until the camera can no longer see over
    // them, at their true depth so they hide the rooms behind. Wall ends
    // toward the camera then continue in their own plane, behind all real
    // scenery, so they only fill what nothing else covers. The building front
    // of the opening camera (an outdoor scene) only continues sideways.
    // The building front's mesh also holds the first corridor inside (#135:
    // its hazard-striped door posts, continued sideways, striped the pit
    // under the pipes); only faces out from the front (absolute Z) count.
    const float NativeWidePowerFrontZ = 128800;
    const float NativeWidePowerRise = 1600;
    const float NativeWidePowerReach = 1600;
    // Wall tops below this (absolute Y) are kerbs and pool edges, not walls.
    const float NativeWidePowerWallTop = 2400;
    const float NativeWidePowerSetBack = 48;
    // How far behind a wall's plane the wall above a ledge may stand.
    const float NativeWidePowerStep = 96;
    // Screens between the paths stand in the dark, away from the lights.
    const float NativeWidePowerScreenShade = 40;

    static List<NativeWideRepair> NativeWidePowerRepairs(IMemory m, NativeWideWorld world)
    {
        var repairs = new List<NativeWideRepair>();
        if (m.ReadU32(world.Header + 0x1C) != 0) return repairs;
        var origin = new Vector3((int)m.ReadU32(world.Header), (int)m.ReadU32(world.Header + 4),
            (int)m.ReadU32(world.Header + 8));
        // The opening scene's building front and the entrance below it.
        var mesh = (world.PolyCount, world.VertexCount, (int)origin.X, (int)origin.Z);
        bool roof = mesh is (531, 590, 7665, 124324), front = mesh is (1605, 1585, 14600, 131598);
        bool Outdoor(NativeWideClipVertex[] t) =>
            roof || front && Math.Min(t[0].Z, Math.Min(t[1].Z, t[2].Z)) + origin.Z >= NativeWidePowerFrontZ;
        var faces = NativeWideFacesOf(m, world);
        float meshEndZ = faces.Where(f => f.Valid).SelectMany(f => f.T).Select(v => v.Z).DefaultIfEmpty().Max();
        var walls = new List<(int Polygon, NativeWideClipVertex[] T)>();
        for (int pi = 0; pi < faces.Length; pi++)
            if (faces[pi].Valid && !faces[pi].SemiTrans && NativeWidePowerWall(faces[pi].T)) walls.Add((pi, faces[pi].T));
        var surface = new NativeWideTriangleGrid();
        foreach (var f in faces) surface.Add(f.T);
        var edges = NativeWidePowerEdges(faces.Select(f => f.T));
        // Half cells the 4:3 cull left on walls (one triangle of a nearly
        // rectangular cell, its diagonal open) get their missing corner first,
        // so a view no longer passes through them and their top rises too.
        int originals = walls.Count;
        for (int wi = 0; wi < originals; wi++)
        {
            var (pi, t) = walls[wi];
            for (int r = 0; r < 3; r++)
            {
                var right = t[r]; var p = t[(r + 1) % 3]; var q = t[(r + 2) % 3];
                Vector3 dp = Position(p) - Position(right), dq = Position(q) - Position(right);
                if (edges[NativeWidePowerKey(p, q)] != 1
                    || Math.Abs(Vector3.Dot(dp, dq)) > 0.1f * dp.Length() * dq.Length()) continue;
                var uv = NativeWidePowerCornerUv(right, p, q);
                var corner = new NativeWideClipVertex(p.X + q.X - right.X, p.Y + q.Y - right.Y, p.Z + q.Z - right.Z,
                    Math.Clamp(p.R + q.R - right.R, 0, 255), Math.Clamp(p.G + q.G - right.G, 0, 255),
                    Math.Clamp(p.B + q.B - right.B, 0, 255), uv.X, uv.Y);
                // A neighbouring mesh can hold the whole cell: the completion
                // stands a sub-pixel distance behind the wall so that one wins.
                var back = Vector3.Cross(Position(p) - Position(q), Position(corner) - Position(q));
                if (back.Length() < 1) continue;
                back = Vector3.Normalize(back) * 4;
                NativeWideClipVertex Back(NativeWideClipVertex v) =>
                    v with { X = v.X + back.X, Y = v.Y + back.Y, Z = v.Z + back.Z };
                NativeWideClipVertex[] half = [Back(q), Back(p), Back(corner)];
                if (surface.Overlaps(half)) continue;
                surface.Add(half);
                walls.Add((pi, half));
                repairs.Add(new(pi, half[0], half[1], half[2], Boundary: true));
                break;
            }
        }
        int halves = repairs.Count;
        if (halves > 0) edges = NativeWidePowerEdges(faces.Select(f => f.T).Concat(walls.Skip(originals).Select(w => w.T)));
        // Some walls end in a sloping ledge, whose top edge is the wall's top
        // (#135: the gem path's right wall, which the view crossed just above
        // into the right path's toxic room).
        // Only ledges standing on a wall count: pipes are sloping faces too.
        var wallEdges = new HashSet<NativeWideEdge>(walls.SelectMany(w =>
            Enumerable.Range(0, 3).Select(ei => NativeWidePowerKey(w.T[ei], w.T[(ei + 1) % 3]))));
        var ledges = Enumerable.Range(0, faces.Length)
            .Where(pi => faces[pi].Valid && !faces[pi].SemiTrans && NativeWidePowerLedge(faces[pi].T)
                && Enumerable.Range(0, 3).Any(ei => wallEdges.Contains(NativeWidePowerKey(faces[pi].T[ei], faces[pi].T[(ei + 1) % 3]))))
            .Select(pi => (Polygon: pi, faces[pi].T));
        var strip = new List<NativeWideRepair>();
        foreach (var (pi, t) in walls.Concat(ledges).Where(w => !Outdoor(w.T)))
            for (int ei = 0; ei < 3; ei++)
            {
                var a = t[ei]; var b = t[(ei + 1) % 3]; var c = t[(ei + 2) % 3];
                var e = Position(b) - Position(a);
                if (edges[NativeWidePowerKey(a, b)] != 1 || e.Length() < 200 || Math.Abs(e.Y) > e.Length() * 0.1f
                    || c.Y >= Math.Min(a.Y, b.Y) - 1 || Math.Min(a.Y, b.Y) + origin.Y < NativeWidePowerWallTop) continue;
                // Faces wind with their normal pointing out of the corridor.
                // The rise stands that far behind the wall's plane and starts
                // below its top, so where the wall goes on (above a step, or
                // in a neighbouring mesh holding the same wall) the original
                // stays in front, and no gap opens along the top.
                var behind = Vector3.Cross(e, Position(c) - Position(a));
                behind.Y = 0;
                if (behind.Length() < 1) continue;
                behind = Vector3.Normalize(behind) * NativeWidePowerSetBack - Vector3.UnitY * 2 * NativeWidePowerSetBack;
                NativeWideClipVertex Back(NativeWideClipVertex v) =>
                    v with { X = v.X + behind.X, Y = v.Y + behind.Y, Z = v.Z + behind.Z };
                strip.Clear();
                AddNativeWideSceneryStrip(strip, pi, Back(a), Back(b), Back(c), new(a.U, a.V), new(b.U, b.V),
                    new(c.U, c.V), Vector3.UnitY, distance: NativeWidePowerRise + 2 * NativeWidePowerSetBack);
                // Walls stepping back above a ledge go on behind it: the rise
                // would cut through them.
                float top = Math.Min(a.Y, b.Y) - 8;
                foreach (var r in strip)
                    if (!surface.Overlaps([r.A, r.B, r.C], NativeWidePowerStep, top)) repairs.Add(r);
            }
        for (int i = 0; i < repairs.Count; i++) repairs[i] = repairs[i] with { Boundary = true };
        // Wall ends toward the camera, the raised tops' included, so the
        // raised wall continues there too.
        int raised = repairs.Count;
        var all = walls.Concat(repairs.Take(raised).Skip(halves).Select(r => (r.Polygon, T: new[] { r.A, r.B, r.C })))
            .ToList();
        var open = NativeWidePowerEdges(faces.Select(f => f.T).Concat(all.Skip(originals).Select(w => w.T)));
        // Cells of different sizes and the raised strips' texture tiles leave
        // open edges inside a wall; only edges where the wall really ends go on.
        foreach (var w in all.Skip(walls.Count)) surface.Add(w.T);
        foreach (var (pi, t) in all)
            for (int ei = 0; ei < 3; ei++)
            {
                var a = t[ei]; var b = t[(ei + 1) % 3]; var c = t[(ei + 2) % 3];
                Vector3 pa = Position(a), e = Position(b) - pa, f = Position(c) - pa;
                if (open[NativeWidePowerKey(a, b)] != 1 || e.Length() < 100 || Math.Abs(e.Y) < e.Length() * 0.9f) continue;
                var outward = e * (Vector3.Dot(e, f) / Vector3.Dot(e, e)) - f;
                outward.Y = 0;
                if (outward.Length() < 1) continue;
                outward = Vector3.Normalize(outward);
                if (!Outdoor(t) && outward.Z < 0.3f) continue;
                // A wall ending where its mesh ends goes on in the next mesh (the
                // next room): continued, it ran on through that room's own walls
                // and slid over their fills as the camera moved (#135, 2b into 4a).
                if (!Outdoor(t) && Math.Max(a.Z, b.Z) >= meshEndZ - 8) continue;
                var beyond = (Position(a) + Position(b)) / 2 + outward * 40;
                if (surface.Overlaps([a, b, a with { X = beyond.X, Y = beyond.Y, Z = beyond.Z }])) continue;
                AddNativeWideSceneryStrip(repairs, pi, a, b, c, new(a.U, a.V), new(b.U, b.V), new(c.U, c.V),
                    outward, distance: NativeWidePowerReach);
            }
        // The side pass culls back faces, and the bands look at the paths'
        // walls from behind where the paths run side by side (#135: the
        // right branch's toxic pools seen through the gem path's and pipe
        // room's sides). Every wall gets a back: the same triangle wound the
        // other way, culled, so it only shows where the wall itself does not.
        foreach (var (pi, t) in walls.Where(w => !Outdoor(w.T)))
            repairs.Add(new(pi, t[0], t[2], t[1], Boundary: true, Culled: true));
        // After the first split, the left path's Tesla room (X 4400-6640) has
        // sloping machinery on its right instead of a wall, and the 16:9 view
        // looks over it at the right path's corridor (X 9180-11700). A dark
        // screen in the empty space between them hides each path from the
        // other: copies of a riveted wall plate (polygon 1424), mirrored cell
        // to cell and shaded dark, in the absolute plane X 8400 (#135: the
        // first screen used a hazard-striped cell, striped zigzags in the
        // bands). It goes on through the block between the two corridors at
        // the split and stands on its flat roof (Y 6200), which the right
        // corridor's camera looked over at the left room's Tesla coils.
        if (mesh is (2222, 2127, 7815, 104809))
            NativeWidePowerScreen(faces, origin, 1424, 8400, 3600, 7600, 99600, 107200, repairs);
        // At the second split the right branch's pipe room (b0, from X 13100)
        // has only pipes on its left, and the view passed through the backs of
        // the left branch's wall onto its toxic pool (#135). A screen of wall
        // plates (polygon 244) at X 12600, below the block that stands
        // between the branches at the fork, and higher beside the next room
        // (b1), where the left branch's raised walls showed until the camera
        // left b0.
        if (mesh is (1779, 1553, 13282, 73868))
        {
            NativeWidePowerScreen(faces, origin, 244, 12600, 1600, 4000, 70900, 74100, repairs);
            NativeWidePowerScreen(faces, origin, 244, 12600, 1600, 5600, 64100, 70900, repairs);
        }
        // The gem path's second room (1m) is the right branch's (0b) twin,
        // 2000 to its left, and looked between their walls at 0b's pool; 0b's
        // wall backs belong to a mesh 1m does not draw. A screen in the empty
        // strip between them (X 7900) from the mesh both rooms draw.
        if (mesh is (1030, 860, 8034, 67689))
            NativeWidePowerScreen(faces, origin, 423, 7900, 1600, 4400, 70800, 76400, repairs);
        // The same between the gem path's third room (m2) and the right
        // branch's 2b room, seen over the gem path's right wall (X 3800).
        if (mesh is (1046, 953, 705, 67537))
            NativeWidePowerScreen(faces, origin, 448, 3800, 1600, 4400, 64800, 69200, repairs);
        // And between the gem path's last rooms (m6, m7) and the right
        // branch's 6b/7b, from the mesh both draw: the right branch's wall
        // tops are raised in a mesh the gem path does not draw, so they came
        // and went with the camera zone (X 5600).
        if (mesh is (1217, 1024, 5682, 32905))
            NativeWidePowerScreen(faces, origin, 355, 5600, 1600, 4400, 37800, 47000, repairs);
        return repairs;
    }

    static void NativeWidePowerScreen(NativeWideFace[] faces, Vector3 origin, int template, float x, float y0, float y1,
        float z0, float z1, List<NativeWideRepair> repairs)
    {
        if ((uint)template >= (uint)faces.Length || !faces[template].Valid) return;
        var t = faces[template].T;
        float uLow = t.Min(v => v.U), uHigh = t.Max(v => v.U), vLow = t.Min(v => v.V), vHigh = t.Max(v => v.V);
        const float Cell = 400;
        for (float y = y0, row = 0; y < y1; y += Cell, row++)
            for (float z = z0, column = 0; z < z1; z += Cell, column++)
            {
                float ua = column % 2 == 0 ? uLow : uHigh, ub = column % 2 == 0 ? uHigh : uLow;
                float va = row % 2 == 0 ? vHigh : vLow, vb = row % 2 == 0 ? vLow : vHigh;
                NativeWideClipVertex Corner(float cy, float cz, float u, float v) =>
                    t[0] with { X = x - origin.X, Y = cy - origin.Y, Z = cz - origin.Z,
                        R = NativeWidePowerScreenShade, G = NativeWidePowerScreenShade, B = NativeWidePowerScreenShade,
                        U = u, V = v };
                var p00 = Corner(y, z, ua, va); var p01 = Corner(y, z + Cell, ub, va);
                var p10 = Corner(y + Cell, z, ua, vb); var p11 = Corner(y + Cell, z + Cell, ub, vb);
                repairs.Add(new(template, p00, p01, p11, Boundary: true));
                repairs.Add(new(template, p00, p11, p10, Boundary: true));
            }
    }

    // The completed corner's UV. Where the texture's right angle sits at another
    // corner than the face's (bevel slivers), the parallelogram rule reaches
    // past the texture tile into its neighbour in the page (#135: green toxic
    // streaks above the walls); the tile's free corner is taken instead.
    static Vector2 NativeWidePowerCornerUv(NativeWideClipVertex right, NativeWideClipVertex p, NativeWideClipVertex q)
    {
        var uv = new Vector2(p.U + q.U - right.U, p.V + q.V - right.V);
        Vector2[] used = [new(right.U, right.V), new(p.U, p.V), new(q.U, q.V)];
        Vector2 low = Vector2.Min(used[0], Vector2.Min(used[1], used[2]));
        Vector2 high = Vector2.Max(used[0], Vector2.Max(used[1], used[2]));
        if (uv == Vector2.Clamp(uv, low, high)) return uv;
        Vector2[] tile = [low, new(high.X, low.Y), new(low.X, high.Y), high];
        return tile.MaxBy(c => used.Min(u => Vector2.DistanceSquared(u, c)));
    }

    // Tall faces standing nearly upright.
    static bool NativeWidePowerWall(NativeWideClipVertex[] t)
    {
        var normal = Vector3.Cross(Position(t[1]) - Position(t[0]), Position(t[2]) - Position(t[0]));
        float low = Math.Min(t[0].Y, Math.Min(t[1].Y, t[2].Y)), high = Math.Max(t[0].Y, Math.Max(t[1].Y, t[2].Y));
        return normal.Length() >= 1 && Math.Abs(normal.Y) <= normal.Length() * 0.25f && high - low >= 300;
    }

    // Faces sloping up toward a wall's top, between upright and flat.
    static bool NativeWidePowerLedge(NativeWideClipVertex[] t)
    {
        var normal = Vector3.Cross(Position(t[1]) - Position(t[0]), Position(t[2]) - Position(t[0]));
        float low = Math.Min(t[0].Y, Math.Min(t[1].Y, t[2].Y)), high = Math.Max(t[0].Y, Math.Max(t[1].Y, t[2].Y));
        return normal.Length() >= 1 && Math.Abs(normal.Y) > normal.Length() * 0.25f
            && Math.Abs(normal.Y) < normal.Length() * 0.9f && high - low >= 150;
    }

    static Dictionary<NativeWideEdge, int> NativeWidePowerEdges(IEnumerable<NativeWideClipVertex[]> triangles)
    {
        var edges = new Dictionary<NativeWideEdge, int>();
        foreach (var t in triangles)
            for (int ei = 0; ei < 3; ei++)
            {
                var key = NativeWidePowerKey(t[ei], t[(ei + 1) % 3]);
                edges[key] = edges.GetValueOrDefault(key) + 1;
            }
        return edges;
    }

    // Raised strips are cut into texture tiles; their shared corners can differ
    // in the last float bits, so edges match on whole units.
    static NativeWideEdge NativeWidePowerKey(NativeWideClipVertex a, NativeWideClipVertex b) =>
        NativeWideEdgeKey(a with { X = MathF.Round(a.X), Y = MathF.Round(a.Y), Z = MathF.Round(a.Z) },
            b with { X = MathF.Round(b.X), Y = MathF.Round(b.Y), Z = MathF.Round(b.Z) });
}
