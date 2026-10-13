using System.Numerics;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Host;

public static partial class FramePacing
{
    // Jungle Rollers (level 12), Upstream (level 15), Boulder Dash (level 19)
    // and the Koala Kong cave (level 33) have no sky: every mesh is ground,
    // banks, trunks, plants or cave rock cut along the 4:3 view (the retail
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

    // Boulder Dash (level 19) is a canyon of rock walls with temple gates and
    // the mushroom path: all of it cut along the 4:3 view like Jungle Rollers.
    // Skirted polygons selected the same way, counting only the side-band
    // voids beside scenery (a pit seen at the 4:3 edge stays a pit).
    static readonly Dictionary<(int X, int Z), int[]> NativeWideBoulderDashSkirts = new()
    {
        // 0a_jW
        [(8811, 122898)] =
        [
            272, 386, 456, 476, 478, 597, 706, 730, 760, 775, 787, 789, 878, 879, 903, 951, 959, 966, 967, 999,
            1046, 1052, 1061, 1067, 1111, 1119, 1125, 1139, 1146, 1154, 1164, 1168, 1169, 1170, 1216, 1233,
            1268, 1275, 1286, 1309, 1394, 1404, 1512, 1514, 1563, 1596
        ],
        // 0b_jW
        [(10653, 114543)] =
        [
            2029, 2034, 2037, 2092, 2114, 2118, 2124, 2126, 2128, 2163, 2197, 2260, 2262, 2288, 2423, 2442,
            2453, 2454, 2523, 2550, 2588, 2642
        ],
        // 0c_jW
        [(11093, 106153)] =
        [
            151, 237, 362, 364, 391, 404, 406, 426, 502, 880, 891, 1798, 1822, 1828, 1835, 1865, 1965, 1977,
            1990, 1992, 2007, 2028, 2033, 2162, 2180, 2183, 2309, 2323, 2342, 2375, 2388, 2397, 2398, 2652,
            2656, 2661
        ],
        // 0d_jW
        [(9156, 97426)] =
        [
            178, 192, 232, 233, 238, 389, 560, 671, 862, 883, 971, 972, 1039, 1100, 1119, 1121, 1125, 1151,
            1152, 1153, 1324, 1380, 1382, 1396, 1417, 1419, 1423, 1439, 1441, 1449, 1450, 1451, 1456, 1460,
            1514, 1515, 1660
        ],
        // 0e_jW
        [(10232, 88296)] =
        [
            80, 208, 213, 249, 262, 285, 332, 345, 346, 421, 524, 542, 549, 562, 565, 567, 582, 592, 608, 844,
            872, 879, 881, 1086, 1197, 1210, 1211, 1291, 1295
        ],
        // 0f_jW
        [(9934, 79079)] =
        [
            316, 317, 344, 459, 564, 635, 636, 773, 794, 802, 803, 804, 818, 840, 868, 884, 900, 948, 952, 953,
            961, 1109, 1110, 1184, 1208, 1246, 1302, 1321, 1330, 1357, 1359, 1360, 1364
        ],
        // 0g_jW
        [(10067, 69372)] =
        [
            170, 174, 176, 180, 185, 223, 224, 798, 854, 863, 868, 877, 878, 884, 886, 956, 1131, 1166, 1171,
            1174, 1228, 1248, 1263, 1296, 1300, 1528, 1529, 1544, 1545
        ],
        // 0h_jW
        [(7602, 60174)] =
        [
            205, 214, 665, 772, 788, 798, 799, 800, 868, 884, 889, 891, 945
        ],
        // 0i_jW
        [(9119, 51470)] =
        [
            224, 280, 284, 300, 302, 350, 351, 352, 403, 417, 421, 471, 579, 600, 601, 697, 759, 795, 833, 849,
            859, 867, 871, 872, 893, 895, 1153, 1166, 1170
        ],
        // 0j_jW
        [(7564, 42535)] =
        [
            55, 99, 191, 193, 197, 199, 203, 205, 207, 209, 211, 231, 267, 321, 602, 682, 684, 902, 963, 965,
            1013, 1031, 1112
        ],
        // 0k_jW
        [(7814, 33836)] =
        [
            240, 245, 248, 667, 722, 724, 934, 964, 965, 1048, 1050, 1051, 1079, 1119, 1183, 1202, 1204, 1225,
            1232, 1233, 1234, 1249, 1256
        ],
        // 0l_jW
        [(7235, 24831)] =
        [
            216, 217, 356, 369, 461, 471, 486, 852, 893, 904, 905, 906, 911, 929, 1008, 1010, 1096, 1116, 1171,
            1175, 1197, 1199, 1212, 1213, 1420, 1421
        ],
        // 0m_jW
        [(7235, 15638)] =
        [
            239, 289, 294, 297, 377, 391, 421, 425, 491, 493, 689, 694, 809, 813, 826, 828, 832, 847, 849, 857,
            858, 859, 904, 932, 1022, 1038, 1039, 1067
        ],
        // 0n_jW
        [(8095, 6660)] =
        [
            50, 53, 61, 278, 292, 296, 346, 474, 475, 566, 608, 622, 658, 692, 895
        ],
        // 0o_jW
        [(8519, -2452)] =
        [
            340, 343, 352, 356, 363, 377, 446, 451, 457, 459, 463, 475, 504, 506, 508, 519, 520, 522, 624, 626,
            643, 646, 655, 659, 679, 681, 803, 804, 999, 1019, 1053, 1054, 1055, 1136, 1137, 1166, 1330, 1363,
            1364, 1402, 1411
        ],
        // 0p_jW
        [(10050, -11220)] =
        [
            504, 684, 721, 726, 729, 783, 803, 859, 863, 884, 886, 899, 900, 930, 1038, 1130, 1193, 1213, 1215,
            1236, 1244, 1245, 1246, 1342, 1343
        ],
        // 0q_jW
        [(9202, -19839)] =
        [
            227, 232, 313, 316, 330, 332, 334, 336, 340, 342, 344, 346, 348, 366, 402, 854, 859, 862, 913, 978,
            991, 1072, 1077
        ],
        // 0r_jW
        [(7930, -28500)] =
        [
            0, 8, 12, 28, 131, 304, 486, 491, 572, 593, 772, 827, 831, 845, 847, 851, 885, 886, 887, 1009,
            1125, 1156, 1162, 1164, 1198
        ],
        // 0s_jW
        [(8142, -37087)] =
        [
            134, 228, 253, 255, 265, 268, 304, 367, 380, 449, 471, 478, 517, 552, 556, 566, 726, 754, 761, 763,
            954, 1063, 1077, 1078, 1144, 1157, 1161
        ],
        // 0t_jW
        [(8279, -45679)] =
        [
            141, 187, 471, 474, 485, 582, 653, 679, 791, 793, 812, 819, 820, 821, 857, 886, 905, 916, 920,
            1060, 1083, 1089, 1109, 1122, 1131
        ],
        // 0u_jW
        [(8279, -55131)] =
        [
            647, 652, 663, 814, 1048, 1053, 1056, 1110, 1132, 1136, 1142, 1144, 1146, 1181, 1215, 1269, 1301,
            1381, 1434, 1463, 1522, 1565, 1577, 1582
        ],
        // 0v_jW
        [(8279, -65430)] =
        [
            300, 341, 352, 353, 392, 399, 482, 485, 495, 499, 505, 509, 513, 515, 517, 535, 1154, 1487
        ],
    };

