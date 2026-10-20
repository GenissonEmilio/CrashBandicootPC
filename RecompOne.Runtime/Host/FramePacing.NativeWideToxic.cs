using System.Numerics;
using RecompOne.Runtime.Memory;

namespace RecompOne.Runtime.Host;

public static partial class FramePacing
{
    // Toxic Waste (level 7) is a straight corridor of tank rooms joined by
    // hazard-striped doorways (jambs at X 7200 and 8800, 400 deep). Past each
    // doorway the room widens to its tank walls (X 6800 and 9200) through
    // slanted chamfers, one each side of both jamb faces. The 4:3 cull kept
    // the chamfers on the near (+Z) face, which the camera looks at, but cut
    // those on the far face: just past a doorway, the 16:9 bands saw past the
    // jambs into nothing (#151 follow-up). Each far face gets the near-face
    // chamfers of a doorway reflected in Z about the plane halfway between the
    // two faces (its own doorway's, or one a floor step higher or lower). The
    // two faces are not exact mirror images, so the copies go behind the
    // scenery: they show only where nothing else is drawn.
    //
    // A doorway: Z of the source doorway's near face, its floor Y (bottom of
    // its jambs), Z of the far face to fill and the floor step between them.
    readonly record struct NativeWideToxicDoor(float SourceFace, float SourceFloor, float Face, float Rise);

    static NativeWideToxicDoor[]? NativeWideToxicDoors(IMemory m, NativeWideWorld world) =>
        m.ReadU32(Catalogs.Catalog.LevelIdAddr) != 7 ? null
        : (world.PolyCount, world.VertexCount, (int)m.ReadU32(world.Header), (int)m.ReadU32(world.Header + 8)) switch
        {
            // The level's first doorway, at the start (the same floor).
            (711, 703, 8200, 123690) => [new(119994, 3595, 125994, 0)],
            (1600, 1400, 8200, 114084) => [new(114796, 5596, 114396, 0), new(111596, 5596, 111196, 0)],
            (1763, 1572, 8200, 102474) => [new(103194, 7599, 102794, 0), new(99994, 7599, 99594, 0)],
            (1785, 1639, 8200, 90643) => [new(91595, 9599, 91195, 0), new(85195, 9599, 84795, 0)],
            (1711, 1536, 8200, 78874) =>
                [new(79994, 11598, 79594, 0), new(76794, 11598, 76394, 0), new(73594, 11598, 73194, 0)],
            // Also mesh 5__7W's two doorways, one floor lower; it has no complete
            // near face of its own and is always drawn together with this mesh.
            (1703, 1561, 8200, 56697) =>
                [new(56793, 15600, 68000, -2000), new(56793, 15600, 64800, -2000), new(56793, 15600, 56393, 0)],
            (1641, 1481, 8200, 45146) => [new(45194, 17596, 49994, -2000), new(45194, 17596, 44794, 0)],
            (1725, 1526, 8200, 33619) =>
                [new(33595, 19594, 38395, -2000), new(33595, 19594, 33195, 0), new(30395, 19594, 29995, 0)],
            (1885, 1713, 8200, 21723) => [new(21995, 21593, 26795, -2000), new(21995, 21593, 21595, 0)],
            (1551, 1403, 8200, 10532) => [new(10396, 23600, 9996, 0)],
            (1869, 1682, 8200, -1222) =>
                [new(3994, 23598, 3594, 0), new(-1206, 25598, -1606, 0), new(-4406, 25598, -4806, 0)],
            // The end room's doorway (Z -19600) opens into the room itself.
            (2418, 2151, 8200, -15400) => [new(-12800, 27599, -8000, -2000), new(-12800, 27599, -13200, 0)],
            _ => null,
        };

    // Both chamfers of the source face, from the jamb to the first column of
    // the tank wall, floor to ceiling, without the semi-transparent acid. The
    // far face's jamb edge and tank wall lie a little further out than the
    // reflection: the copy is pushed 24 into the jamb (hidden by its face),
    // and a second copy, outward and back by 100, closes the seam left at the
    // tank wall behind the first.
    static IEnumerable<NativeWideCopy> NativeWideToxicDoorCopies(NativeWideToxicDoor[] doors)
    {
        foreach (var door in doors)
        {
            float mirror = (door.SourceFace + door.Face) / 2;
            float y0 = door.SourceFloor - 10, y1 = door.SourceFloor + 2700;
            float z0 = door.SourceFace - 40, z1 = door.SourceFace + 900;
            yield return new(new(6750, y0, z0), new(7201, y1, z1), new(0, door.Rise, 24), Opaque: true, MirrorZ: mirror);
            yield return new(new(8799, y0, z0), new(9260, y1, z1), new(0, door.Rise, 24), Opaque: true, MirrorZ: mirror);
            yield return new(new(6750, y0, z0), new(7201, y1, z1), new(-100, door.Rise, -100), Opaque: true, MirrorZ: mirror, Rank: 1);
            yield return new(new(8799, y0, z0), new(9260, y1, z1), new(100, door.Rise, -100), Opaque: true, MirrorZ: mirror, Rank: 1);
        }
    }
}
