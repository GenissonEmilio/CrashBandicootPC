using System.Numerics;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Host;

public static partial class FramePacing
{
    // Jungle Rollers (level 12), Upstream (level 15) and the Koala Kong cave
    // (level 33) have no sky: every mesh is ground, banks, trunks, plants or
    // cave rock cut along the 4:3 view (the retail
    // draw lists keep only what the 4:3 camera sees), and the 16:9 view saw
    // black past every cut. Open edges are continued outward in the plane of
    // their own face, with the face's texture, as a skirt. Edges of small
    // pieces (plants, leaves, props) are left alone: their continuation would
    // be a stretched leaf. The skirt is drawn behind all real scenery, in its
    // own depth order, so it only covers what nothing else does.
    //
    // Most open edges lie where the 16:9 view never looks or where real
    // scenery always stands in front, and a skirt on each of them (some 600
    // per mesh) costs more than the whole frame. Only the polygons listed
    // below get one: those whose skirt fills side-band pixels from at least
    // one camera path point (found offline by rendering every path point).
    const float NativeWideSkirtDistance = 3200;
    const int NativeWideSkirtMinPiece = 20;
    static readonly Dictionary<NativeWideSceneryKey, List<NativeWideRepair>> _nativeWideSkirts = [];

    // Jungle Rollers' skirted polygons per mesh, by header X and Z.
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