    // Boulder Dash's three temple gates (a boulder waits on each) stand in a
    // wall across the whole canyon: a 45-degree ramp of three 400 rows, then
    // two 800 bands of wall, in 800 cells except one 1600 cell at each outer
    // end. The 4:3 cull removed or halved some of those end cells, mostly on
    // the right, and the 16:9 view saw black past the wall's corners. A half
    // end cell is completed; a missing one is the other end's cell mirrored
    // onto it; where both are missing (the third gate's top band), the next
    // inner cell is stretched over each half of the end. These cells keep their
    // true depth: a skirt of some nearer rock would otherwise win the corner.
    // Absolute wall Z and X of the left end's outer and inner edges and of the
    // right end's inner and outer edges, by mesh header X and Z.
    readonly record struct NativeWideGate(float Wall, float OuterLeft, float InnerLeft, float InnerRight,
        float OuterRight);
    static readonly Dictionary<(int X, int Z), NativeWideGate> NativeWideBoulderDashGates = new()
    {
        [(8279, -65430)] = new(-62806, 3591, 5199, 12399, 13999),  // 0v: the first gate
        [(9202, -19839)] = new(-19607, 3594, 5194, 12394, 14002),  // 0q: the second gate
        [(7564, 42535)] = new(43599, 3588, 5196, 12396, 14004),    // 0j: the third gate
    };

