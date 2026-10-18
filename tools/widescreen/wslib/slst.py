"""Retail SLST visibility lists: which polygons the 4:3 game draws at each camera-path point.

Every camera path names an SLST entry: item 0 is the full sorted polygon list of point 0,
items 1..n-1 are deltas (removals, additions, then swaps that keep the depth order) and
item n is the full list of the last point again. A list entry is a poly id:
bits 0-11 polygon, 12-14 world (index into the zone's worlds), 15 flag.
Decoder: wurlyfox c1 src/formats/slst.c SlstDecodeForward.

retail_polys(L, zone, path, k) -> {world index: set(polygon)}; render_core() renders the
4:3 core like the game: those polygons, plus the neighbour meshes only where they leave
a void (the runtime's #105 WorldCore fill).
"""
import struct
from functools import lru_cache

import numpy as np

from . import raster
from .nsf import eidstr


def _items(L, path):
    for eid, (t, items) in L.entries.items():
        if eidstr(eid) == path.slst:
            return items
    raise KeyError(path.slst)


def _full(item):
    n, _ = struct.unpack_from("<HH", item, 0)
    return list(struct.unpack_from(f"<{n}H", item, 4))


class _Cursor:
    pass


def _forward(item, src):
    """List of the next path point from the list src and its delta item."""
    length, _ = struct.unpack_from("<HH", item, 0)
    nodes = list(struct.unpack_from(f"<{length}H", item, 4)) if length else []
    split, swaps = nodes[0], nodes[1]

    def nxt(c):
        if c.len > 0:
            c.poly = nodes[c.i]; c.i += 1
            c.idx += c.poly >> 15
        else:
            c.node = nodes[c.i] if c.i < len(nodes) else 0xFFFF; c.i += 1
            if c.i < c.end and c.node != 0xFFFF:
                c.idx = c.node & 0xFFF
                c.len = ((c.node & 0xF000) >> 12) + 1
            else:
                c.idx = 0xFFFF00; c.len = 0
            if c.len > 0:
                c.poly = nodes[c.i]; c.i += 1
    rem, add = _Cursor(), _Cursor()
    rem.i, rem.end = 2, split
    add.i, add.end = split, swaps
    rem.len = add.len = 0
    rem.idx = add.idx = 0
    nxt(rem); nxt(add)
    isrc = 0; dst = []
    rem.count = 0
    while isrc < len(src) or add.len > 0 or rem.len > 0:
        if isrc < len(src) and rem.len > 0 and rem.idx - 1 - isrc == 0:
            isrc += 1; rem.len -= 1; rem.count += 1; nxt(rem)
        elif add.len > 0 and add.idx + rem.count - isrc == 0:
            dst.append(add.poly & 0x7FFF); add.len -= 1; nxt(add)
        elif isrc < len(src):
            cc = min(add.idx + rem.count - isrc, rem.idx - (isrc + 1), len(src) - (isrc + 1))
            if cc == 0:
                cc = 1
            dst.extend(src[isrc:isrc + cc]); isrc += cc
        else:
            break
    i = swaps
    idx = 0
    while i < length:
        node = nodes[i]
        if node == 0xFFFF:
            break
        if node & 0x8000:
            idx, ln, nl = node & 0x7FF, (node >> 11) & 0xF, 1
        elif node & 0xC000 == 0x4000:
            idx = idx + ((node >> 5) & 0x1FF); ln = (node & 0x1F) + 16; nl = 1
        else:
            idx, ln, nl = node & 0xFFF, nodes[i + 1] & 0xFFF, 2
        j = idx + ln + 1
        if j < len(dst):
            dst[idx], dst[j] = dst[j], dst[idx]
        i += nl
    return dst


@lru_cache(None)
def lists(L, zone, pidx):
    """Decoded poly id list of every point of path pidx of zone (full zone name)."""
    path = L.zones[zone].paths[pidx]
    items = _items(L, path)
    cur = _full(items[0])
    out = [cur]
    for k in range(1, len(path)):
        cur = _forward(items[k], cur)
        out.append(cur)
    return out


def check(L, zone, pidx):
    """(ok, decoded last list length, stored last list length): the forward-decoded list of the
    last point must equal the full list stored after the deltas."""
    path = L.zones[zone].paths[pidx]
    items = _items(L, path)
    last = lists(L, zone, pidx)[-1]
    if len(items) <= len(path):
        return None, len(last), 0
    stored = _full(items[len(path)])
    return last == stored, len(last), len(stored)


def retail_polys(L, zone, pidx, k):
    res = {}
    for v in lists(L, zone, pidx)[k]:
        res.setdefault((v >> 12) & 7, set()).add(v & 0xFFF)
    return res


def nearest_point(L, zone, cam):
    """(path, k) of the zone's camera-path point closest to the camera position: the SLST list
    the game uses there (it follows Crash's progress, which this approximates)."""
    z = L.zone(zone)
    best = None
    for p in z.paths:
        d = np.linalg.norm(np.array([q[:3] for q in p.points], float) - cam.pos, axis=1)
        k = int(d.argmin())
        if best is None or d[k] < best[0]:
            best = (d[k], p.index, k)
    return best[1], best[2]


def render_core(L, cam, zone, pidx, k, xrange=None):
    """Frame of the 4:3 core as the game draws it at path point k: the retail SLST polygons
    of the zone's own worlds, and the neighbour meshes where those leave a void. Layers are
    L.zone_meshes(zone), polygon ids the original ones, so it composes with raster.render."""
    z = L.zone(zone)
    ms = L.zone_meshes(z)
    nown = len(z.worlds)
    rp = retail_polys(L, z.name, pidx, k)
    keep = [m.subset(np.isin(np.arange(m.polys), sorted(rp.get(wi, ())))) for wi, m in enumerate(ms[:nown])]
    fr = raster.render(cam, keep, xrange=xrange)
    for li, m in enumerate(keep):
        sel = fr.layer == li
        fr.poly[sel] = m.orig[fr.poly[sel]]
    if len(ms) > nown:
        nb = raster.render(cam, ms[nown:], xrange=xrange)
        fill = fr.void & ~nb.void
        fr.layer[fill] = nb.layer[fill] + nown
        fr.poly[fill] = nb.poly[fill]
        fr.depth[fill] = nb.depth[fill]
    fr.layers = ms
    return fr


def compose(fr, core, cols=None):
    """Copy core's pixels into fr over columns cols (default: the whole 4:3 core)."""
    cols = cols if cols is not None else slice(fr.cx - raster.CORE, fr.cx + raster.CORE)
    fr.depth[:, cols] = core.depth[:, cols]
    fr.layer[:, cols] = core.layer[:, cols]
    fr.poly[:, cols] = core.poly[:, cols]
    return fr
