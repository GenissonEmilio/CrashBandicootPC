using System.Numerics;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Host;

public static partial class FramePacing
{
    // Pinstripe's office (level 8, one mesh) is seen through its missing
    // front wall. Wooden pillars stand at both ends (X ±2000..±2400): a pair
    // at the window wall (Z 393..793), a pair by the camera (Z 3193..3593).
    // Their faces toward the camera were cut by the 4:3 cull: the near pair
    // kept only its lowest rows, the left window pillar lost its bottom row
    // (#116). Every pillar is built the same, so the missing rows are the
    // matching rows of another pillar, moved along Z; a half cell gets its
    // missing corner. Behind the window, the round generator room's wall
    // lacks the segments between its left end and the window, which the
    // intro flight sees: they are the right segments reflected about the
    // room's axis (X -4). The sky in the skylights is the background fill
    // (NativeWideSkyFillOf), not scenery.
    const float NativeWidePinstripeAxis = -4;
    // Absolute Z of the near pillars' fronts and X of their outer edges.
    const float NativeWidePinstripeFront = 3593;
    const float NativeWidePinstripeEdge = 2400;

    static List<NativeWideRepair> NativeWidePinstripeRepairs(IMemory m, NativeWideWorld world)
    {
        var repairs = new List<NativeWideRepair>();
        if (world.PolyCount != 1092 || world.VertexCount != 1034 || m.ReadU32(world.Header) != 0
            || m.ReadU32(world.Header + 4) != 1599 || (int)m.ReadU32(world.Header + 8) != -1199) return repairs;
        var faces = NativeWideFacesOf(m, world);
        // Shared corners take the colour the original vertex there has.
        var colours = new Dictionary<Vector3, NativeWideClipVertex>();
        for (int vi = 0; vi < world.VertexCount; vi++)
        {
            var v = ReadNativeWideLocal(m, world, vi);
            colours.TryAdd(Position(v), v);
        }
        NativeWideClipVertex Shade(NativeWideClipVertex v) =>
            colours.TryGetValue(Position(v), out var o) ? v with { R = o.R, G = o.G, B = o.B } : v;
        void Add(int pi, NativeWideClipVertex a, NativeWideClipVertex b, NativeWideClipVertex c) =>
            repairs.Add(new(pi, Shade(a), Shade(b), Shade(c)));

        // Half cells: near pillars' fronts (left row 1, right row 0), the left
        // window pillar's row 1 and the generator room's lowest left segment,
        // whose new corner also colours the reflected segment beside it.
        foreach (int pi in (int[])[383, 384, 372, 1057])
            if (faces[pi].Valid && NativeWideOppositeCorner(faces[pi].T, out var p, out var q, out var corner))
            {
                Add(pi, p, q, corner);
                colours.TryAdd(Position(corner), corner);
            }
        // Rows 2 and 3 of the near pillars' fronts from the window pillars
        // (the right one is 8 units narrower at the top), row 0 of the left
        // window pillar from the near one.
        void Move(int[] polygons, float dz, float snapX = float.NaN)
        {
            foreach (int pi in polygons)
            {
                if (!faces[pi].Valid) continue;
                var t = faces[pi].T;
                NativeWideClipVertex Moved(NativeWideClipVertex v) => v with
                {
                    X = !float.IsNaN(snapX) && Math.Abs(v.X - snapX) <= 8 ? snapX : v.X,
                    Z = v.Z + dz,
                };
                Add(pi, Moved(t[0]), Moved(t[1]), Moved(t[2]));
            }
        }
        Move([368, 369, 370, 371], 2800);
        Move([329, 330, 331, 332], 2800, 2400);
        Move([381, 382], -2800);
        // Nothing stands beyond the near pillars' outer edges (X ±2400), and
        // from the arena's ends the 16:9 view looks past them low down. Their
        // completed fronts are reflected about that edge, as if the front wall
        // went on: the texture and colours continue across it.
        float front = NativeWidePinstripeFront - (int)m.ReadU32(world.Header + 8);
        var near = new List<NativeWideRepair>();
        foreach (int pi in (int[])[381, 382, 383, 384, 389, 390])
            if (faces[pi].Valid) near.Add(new(pi, faces[pi].T[0], faces[pi].T[1], faces[pi].T[2]));
        near.AddRange(repairs.Where(r => r.A.Z == front && r.B.Z == front && r.C.Z == front));
        foreach (var r in near)
        {
            float edge = 2 * NativeWidePinstripeEdge * Math.Sign(r.A.X + r.B.X + r.C.X);
            repairs.Add(r with { A = r.A with { X = edge - r.A.X }, B = r.B with { X = edge - r.B.X },
                C = r.C with { X = edge - r.C.X } });
        }
        // Generator room, rows Y -401..919 / 919..2239 / 2239..3559: the right
        // wall's segments from the window (X 2000, Z 393) to 44° and the top
        // row's next one, reflected. Their window corner meets the left
        // window pillar at X -2000. The left side is lit darker: a new corner
        // takes the colour of the original corner it shares a triangle with.
        float axis = 2 * NativeWidePinstripeAxis;
        foreach (int pi in (int[])[1064, 1065, 1062, 1063, 1022, 1023, 1024, 1025])
        {
            if (!faces[pi].Valid) continue;
            NativeWideClipVertex[] t = [.. faces[pi].T.Select(v =>
            {
                float x = axis - v.X;
                return Shade(v with { X = Math.Abs(x + 2008) < 1 ? -2000 : x });
            })];
            int lit = Array.FindIndex(t, v => colours.ContainsKey(Position(v)));
            if (lit < 0) continue;
            for (int k = 0; k < 3; k++)
                if (!colours.ContainsKey(Position(t[k]))) t[k] = t[k] with { R = t[lit].R, G = t[lit].G, B = t[lit].B };
            repairs.Add(new(pi, t[0], t[1], t[2]));
        }
        return repairs;
    }
}
