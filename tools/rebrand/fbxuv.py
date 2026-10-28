# uvmap.py without Blender: reads a binary FBX's mesh straight from the file (numpy only) and writes, for each texel
# of its colour map, the rest-pose point it paints and that face's normal (NaN where no triangle).
# The points are in the FBX file's own axes and units, scaled to metres (Unity's FBX export here: Y up, the model
# faces +Z or -Z; rebrand.py only needs "up" and "sideways", which it finds from the mesh itself).
# usage: python3 fbxuv.py <model.fbx> <out.npz> [size, default 2048]
import sys, struct, zlib, numpy as np


def read_fbx(path):
    data = open(path, 'rb').read()
    if not data.startswith(b'Kaydara FBX Binary'): raise SystemExit(path + ': not a binary FBX')
    ver = struct.unpack_from('<I', data, 23)[0]
    wide = ver >= 7500
    hdr = '<QQQB' if wide else '<IIIB'
    hsz = struct.calcsize(hdr)

    def prop(o):
        t = chr(data[o]); o += 1
        simple = {'Y': '<h', 'C': '<?', 'I': '<i', 'F': '<f', 'D': '<d', 'L': '<q'}
        if t in simple:
            v = struct.unpack_from(simple[t], data, o)[0]; return v, o + struct.calcsize(simple[t])
        if t in 'fdlib':
            n, enc, clen = struct.unpack_from('<III', data, o); o += 12
            raw = data[o:o + clen]; o += clen
            if enc == 1: raw = zlib.decompress(raw)
            return np.frombuffer(raw, {'f': '<f4', 'd': '<f8', 'l': '<i8', 'i': '<i4', 'b': 'u1'}[t], n), o
        if t in 'SR':
            n = struct.unpack_from('<I', data, o)[0]; o += 4
            return data[o:o + n], o + n
        raise SystemExit('unknown FBX property type ' + t)

    def node(o):
        end, nprops, plen, nlen = struct.unpack_from(hdr, data, o)
        if end == 0: return None, o + hsz + nlen
        name = data[o + hsz:o + hsz + nlen].decode(); p = o + hsz + nlen
        props = []
        for _ in range(nprops):
            v, p = prop(p); props.append(v)
        p0 = p
        kids = []
        while p < end:
            k, p = node(p)
            if k is None: break
            kids.append(k)
        return (name, props, kids), end

    top, o = [], 27
    while o < len(data) - hsz:
        n, o = node(o)
        if n is None: break
        top.append(n)
    return top


def find(nodes, name): return [n for n in nodes if n[0] == name]
def child(n, name):
    for k in n[2]:
        if k[0] == name: return k
    return None


def mesh(path):
    top = read_fbx(path)
    objs = find(top, 'Objects')[0][2]
    geos = [n for n in objs if n[0] == 'Geometry' and n[1][2] == b'Mesh']
    geo = max(geos, key=lambda g: len(child(g, 'Vertices')[1][0]))
    v = child(geo, 'Vertices')[1][0].reshape(-1, 3)
    pvi = child(geo, 'PolygonVertexIndex')[1][0]
    uvl = child(geo, 'LayerElementUV')
    uv = child(uvl, 'UV')[1][0].reshape(-1, 2)
    ref = child(uvl, 'ReferenceInformationType')[1][0]
    mapping = child(uvl, 'MappingInformationType')[1][0]
    uvi = child(uvl, 'UVIndex')[1][0] if ref == b'IndexToDirect' else None
    if mapping != b'ByPolygonVertex': raise SystemExit('UV mapping ' + mapping.decode() + ' not handled')
    # the file's unit (cm in a Unity-bound FBX) to metres
    scale = 0.01
    gs = find(top, 'GlobalSettings')
    if gs:
        for p in child(gs[0], 'Properties70')[2]:
            if p[1][0] == b'UnitScaleFactor': scale = float(p[1][4]) * 0.01
    # polygons -> triangles (fan), per polygon-vertex
    verts = np.where(pvi < 0, ~pvi, pvi)
    ends = np.nonzero(pvi < 0)[0]
    tris = []
    start = 0
    for e in ends:
        for k in range(start + 1, e):
            tris.append((start, k, k + 1))
        start = e + 1
    tris = np.array(tris, np.int64)
    uvs = uv[uvi] if uvi is not None else uv
    return v * scale, verts, uvs, tris


def main():
    fbx, out = sys.argv[1], sys.argv[2]
    N = int(sys.argv[3]) if len(sys.argv) > 3 else 2048
    co, verts, uvs, tris = mesh(fbx)
    pos = np.full((N, N, 3), np.nan, np.float32); nrm = np.zeros((N, N, 3), np.float32)
    for tri in tris:
        P = co[verts[tri]]; n = np.cross(P[1] - P[0], P[2] - P[0]); nn = np.linalg.norm(n)
        if nn == 0: continue
        n /= nn; uv = uvs[tri]
        x = uv[:, 0] * N; y = (1 - uv[:, 1]) * N
        x0, x1 = max(int(np.floor(x.min())) - 1, 0), min(int(np.ceil(x.max())) + 1, N - 1)
        y0, y1 = max(int(np.floor(y.min())) - 1, 0), min(int(np.ceil(y.max())) + 1, N - 1)
        if x1 < x0 or y1 < y0: continue
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        d = (y[1] - y[2]) * (x[0] - x[2]) + (x[2] - x[1]) * (y[0] - y[2])
        if abs(d) < 1e-12: continue
        w0 = ((y[1] - y[2]) * (gx - x[2]) + (x[2] - x[1]) * (gy - y[2])) / d
        w1 = ((y[2] - y[0]) * (gx - x[2]) + (x[0] - x[2]) * (gy - y[2])) / d
        w2 = 1 - w0 - w1; e = -1.0 / max(abs(d) ** 0.5, 1)
        m = (w0 >= e) & (w1 >= e) & (w2 >= e)
        if not m.any(): continue
        p = w0[..., None] * P[0] + w1[..., None] * P[1] + w2[..., None] * P[2]
        pos[y0:y1 + 1, x0:x1 + 1][m] = p[m]; nrm[y0:y1 + 1, x0:x1 + 1][m] = n
    np.savez_compressed(out, pos=pos, nrm=nrm)
    ok = ~np.isnan(pos[..., 0])
    print('covered', round(float(ok.mean()), 3), 'bounds', co.min(0).round(3), co.max(0).round(3))


if __name__ == '__main__':
    main()
