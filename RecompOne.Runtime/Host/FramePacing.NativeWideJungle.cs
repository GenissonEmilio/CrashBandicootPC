using System.Numerics;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Host;

public static partial class FramePacing
{
    // Jungle Rollers (level 12) has no sky: every mesh is ground, banks, trunks
    // and plants cut along the 4:3 view (the retail draw lists keep only what
    // the 4:3 camera sees), and the 16:9 view saw black past every cut. Open
    // edges are continued outward in the plane of their own face, with the
    // face's texture, as a skirt. Edges of small pieces (plants, leaves, props)
    // are left alone: their continuation would be a stretched leaf. The skirt
    // is drawn behind all real scenery, in its own depth order, so it only
    // covers what nothing else does.
    //
    // Most open edges lie where the 16:9 view never looks or where real
    // scenery always stands in front, and a skirt on each of them (some 600
    // per mesh) costs more than the whole frame. Only the polygons listed
    // below get one: those whose skirt fills side-band pixels from at least
    // one camera path point (found offline by rendering every path point).
    const float NativeWideSkirtDistance = 3200;
    const int NativeWideSkirtMinPiece = 20;
    static readonly Dictionary<NativeWideSceneryKey, List<NativeWideRepair>> _nativeWideSkirts = [];

    // Skirted polygons per mesh, by header X and Z.
    static readonly Dictionary<(int X, int Z), int[]> NativeWideJungleSkirts = new()
    {
        // 0a_cW
        [(9900, 122966)] =
        [
            6, 13, 34, 42, 60, 73, 74, 85, 86, 87, 103, 109, 162, 163, 172, 181, 183, 184, 186, 194, 198, 200,
            201, 204, 206, 255, 256, 267, 273, 276, 277, 304, 347, 348, 671, 679, 680, 681, 686, 696, 735, 756,
            852, 869, 871, 958, 961
        ],
        // 0b_cW
        [(13228, 115223)] =
        [
            0, 1, 3, 4, 5, 50, 132, 141, 156, 162, 178, 233, 254, 261, 263, 551, 603, 640, 653, 696, 742, 768,
            776, 778, 781, 825, 867, 874, 1020, 1021, 1080, 1097, 1139, 1164, 1165, 1174, 1175
        ],
        // 0c_cW
        [(11093, 108123)] =
        [
            11, 13, 15, 19, 21, 178, 179, 313, 327, 331, 334, 339, 348, 381, 386, 392, 428, 430, 443, 445, 471,
            478, 482, 594, 618, 642, 643, 644, 653, 658, 661, 668, 818, 914, 1049, 1050, 1051, 1325, 1326,
            1569, 1592, 1618, 1620
        ],
        // 0d_cW
        [(11930, 101027)] =
        [
            4, 9, 18, 22, 24, 28, 111, 120, 265, 276, 286, 412, 414, 416, 451, 458, 469, 528, 546, 575, 576,
            580, 589, 590, 592, 604, 607, 620, 728, 953, 1150, 1154, 1325, 1326, 1354
        ],
        // 0e_cW
        [(9637, 94138)] =
        [
            50, 161, 166, 188, 191, 207, 252, 259, 292, 294, 296, 540, 583, 629, 727, 729, 737, 1012, 1014,
            1342, 1387, 1580, 1581, 1582, 1583, 1584, 1635
        ],
        // 0g_cW
        [(8249, 88318)] =
        [
            92, 106, 230, 241, 281, 295, 299, 303, 306, 308, 310, 530, 725, 727, 736, 741, 755, 1041
        ],
        // 0h_cW
        [(8382, 81540)] =
        [
            63, 68, 70, 312, 316, 324, 420, 421, 437, 627, 817, 818, 820, 823, 825, 838, 912, 916, 932, 940,
            970, 990, 1000, 1022, 1024, 1058, 1069, 1200, 1204, 1205, 1233, 1258, 1455, 1457, 1459, 1502, 1509,
            1523, 1540, 1559, 1563, 1564, 1577, 1584
        ],
        // 0i_cW
        [(3936, 74831)] =
        [
            6, 99, 112, 125, 143, 282, 284, 286, 287, 322, 330, 380, 423, 425, 449, 454, 482, 505, 507, 510,
            561, 584, 585, 587, 620, 621, 800, 804, 913, 928
        ],
        // 0j_cW
        [(400, 69109)] =
        [
            2, 4, 7, 9, 92, 98, 117, 123, 143, 145, 250, 278, 280, 316, 318, 542, 602, 668, 670, 854, 1064,
            1075, 1090
        ],
        // 0k_cW
        [(-1154, 61818)] =
        [
            292, 432, 439, 928, 1010, 1103, 1394, 1398, 1459, 1475, 1485, 1512, 1530, 1617, 1636, 1641, 1660,
            1930, 1931, 1934, 1960, 2283, 2284
        ],
        // 0l_cW
        [(-2291, 53944)] =
        [
            43, 50, 75, 83, 94, 609, 611, 656, 688, 704, 778, 838, 1012, 1017
        ],
        // 0m_cW
        [(5551, 47106)] =
        [
            45, 48, 56, 71, 101, 118, 130, 162, 225, 249, 258, 331, 340, 484, 487, 570, 571, 623, 635, 637,
            1260, 1273, 1286, 1297, 1316, 1512, 1533, 1553, 1555, 1557
        ],
        // 0n_cW
        [(5551, 41045)] =
        [
            10, 11, 36, 114, 136, 330, 332, 512, 519, 522, 528
        ],
        // 0o_cW
        [(8095, 34989)] =
        [
            9, 35, 68, 218, 222, 225, 238, 240, 241, 251, 276, 297, 402, 423, 431, 457, 481, 485, 496, 554,
            566, 568, 649, 650, 664, 665, 856, 869, 873, 881, 886, 887, 890, 971, 972, 991, 997
        ],
        // 0p_cW
        [(8519, 28933)] =
        [
            66, 82, 332, 340, 611, 614, 646, 712, 733, 743, 754, 758, 824, 832
        ],
        // 0q_cW
        [(11734, 23085)] =
        [
            6, 8, 10, 12, 14, 33, 103, 111, 117, 149, 155, 163, 258, 267, 268, 309, 324, 331, 355, 406, 519,
            540, 546, 550, 578, 604, 608, 619, 686, 696, 698, 887, 1026, 1289, 1558
        ],
        // 0r_cW
        [(10886, 15757)] =
        [
            12, 98, 112, 207, 209, 236, 278, 416, 418, 452, 460, 466, 589, 593, 666, 973, 992, 1034, 1035,
            1037, 1055, 1056, 1063, 1092, 1093, 1379, 1381, 1427
        ],
        // 0s_cW
        [(9614, 7572)] =
        [
            23, 25, 27, 29, 207, 214, 236, 255, 387, 408, 433, 473, 522, 550, 566, 572, 575, 630, 631, 654,
            715, 721, 723, 736, 750, 752, 754, 758, 759, 776, 791, 798, 802, 806, 810, 815, 816, 819, 825, 826,
            837, 839, 846
        ],
        // 0t_cW
        [(9826, -811)] =
        [
            2, 12, 14, 23, 25, 27, 41, 49, 66, 72, 116, 140, 162, 169, 368, 390, 396, 399, 426, 452, 465, 578,
            584, 615, 628, 668, 681, 683, 685, 721, 725, 1064, 1065, 1088, 1261, 1263, 1274, 1275, 1343, 1368,
            1404, 1488
        ],
        // 0u_cW
        [(8279, -9403)] =
        [
            57, 59, 61, 67, 99, 106, 146, 151, 227, 268, 279, 281, 296, 917, 949, 1025, 1350, 1354
        ],
    };