    // Upstream's skirted polygons, selected the same way. River water over
    // black is not a hole there: the 4:3 view shows the same river over black.
    // Water over black at the waterfalls and their top pools is.
    static readonly Dictionary<(int X, int Z), int[]> NativeWideUpstreamSkirts = new()
    {
        // 0d_fW
        [(8197, 100003)] =
        [
            5, 9, 17, 30, 71, 72, 73, 75, 190, 255, 257, 258, 297, 331, 335, 349, 353, 386, 387, 388, 397, 422,
            424, 425
        ],
        // 0e_fW
        [(8197, 91739)] =
        [
            0, 1, 103, 104, 105, 106, 110, 112, 114, 127, 131, 145, 146, 181, 185, 187, 194, 227, 234, 240,
            270, 299, 300, 666, 698, 699, 724, 730, 731, 732, 733, 876, 881, 922, 943, 1017, 1073, 1078, 1081,
            1084, 1092, 1122, 1133, 1190, 1195, 1198, 1201, 1209, 1245, 1368, 1373, 1390, 1558, 1564, 1571,
            1577
        ],
        // 0f_fW
        [(8198, 82687)] =
        [
            24, 88, 89, 90, 92, 97, 99, 100, 142, 220, 224, 229, 232, 246, 261, 299, 308, 336, 341, 349, 350,
            351, 515, 518, 542, 578, 583, 591, 593, 751, 752, 898, 903, 906, 909, 917, 946, 957, 1014, 1019,
            1022, 1025, 1033, 1062, 1084, 1252, 1265, 1273, 1274
        ],
        // 0g_fW
        [(8197, 73521)] =
        [
            5, 10, 18, 59, 60, 77, 78, 81, 82, 84, 86, 93, 94, 225, 227, 356, 415, 590, 659, 660, 661, 764,
            765, 771, 782, 788, 828, 834, 865, 894, 1236, 1240, 1296, 1355, 1414, 1416, 1426, 1483, 1540, 1546,
            1550, 1553, 1559, 1594, 1653, 1825
        ],
        // 0h_fW
        [(8197, 64476)] =
        [
            10, 11, 16, 35, 39, 41, 81, 161, 162, 163, 165, 170, 173, 290, 291, 295, 396, 401, 403, 434, 464,
            476, 502, 507, 511, 515, 516, 529, 666, 687, 744, 755, 860, 929, 931, 932, 1262, 1265, 1273, 1306,
            1337, 1339, 1356, 1479, 1500
        ],
        // 0i_fW
        [(8197, 55697)] =
        [
            10, 14, 16, 50, 56, 78, 87, 94, 95, 235, 372, 451, 452, 453, 455, 460, 463, 496, 539, 597, 602,
            632, 846, 1005, 1069, 1073, 1076, 1129, 1157, 1161, 1294, 1306, 1362, 1364, 1367, 1370, 1373, 1381,
            1411, 1413, 1418, 1420, 1438, 1440, 1442, 1470, 1492, 1494
        ],
        // 0j_fW
        [(8080, 46423)] =
        [
            29, 31, 43, 77, 88, 112, 113, 120, 124, 139, 306, 307, 323, 690, 695, 703, 704, 744, 745, 763, 766,
            767, 771, 778, 779, 1171, 1183, 1228, 1246, 1247, 1283, 1287
        ],
        // 0k_fW
        [(8197, 37530)] =
        [
            35, 114, 115, 116, 118, 123, 125, 126, 130, 138, 167, 223, 224, 229, 231, 232, 288, 290, 292, 293,
            299, 307, 311, 313, 315, 316, 317, 391, 439, 440, 441, 442, 444, 445, 451, 452, 453, 462, 478, 485,
            487, 491, 492, 500, 501, 503, 505, 693, 694, 704, 706, 762, 764, 767, 770, 773, 778, 815, 860, 863,
            865, 868, 871, 904, 917, 922, 924, 1076, 1099, 1101
        ],
        // 0l_fW
        [(8197, 28810)] =
        [
            15, 19, 21, 23, 25, 28, 59, 65, 87, 107, 109, 273, 276, 277, 281, 471, 484, 491, 501, 530, 531,
            902, 903, 907, 923, 931, 1055, 1184, 1185, 1195, 1197, 1253, 1255, 1258, 1261, 1264, 1302, 1303,
            1304, 1313, 1315, 1371, 1373, 1376, 1379, 1382, 1426, 1427, 1428, 1562
        ],
        // 0m_fW
        [(8198, 21246)] =
        [
            15, 20, 63, 66, 67, 144, 155, 180, 384, 385, 389, 506, 507, 508, 539, 541, 547, 549, 605, 616, 718,
            759, 789, 908, 909, 910, 920, 921, 922, 958, 960, 961, 983, 1034, 1162
        ],
        // 0n_fW
        [(8197, 13652)] =
        [
            35, 110, 116, 123, 163, 169, 195, 221, 222, 572, 576, 584, 612, 618, 620, 621, 822, 889, 900, 958,
            960, 963, 966, 969, 974, 1006, 1008, 1011, 1020, 1052, 1054, 1056, 1058, 1061, 1093, 1107, 1112,
            1264, 1281, 1288
        ],
        // 0o_fW
        [(8197, 4756)] =
        [
            10, 16, 18, 33, 56, 62, 93, 122, 306, 468, 490, 571, 572, 573, 574, 578, 580, 582, 592, 598, 604,
            617, 622, 627, 631, 634, 672, 674, 676, 754, 775, 924, 928, 931, 937, 972, 979, 1205, 1216, 1274,
            1276, 1279, 1282, 1285, 1323, 1339, 1486
        ],
        // 0p_fW
        [(8197, -4074)] =
        [
            2, 6, 8, 38, 43, 45, 50, 51, 54, 69, 276, 419, 497, 499, 501, 509, 523, 538, 573, 575, 576, 577,
            581, 584, 611, 612, 617, 623, 624, 625, 789, 801, 814, 815, 840, 844, 848, 849, 863, 1045, 1055,
            1056, 1111, 1113, 1116, 1119, 1122, 1127, 1160, 1161, 1171, 1172, 1173, 1230, 1232, 1235, 1238,
            1241, 1246, 1249, 1278, 1295, 1302, 1474, 1480, 1481
        ],
        // 0q_fW
        [(8197, -12905)] =
        [
            40, 92, 93, 94, 96, 101, 104, 114, 140, 217, 218, 219, 220, 224, 226, 228, 239, 240, 255, 279, 290,
            292, 293, 294, 298, 301, 334, 338, 342, 517, 541, 542, 796, 843, 845, 848, 851, 854, 862, 889, 899,
            900, 955, 957, 960, 963, 966, 1003, 1027, 1224, 1225
        ],
        // 0r_fW
        [(8197, -21736)] =
        [
            10, 14, 16, 52, 80, 99, 100, 401, 478, 482, 487, 490, 503, 507, 538, 588, 591, 595, 598, 603, 626,
            631, 639, 640, 819, 959, 963, 966, 972, 1023, 1051, 1191, 1201, 1202, 1257, 1259, 1262, 1265, 1268,
            1306, 1318, 1341
        ],
        // 0s_fW
        [(8197, -31233)] =
        [
            237, 287, 289, 290, 309, 320, 579, 590, 794
        ],
    };

    // Koala Kong's cave (level 33, one mesh): only the near floor and the
    // lava are continued; the rock is filled by the side walls of
    // NativeWideKoalaWalls (rock skirts became flat orange panels and streaks).
    static readonly Dictionary<(int X, int Z), int[]> NativeWideKoalaSkirts = new()
    {
        // 0__xW
        [(0, 0)] =
        [
            141, 267, 268
        ],
    };

