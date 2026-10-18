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

    // The Beach's skirted polygons per mesh, by header X and Z (see
    // NativeWideJungleSkirts): every jungle and temple void of the 16:9 view,
    // and the sky haze where the beach should go on (#137).
    static readonly Dictionary<(int X, int Z), int[]> NativeWideBeachSkirts = new()
    {
        // 0__9W
        [(8355, 119600)] =
        [
            197, 235, 372, 377, 507, 605, 616, 618, 619, 634, 643, 701, 711, 715, 721, 727, 740, 757, 760, 766,
            774, 937, 1068, 1085, 1097, 1280, 1632, 1640, 1765, 1832, 1855, 2071, 2075, 2076, 2078, 2114, 2116,
            2126
        ],
        // 1__9W
        [(8355, 113293)] =
        [
            32, 115, 128, 176, 263, 275, 276, 315, 581, 582, 1179, 1184, 1196, 1200, 1205, 1302, 1310, 1320,
            1322, 1490, 1493, 1559, 1560, 1958
        ],
        // 2__9W
        [(8355, 104536)] =
        [
            115, 235, 528, 532, 539, 540, 710, 724, 741, 927, 928, 929, 1044, 1045, 1060, 1142, 1143, 1144,
            1158, 1217, 1221, 1229, 1243, 1258, 1282, 1290, 1298, 1346, 1587, 1590, 1727, 1847, 1905, 2085,
            2248, 2249, 2251, 2283, 2321, 2329, 2339, 2341, 2349, 2360
        ],
        // 3a_9W
        [(8397, 96619)] =
        [
            170, 173, 177, 206, 209, 215, 386, 408, 418, 419, 623, 627, 629, 675, 681, 686, 743, 747, 753, 776,
            777, 809, 811, 813, 815, 1141, 1145, 1171, 1206, 1210, 1211
        ],
        // 3b_9W
        [(13535, 91328)] =
        [
            320, 428, 442, 448, 450, 451, 477, 479, 483, 520, 546, 548, 635, 655, 658, 669, 834
        ],
        // 4__9W
        [(10009, 84212)] =
        [
            14, 28, 117, 119, 123, 125, 128, 132, 229, 231, 260, 264, 335, 341, 405, 533, 564, 608, 687, 836,
            1171, 1173, 1175, 1229, 1494, 1530, 1544, 1591, 1595, 1596, 1612, 1618, 1648, 1801, 1843, 1883,
            2205
        ],
        // 5__9W
        [(14046, 75560)] =
        [
            203, 215, 216, 272, 627, 689, 867, 868, 870, 872, 885, 914, 915, 916, 989, 1260, 1404, 1425, 1427,
            1435, 1449, 1472, 1474, 1773, 1797
        ],
        // 6__9W
        [(15471, 66196)] =
        [
            5, 13, 413, 434, 439, 555, 559, 921, 926, 1026, 1030, 1031, 1085, 1104, 1105, 1115, 1148, 1307,
            1331, 1349, 1384, 1385, 1396, 1399, 1450, 1458, 1463, 1571, 1594, 1611, 1621, 1641, 1647, 1649,
            1813, 1872, 1878
        ],
        // 7__9W
        [(16297, 58416)] =
        [
            18, 52, 53, 60, 64, 76, 204, 262, 281, 521, 527, 534, 661, 668, 748, 769, 777, 778, 902, 910, 1237,
            1355, 1474, 1476, 1534, 1535, 1702, 1764, 1765, 1858, 1859, 1866, 1867, 1887, 2125, 2136, 2337,
            2343, 2347
        ],
        // 7b_9W
        [(16297, 52687)] =
        [
            105, 129, 366, 433, 497, 536, 538, 540, 640, 709, 769, 798, 1020, 1059, 1094, 1096, 1102, 1104,
            1218, 1263, 1296, 1350, 1365, 1367, 1371, 1436, 1439, 1444, 1450, 1452, 1454, 1461, 1481, 1630,
            1695, 1709, 1714, 1722, 2014, 2061
        ],
        // 8__9W
        [(15871, 46982)] =
        [
            4, 58, 284, 528, 1061, 1104, 1191, 1253, 1255, 1418, 1442, 1444, 1469, 1471, 1775
        ],
        // 9__9W
        [(16139, 39874)] =
        [
            20, 196, 237, 238, 239, 254, 259, 330, 467, 479, 487, 506, 603, 609, 636, 778, 798, 811, 827, 828,
            1032, 1217, 1228, 1395, 1417, 1453, 1480, 1489, 1543, 1670, 1674, 1676, 1677, 1691, 2050
        ],
        // e__9W
        [(8355, 130513)] =
        [
            697, 717, 719, 765, 849, 901, 923, 928, 964, 1028, 1037, 1125, 1172, 1226, 1228, 1319, 1321, 1323,
            1325, 1360, 1366, 1670, 1678, 1690, 1708, 1715, 1720, 1728, 1729, 1731, 1878, 1924, 1934, 1941,
            2031, 2044, 2046, 2049, 2054, 2079, 2081, 2087, 2129, 2218, 2316, 2324, 2348, 2362, 2506, 2507,
            2600, 2601
        ],
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