    static void NativeWideBoulderDashGateEnds(IMemory m, NativeWideWorld world, NativeWideGate gate,
        List<NativeWideRepair> output)
    {
        var origin = new Vector3((int)m.ReadU32(world.Header), (int)m.ReadU32(world.Header + 4),
            (int)m.ReadU32(world.Header + 8));
        // Cells by side (0 left end, 1 right end, 2 left end's inner neighbour,
        // 3 right end's inner neighbour) and row (absolute Y range).
        var cells = new Dictionary<(int Side, float Low, float High), List<NativeWideRepair>>();
        float[] x0 = [gate.OuterLeft, gate.InnerRight, gate.InnerLeft, gate.InnerRight - 800];
        float[] x1 = [gate.InnerLeft, gate.OuterRight, gate.InnerLeft + 800, gate.InnerRight];
        for (int pi = 0; pi < world.PolyCount; pi++)
        {
            uint poly = world.Polygons + (uint)pi * 8;
            NativeWidePolygonVertices(FastU32(m, poly), FastU32(m, poly + 4), out int a, out int b, out int c);
            if (!TryNativeWideMaterial(m, world, pi, 0, out _, out _,
                out short u0, out short v0, out short u1, out short v1, out short u2, out short v2)) continue;
            NativeWideClipVertex[] t = [ReadNativeWideLocal(m, world, a) with { U = u0, V = v0 },
                ReadNativeWideLocal(m, world, b) with { U = u1, V = v1 },
                ReadNativeWideLocal(m, world, c) with { U = u2, V = v2 }];
            float minX = t.Min(v => v.X) + origin.X, maxX = t.Max(v => v.X) + origin.X;
            float minZ = t.Min(v => v.Z) + origin.Z, maxZ = t.Max(v => v.Z) + origin.Z;
            if (minZ < gate.Wall - 1 || maxZ > gate.Wall + 1300 || t.Max(v => v.Y) - t.Min(v => v.Y) < 300) continue;
            for (int side = 0; side < 4; side++)
            {
                // Only triangles spanning the whole cell: no trims or details.
                if (minX < x0[side] - 2 || maxX > x1[side] + 2 || maxX - minX < x1[side] - x0[side] - 16) continue;
                var key = (side, t.Min(v => v.Y) + origin.Y, t.Max(v => v.Y) + origin.Y);
                if (!cells.TryGetValue(key, out var cell)) cells[key] = cell = [];
                cell.Add(new(pi, t[0], t[1], t[2], true));
            }
        }
        // Copies a cell with its X range from0..from1 mapped onto to0..to1.
        void Map(List<NativeWideRepair> source, float from0, float from1, float to0, float to1)
        {
            float scale = (to1 - to0) / (from1 - from0);
            NativeWideClipVertex Move(NativeWideClipVertex v) =>
                v with { X = to0 - origin.X + (v.X + origin.X - from0) * scale };
            foreach (var r in source) output.Add(r with { A = Move(r.A), B = Move(r.B), C = Move(r.C) });
        }
        var whole = new Dictionary<(int Side, float Low, float High), List<NativeWideRepair>>();
        foreach (var (key, cell) in cells)
        {
            if (cell.Count == 1 && NativeWideOppositeCorner([cell[0].A, cell[0].B, cell[0].C],
                    out var p, out var q, out var corner))
            {
                cell.Add(new(cell[0].Polygon, p, q, corner, true));
                if (key.Side < 2) output.Add(cell[^1]);
            }
            if (cell.Count == 2) whole[key] = cell;
        }
        foreach (var (low, high) in whole.Keys.Select(k => (k.Low, k.High)).Distinct().ToList())
        {
            bool left = whole.TryGetValue((0, low, high), out var l);
            bool right = whole.TryGetValue((1, low, high), out var r);
            if (left && !right)
                Map(l!, gate.OuterLeft, gate.InnerLeft, gate.OuterRight, gate.InnerRight);
            else if (right && !left)
                Map(r!, gate.InnerRight, gate.OuterRight, gate.InnerLeft, gate.OuterLeft);
            else if (!left && !right)
            {
                float midLeft = (gate.OuterLeft + gate.InnerLeft) / 2, midRight = (gate.InnerRight + gate.OuterRight) / 2;
                if (whole.TryGetValue((2, low, high), out var nl))
                {
                    Map(nl, gate.InnerLeft, gate.InnerLeft + 800, gate.OuterLeft, midLeft);
                    Map(nl, gate.InnerLeft, gate.InnerLeft + 800, midLeft, gate.InnerLeft);
                }
                if (whole.TryGetValue((3, low, high), out var nr))
                {
                    Map(nr, gate.InnerRight - 800, gate.InnerRight, gate.InnerRight, midRight);
                    Map(nr, gate.InnerRight - 800, gate.InnerRight, midRight, gate.OuterRight);
                }
            }
        }
    }