    static IReadOnlyList<NativeWideRepair> NativeWideSceneRepairs(IMemory m, NativeWideWorld world)
    {
        var repairs = NativeWideAssetRepairs(m, world);
        uint level = m.ReadU32(Catalogs.Catalog.LevelIdAddr);
        if (level != 12) return repairs;
        var key = new NativeWideSceneryKey(level, m.ReadU32(world.Header), m.ReadU32(world.Header + 4),
            m.ReadU32(world.Header + 8), world.PolyCount, world.VertexCount);
        if (_nativeWideSkirts.TryGetValue(key, out var cached)) return cached;
        // The opening's hand-made bank and trunk strips keep their true depth.
        var all = new List<NativeWideRepair>(repairs.Count);
        foreach (var repair in repairs) all.Add(repair with { Boundary = true });
        int authored = all.Count;
        if (NativeWideJungleSkirts.TryGetValue(((int)m.ReadU32(world.Header), (int)m.ReadU32(world.Header + 8)),
                out var skirted))
            NativeWideSkirtRepairs(m, world, [.. skirted], all);
        PaceLog($"native-wide level={level} skirt repairs={all.Count - authored}");
        return _nativeWideSkirts[key] = all;
    }

    // skirted: polygons whose open edges get a skirt (null: all of them).
    static void NativeWideSkirtRepairs(IMemory m, NativeWideWorld world, HashSet<int>? skirted,
        List<NativeWideRepair> output)
    {
        int count = world.PolyCount;
        var triangles = new NativeWideClipVertex[count * 3];
        var edges = new Dictionary<NativeWideEdge, NativeWideEdgeOwner>();
        // Pieces: polygons joined by shared edges (union-find).
        var piece = new int[count];
        for (int pi = 0; pi < count; pi++) piece[pi] = pi;
        int Find(int p)
        {
            while (piece[p] != p) p = piece[p] = piece[piece[p]];
            return p;
        }
        for (int pi = 0; pi < count; pi++)
        {
            uint poly = world.Polygons + (uint)pi * 8;
            NativeWidePolygonVertices(FastU32(m, poly), FastU32(m, poly + 4), out int a, out int b, out int c);
            int o = pi * 3;
            triangles[o] = ReadNativeWideLocal(m, world, a);
            triangles[o + 1] = ReadNativeWideLocal(m, world, b);
            triangles[o + 2] = ReadNativeWideLocal(m, world, c);
            for (int ei = 0; ei < 3; ei++)
            {
                var edge = NativeWideEdgeKey(triangles[o + ei], triangles[o + (ei + 1) % 3]);
                if (edges.TryGetValue(edge, out var owner))
                {
                    edges[edge] = owner with { Count = owner.Count + 1 };
                    piece[Find(pi)] = Find(owner.Polygon);
                }
                else edges[edge] = new NativeWideEdgeOwner(pi, ei, 1);
            }
        }
        var pieceSize = new int[count];
        for (int pi = 0; pi < count; pi++) pieceSize[Find(pi)]++;

        // Each open vertex moves along the mean of its open edges' outward
        // directions, so neighbouring strips share their outer corners.
        // Semi-transparent materials are the cut-out leaves and fronds (their
        // textures are the only ones with transparent texels): never continued.
        var open = new List<(NativeWideEdgeOwner Owner, Vector2[] Uv)>();
        var corner = new Dictionary<Vector3, Vector3>();
        foreach (var owner in edges.Values)
        {
            if (owner.Count != 1 || pieceSize[Find(owner.Polygon)] < NativeWideSkirtMinPiece) continue;
            int o = owner.Polygon * 3;
            var a = triangles[o + owner.Edge];
            var b = triangles[o + (owner.Edge + 1) % 3];
            var c = triangles[o + (owner.Edge + 2) % 3];
            var outward = NativeWideOutward(a, b, c);
            if (outward.LengthSquared() < 1) continue;
            if (!TryNativeWideMaterial(m, world, owner.Polygon, 0, out var flags, out _,
                out short u0, out short v0, out short u1, out short v1, out short u2, out short v2)
                || !flags.Textured || flags.SemiTrans) continue;
            outward = Vector3.Normalize(outward);
            if (skirted?.Contains(owner.Polygon) ?? true) open.Add((owner, [new(u0, v0), new(u1, v1), new(u2, v2)]));
            corner[Position(a)] = corner.GetValueOrDefault(Position(a)) + outward;
            corner[Position(b)] = corner.GetValueOrDefault(Position(b)) + outward;
        }
        foreach (var (owner, uv) in open)
        {
            int o = owner.Polygon * 3;
            var a = triangles[o + owner.Edge];
            var b = triangles[o + (owner.Edge + 1) % 3];
            var c = triangles[o + (owner.Edge + 2) % 3];
            var own = Vector3.Normalize(NativeWideOutward(a, b, c));
            AddNativeWideSceneryStrip(output, owner.Polygon, a, b, c,
                uv[owner.Edge], uv[(owner.Edge + 1) % 3], uv[(owner.Edge + 2) % 3],
                NativeWideSkirtDirection(corner[Position(a)], own), NativeWideSkirtDirection(corner[Position(b)], own),
                NativeWideSkirtDistance);
        }
    }

    // A corner's mean direction; where its edges point apart (a thin spike),
    // the edge's own direction.
    static Vector3 NativeWideSkirtDirection(Vector3 sum, Vector3 own) =>
        sum.LengthSquared() < 0.25f ? own : Vector3.Normalize(sum);
}
