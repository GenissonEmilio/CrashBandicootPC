using System.Numerics;
using RecompOne.Runtime.Hle;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Host;

public static partial class FramePacing
{
    // N. Sanity Beach (level 9) is the first level players see, and its jungle
    // and temple scenery was culled per triangle to the 4:3 view all along the
    // path. Meshes are identified by polygon and vertex count and absolute
    // header X / Z; boxes and ranges are absolute.
    static NativeWideCopies? NativeWideBeachCopies(NativeWideWorld world, int x, int z) =>
        (world.PolyCount, world.VertexCount, x, z) switch
        {
            // The temple stairs: the terraces either side (X 3597-5997 and
            // 9197-11597, 800 cells) lost cells near their outer ends, and the
            // pyramid itself ends where the 16:9 view still looks past it. Every
            // terrace front and top row is completed and continues outward.
            (1217, 1310, 8397, 96619) => new([], [],
            [
                new(new(3590, 10860, 95200), new(6000, 14868, 98900), 800, 397, 5997),
                new(new(9170, 10860, 95200), new(11600, 14868, 98900), 800, 9197, 14797),
            ]),
            // The stairs' lowest terrace and the courtyard in front of them. The
            // terrace's front (Z 99992) kept only its upper half row on the left,
            // and its top only the back row; both are copied into the missing
            // row. The courtyard's rubble mound has a gap left of the stairs; the
            // right one is its mirror image about the stairs' axis (X 7595).
            // The ground at the left pillar's foot continues under the mound,
            // showing only where it has gaps.
            (2620, 3192, 8355, 104536) => new(
            [
                new(new(10000, 8900, 100000), new(10800, 9800, 100944), Vector3.Zero, MirrorX: 7595, Opaque: true),
            ],
            [new(new(5590, 8940, 100390), new(6000, 9180, 101200), -Vector3.UnitX, 2000)],
            [
                .. NativeWideBeachTerrace(3590, 6020, 395, 5995),
                .. NativeWideBeachTerrace(9190, 11600, 9195, 14795),
            ],
            // The tapered pillar left of the stairs (axis X 6395) lost the upper
            // left of its front face and half of its top. The front's last
            // half cells are completed, the top's missing front half repeats its
            // back half, and the right half is reflected onto the left.
            Symmetry: new(6395,
                [new(596, new(6115, 11353, 101072), false), new(603, new(6395, 12153, 101040), true)],
                [new(594, new(0, 0, 240), true), new(595, new(0, 0, 240), true)],
                [601, 602, 603, 594, 595])),
            // The start: the beach's sand is a lattice of 400-unit cells, the
            // rows nearest the camera in their own transition tiles. Both sides
            // were cut along the 4:3 frustum and continue as rising dunes.
            (2664, 3054, 8355, 130513) => new([], [], Terrain: new(new(-65536, -65536, 131400),
                new(65536, 65536, 136000), NativeWideBeachSand, 8)),
            _ => null,
        };

    static readonly int[] NativeWideBeachSand =
    [
        344, 346, 438, 440, 593, 595, 597, 599, 601, 603, 605, 607, 609, 611, 613, 615,
        617, 619, 621, 623, 625, 627, 629, 631, 633, 635, 637, 639,
    ];

    // One side of the lowest terrace: faces between absolute X xMin and xMax,
    // filled from X from to to.
    static NativeWideGrid[] NativeWideBeachTerrace(float xMin, float xMax, float from, float to) =>
    [
        new(new(xMin, 9913, 99200), new(xMax, 10873, 99200), 800, from, to),
        new(new(xMin, 9433, 99992), new(xMax, 9913, 99992), 800, from, to, Repeat: [-480]),
        new(new(xMin, 9913, 99200), new(xMax, 9913, 99600), 800, from, to, Repeat: [392]),
        new(new(xMin, 10873, 98770), new(xMax, 10873, 99200), 800, from, to),
    ];

    // A jungle's canopy tile (4-bit): texture page EID, 64-halfword segment,
    // CLUT column and row inside the page, and the tile's opaque interior.
    readonly record struct NativeWideCanopyTile(uint Page, int Segment, int ClutColumn, int ClutRow,
        float ULow, float UHigh, float VLow, float VHigh);

    // The same 64x64 tile, U 128-191, in each level's own copy of the page.
    static NativeWideCanopyTile? NativeWideCanopyOf(uint level) => level switch
    {
        // N. Sanity Beach: Ju59T, V 64-127 (page 0x1A, CLUT 0x402D in game).
        9 => new(0x5AF0A4EFu, 2, 13, 0, 132, 190, 77, 120),
        // Jungle Rollers: Ju5cT, V 32-95, used by every mesh of the level.
        12 => new(0x5AF0A66Fu, 0, 7, 0, 132, 190, 45, 88),
        _ => null,
    };