    static IReadOnlyList<NativeWideRepair> NativeWideSceneRepairs(IMemory m, NativeWideWorld world)
    {
        var repairs = NativeWideAssetRepairs(m, world);
        uint level = m.ReadU32(Catalogs.Catalog.LevelIdAddr);
        var skirts = level switch
        {
            12 => NativeWideJungleSkirts, 15 => NativeWideUpstreamSkirts, 33 => NativeWideKoalaSkirts, _ => null
        };
        if (skirts == null) return repairs;
        var key = new NativeWideSceneryKey(level, m.ReadU32(world.Header), m.ReadU32(world.Header + 4),
            m.ReadU32(world.Header + 8), world.PolyCount, world.VertexCount);
        if (_nativeWideSkirts.TryGetValue(key, out var cached)) return cached;
        // Jungle Rollers' hand-made opening bank and trunk strips keep their
        // true depth. Upstream's opening bank strips overlap the retail bank
        // they continue, so they stay behind the scenery like the skirts.
        var all = new List<NativeWideRepair>(repairs.Count);
        foreach (var repair in repairs) all.Add(repair with { Boundary = level == 12 });
        int authored = all.Count;
        if (skirts.TryGetValue(((int)m.ReadU32(world.Header), (int)m.ReadU32(world.Header + 8)),
                out var skirted))
            NativeWideSkirtRepairs(m, world, [.. skirted], all, river: level == 15, planar: level == 33);
        if (level == 33) NativeWideKoalaWalls(m, world, all);
        PaceLog($"native-wide level={level} skirt repairs={all.Count - authored}");
        return _nativeWideSkirts[key] = all;
    }

    // The Koala Kong arena ends at X = -4000 and X = 4000 (lava, bank and back
    // wall all stop there), but beside the two rock pillars at its ends
    // nothing was modelled: the 16:9 view saw black around them. The pillars'
    // own faces are seen nearly edge-on, so their skirts were long streaks. A
    // rock wall in each end plane is seen almost head-on instead, and the
    // near floor runs on to it. Each is the retail texture of a source
    // polygon (pillar rock, floor) in mirrored tiles at that polygon's texel
    // scale, every corner shaded like the scenery near it (lava glow by the
    // bank, dark purple elsewhere).
    static void NativeWideKoalaWalls(IMemory m, NativeWideWorld world, List<NativeWideRepair> output)
    {
        // Scenery colours, except the grey vertices of self-coloured textures (lava, rails).
        var points = new List<(Vector3 Position, Vector3 Colour)>();
        for (int vi = 0; vi < world.VertexCount; vi++)
        {
            var v = ReadNativeWideLocal(m, world, vi);
            if (Math.Abs(v.R - v.G) < 8 && Math.Abs(v.G - v.B) < 8 && v.R > 40) continue;
            points.Add((Position(v), new Vector3(v.R, v.G, v.B)));
        }
        // Repair corners are mesh-local: the header holds the mesh translation.
        var local = new Vector3((int)m.ReadU32(world.Header), (int)m.ReadU32(world.Header + 4),
            (int)m.ReadU32(world.Header + 8));
        NativeWideKoalaPlane(m, world, output, points, 736, new Vector3(-4000, -640, -3840) - local,
            Vector3.UnitZ, 8960, Vector3.UnitY, 5440);
        NativeWideKoalaPlane(m, world, output, points, 141, new Vector3(-4000, 0, 1440) - local,
            Vector3.UnitX, 1800, Vector3.UnitZ, 3360);
        NativeWideKoalaPlane(m, world, output, points, 767, new Vector3(4000, -640, -3840) - local,
            Vector3.UnitZ, 8960, Vector3.UnitY, 5440);
        NativeWideKoalaPlane(m, world, output, points, 84, new Vector3(2200, 0, 1440) - local,
            Vector3.UnitX, 1800, Vector3.UnitZ, 3360);
    }