    static IReadOnlyList<NativeWideRepair> NativeWideSceneRepairs(IMemory m, NativeWideWorld world)
    {
        var repairs = NativeWideAssetRepairs(m, world);
        uint level = m.ReadU32(Catalogs.Catalog.LevelIdAddr);
        var skirts = level switch
        {
            12 => NativeWideJungleSkirts, 15 => NativeWideUpstreamSkirts, 19 => NativeWideBoulderDashSkirts,
            33 => NativeWideKoalaSkirts, _ => null
        };
        if (skirts == null && level != 18) return repairs;
        var key = new NativeWideSceneryKey(level, m.ReadU32(world.Header), m.ReadU32(world.Header + 4),
            m.ReadU32(world.Header + 8), world.PolyCount, world.VertexCount);
        if (_nativeWideSkirts.TryGetValue(key, out var cached)) return cached;
        // Jungle Rollers' hand-made opening bank and trunk strips keep their
        // true depth. Upstream's opening bank strips overlap the retail bank
        // they continue, so they stay behind the scenery like the skirts.
        var all = new List<NativeWideRepair>(repairs.Count);
        foreach (var repair in repairs) all.Add(repair with { Boundary = level == 12 });
        int authored = all.Count;
        if (level == 19 && NativeWideBoulderDashGates.TryGetValue(((int)m.ReadU32(world.Header),
                (int)m.ReadU32(world.Header + 8)), out var gate))
            NativeWideBoulderDashGateEnds(m, world, gate, all);
        if (skirts != null && skirts.TryGetValue(((int)m.ReadU32(world.Header), (int)m.ReadU32(world.Header + 8)),
                out var skirted))
            NativeWideSkirtRepairs(m, world, [.. skirted], all, river: level == 15, planar: level == 33);
        if (level == 33) NativeWideKoalaWalls(m, world, all);
        if (level == 18) NativeWideGateRepairs(m, world, all);
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
    // edgeReach (The Great Gate): single open edges, by polygon * 3 + edge,
    // each with its own skirt length; when set, only these are continued.
    const float NativeWideSkirtMinExtent = 1500;
    static void NativeWideSkirtRepairs(IMemory m, NativeWideWorld world, HashSet<int>? skirted,
        List<NativeWideRepair> output, bool river = false, bool planar = false,
        Dictionary<int, float>? edgeReach = null)
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
            if (edgeReach?.ContainsKey(owner.Polygon * 3 + owner.Edge) ?? skirted?.Contains(owner.Polygon) ?? true)
                open.Add((owner, [new(u0, v0), new(u1, v1), new(u2, v2)]));
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
                edgeReach?.GetValueOrDefault(owner.Polygon * 3 + owner.Edge) ?? NativeWideSkirtDistance);
        }
    }

    // A corner's mean direction; where its edges point apart (a thin spike),
    // the edge's own direction.
    static Vector3 NativeWideSkirtDirection(Vector3 sum, Vector3 own) =>
        sum.LengthSquared() < 0.25f ? own : Vector3.Normalize(sum);
}