    // Jungles without sky (N. Sanity Beach past the courtyard, all of Jungle
    // Rollers): beyond their culled edges the 16:9 view saw the black
    // background. Behind all scenery, a camera-centred cylinder of the jungle's
    // canopy tile (mirrored so it tiles without seams) stands in for the forest
    // that the 4:3 view never needed. Its page is looked up by EID, so the
    // cylinder only draws while the page is resident; the tile's fringe has
    // transparent texels, so only its opaque interior is used.
    static void AddNativeWideCanopy(IMemory m, NativeWideCanopyTile tile, int worldCount, short[] matrix,
        int projection, int screenX, int screenY, int drawX, int drawY, float viewCenterX, float viewCenterY,
        float coreHalf, float wideHalf, float halfHeight)
    {
        bool scenery = false;
        for (int wi = 0; wi < worldCount; wi++)
        {
            var world = _nativeWideWorldPool[wi];
            if (world.PolyCount == 0) continue;
            if (FastU32(m, world.Header + 0x1Cu) != 0) return;
            scenery = true;
        }
        if (!scenery) return;
        uint page = NativeWideTpageWord(m, tile.Page);
        if (page == 0) return;
        var flags = new PrimFlags
        {
            Textured = true, Gouraud = true, WideMode = WidePrimitiveMode.BackdropSides,
            TPage = (ushort)(((int)page & 0x1C) | tile.Segment),
            Clut = (ushort)(tile.ClutColumn | tile.ClutRow << 6 | (int)((page >> 4) & 0xFFF0u)),
        };
        float uLow = tile.ULow, uHigh = tile.UHigh;
        float vLow = tile.VLow + (page & 0x80u), vHigh = tile.VHigh + (page & 0x80u);
        const int columns = 24;
        const float radius = 3000;
        float step = MathF.Tau / columns, height = radius * step;
        // Camera axes in world space: rows of the rotation.
        float forwardX = matrix[6], forwardZ = matrix[8];
        float forwardLength = MathF.Max(1, MathF.Sqrt(forwardX * forwardX + forwardZ * forwardZ));
        Span<NativeWideClipVertex> corners = stackalloc NativeWideClipVertex[3];
        NativeWideClipVertex Corner(float angle, float y, float u, float v)
        {
            float x = radius * MathF.Sin(angle), z = -radius * MathF.Cos(angle);
            return new((matrix[0] * x + matrix[1] * y + matrix[2] * z) / 4096f,
                (matrix[3] * x + matrix[4] * y + matrix[5] * z) / 4096f,
                (matrix[6] * x + matrix[7] * y + matrix[8] * z) / 4096f, 128, 128, 128, u, v);
        }
        for (int c = 0; c < columns; c++)
        {
            float a0 = c * step, a1 = a0 + step, middle = a0 + step / 2;
            // Only columns towards the view.
            if ((MathF.Sin(middle) * forwardX - MathF.Cos(middle) * forwardZ) / forwardLength < -0.2f) continue;
            float ua = (c & 1) == 0 ? uLow : uHigh, ub = (c & 1) == 0 ? uHigh : uLow;
            for (int r = -10; r < 10; r++)
            {
                float y0 = r * height, y1 = y0 + height;
                float va = (r & 1) == 0 ? vHigh : vLow, vb = (r & 1) == 0 ? vLow : vHigh;
                var p00 = Corner(a0, y0, ua, va); var p10 = Corner(a1, y0, ub, va);
                var p01 = Corner(a0, y1, ua, vb); var p11 = Corner(a1, y1, ub, vb);
                corners[0] = p00; corners[1] = p10; corners[2] = p11;
                AddNativeWideClippedTriangle(corners, projection, screenX, screenY, drawX, drawY,
                    viewCenterX, viewCenterY, coreHalf, wideHalf, halfHeight, flags, true, _nativeWideOpaque, _nativeWideTransparent);
                corners[0] = p00; corners[1] = p11; corners[2] = p01;
                AddNativeWideClippedTriangle(corners, projection, screenX, screenY, drawX, drawY,
                    viewCenterX, viewCenterY, coreHalf, wideHalf, halfHeight, flags, true, _nativeWideOpaque, _nativeWideTransparent);
            }
        }
    }
}