    static void NativeWideKoalaPlane(IMemory m, NativeWideWorld world, List<NativeWideRepair> output,
        List<(Vector3 Position, Vector3 Colour)> points, int texture, Vector3 origin,
        Vector3 alongU, float lengthU, Vector3 alongV, float lengthV)
    {
        if (!TryNativeWideMaterial(m, world, texture, 0, out _, out _,
            out short u0, out short v0, out short u1, out short v1, out short u2, out short v2)) return;
        float uMin = Math.Min(u0, Math.Min(u1, u2)), uMax = Math.Max(u0, Math.Max(u1, u2));
        float vMin = Math.Min(v0, Math.Min(v1, v2)), vMax = Math.Max(v0, Math.Max(v1, v2));
        uint poly = world.Polygons + (uint)texture * 8;
        NativeWidePolygonVertices(FastU32(m, poly), FastU32(m, poly + 4), out int a, out int b, out int c);
        Vector3 pa = Position(ReadNativeWideLocal(m, world, a));
        float area = Vector3.Cross(Position(ReadNativeWideLocal(m, world, b)) - pa,
            Position(ReadNativeWideLocal(m, world, c)) - pa).Length();
        float uvArea = Math.Abs((u1 - u0) * (v2 - v0) - (v1 - v0) * (u2 - u0));
        if (area < 1 || uvArea < 1 || uMax - uMin < 1 || vMax - vMin < 1) return;
        float density = MathF.Sqrt(uvArea / area);
        float cellU = (uMax - uMin) / density, cellV = (vMax - vMin) / density;
        int columns = (int)MathF.Ceiling(lengthU / cellU), rows = (int)MathF.Ceiling(lengthV / cellV);
        var dark = new Vector3(10, 7, 28);
        const float reach = 2500;
        Vector3 Shade(Vector3 p)
        {
            Vector3 sum = Vector3.Zero;
            float weights = 0, nearest = reach;
            foreach (var (position, colour) in points)
            {
                float d = Vector3.Distance(position, p);
                if (d >= reach) continue;
                nearest = Math.Min(nearest, d);
                float w = 1 / MathF.Max(d * d, 1);
                sum += colour * w;
                weights += w;
            }
            return weights == 0 ? dark : Vector3.Lerp(sum / weights, dark, nearest / reach);
        }
        Vector3 At(int i, int j) => origin + alongU * (i * cellU) + alongV * (j * cellV);
        var shade = new Vector3[columns + 1, rows + 1];
        for (int i = 0; i <= columns; i++) for (int j = 0; j <= rows; j++) shade[i, j] = Shade(At(i, j));
        for (int i = 0; i < columns; i++) for (int j = 0; j < rows; j++)
        {
            NativeWideClipVertex Corner(int di, int dj)
            {
                float u = ((i + di) & 1) == 0 ? uMin : uMax, v = ((j + dj) & 1) == 0 ? vMax : vMin;
                var p = At(i + di, j + dj);
                var colour = shade[i + di, j + dj];
                return new(p.X, p.Y, p.Z, colour.X, colour.Y, colour.Z, u, v);
            }
            output.Add(new(texture, Corner(0, 0), Corner(1, 0), Corner(1, 1)));
            output.Add(new(texture, Corner(0, 0), Corner(1, 1), Corner(0, 1)));
        }
    }

    // skirted: polygons whose open edges get a skirt (null: all of them).
    // river (Upstream): polygons with animated water vertices are never
    // continued: the river keeps its width, and a continued waterfall lip
    // reads as a flat blue panel beside the bank. Neither are compact pieces
    // (the tiki totems), whose cut sides would become long stretched stripes.
    // planar (Koala Kong): skirts keep the source face's texel scale (see
    // AddNativeWidePlanarStrip); the cave's small rock faces fan their skirts
    // far apart, and the plain strip turned them into streaks.
    const float NativeWideSkirtMinExtent = 1500;
    static void NativeWideSkirtRepairs(IMemory m, NativeWideWorld world, HashSet<int>? skirted,
        List<NativeWideRepair> output, bool river = false, bool planar = false)
    {
        int count = world.PolyCount;
        var triangles = new NativeWideClipVertex[count * 3];
        var water = new bool[count];
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
            if (river)
                water[pi] = ((FastU32(m, world.Vertices + (uint)a * 8 + 4) | FastU32(m, world.Vertices + (uint)b * 8 + 4)
                    | FastU32(m, world.Vertices + (uint)c * 8 + 4)) & 1) != 0;
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
        var low = new Vector3[count];
        var high = new Vector3[count];
        Array.Fill(low, new Vector3(float.MaxValue));
        Array.Fill(high, new Vector3(float.MinValue));
        for (int pi = 0; pi < count; pi++)
        {
            int root = Find(pi);
            pieceSize[root]++;
            for (int k = 0; k < 3; k++)
            {
                low[root] = Vector3.Min(low[root], Position(triangles[pi * 3 + k]));
                high[root] = Vector3.Max(high[root], Position(triangles[pi * 3 + k]));
            }
        }

        // Each open vertex moves along the mean of its open edges' outward
        // directions, so neighbouring strips share their outer corners.
        // Semi-transparent materials are the cut-out leaves and fronds (their
        // textures are the only ones with transparent texels): never continued.
        var open = new List<(NativeWideEdgeOwner Owner, Vector2[] Uv)>();
        var corner = new Dictionary<Vector3, Vector3>();
        foreach (var owner in edges.Values)
        {
            int piece0 = Find(owner.Polygon);
            if (owner.Count != 1 || water[owner.Polygon] || pieceSize[piece0] < NativeWideSkirtMinPiece) continue;
            var extent = high[piece0] - low[piece0];
            if (river && MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z)) < NativeWideSkirtMinExtent) continue;
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
            if (planar)
            {
                AddNativeWidePlanarStrip(output, owner.Polygon, a, b, c,
                    uv[owner.Edge], uv[(owner.Edge + 1) % 3], uv[(owner.Edge + 2) % 3],
                    NativeWideSkirtDirection(corner[Position(a)], own), NativeWideSkirtDirection(corner[Position(b)], own),
                    NativeWideSkirtDistance);
                continue;
            }
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
