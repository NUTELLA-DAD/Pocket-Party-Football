# minimal binary FBX reader: nodes -> (name, props, children)
import struct, zlib, sys
class Yi(int): pass
class Li(int): pass
class Ff(float): pass
class Raw(bytes): pass
def read(path):
    d = open(path, 'rb').read()
    assert d[:20] == b'Kaydara FBX Binary  ', 'not binary fbx'
    ver = struct.unpack_from('<I', d, 23)[0]
    big = ver >= 7500
    def prop(o):
        t = chr(d[o]); o += 1
        if t in 'YCILFD':
            f = {'Y': '<h', 'C': '<?', 'I': '<i', 'L': '<q', 'F': '<f', 'D': '<d'}[t]
            v = struct.unpack_from(f, d, o)[0]
            v = {'Y': Yi, 'L': Li, 'F': Ff}.get(t, lambda x: x)(v)
            return v, o + struct.calcsize(f)
        if t in 'SR':
            n = struct.unpack_from('<I', d, o)[0]; o += 4
            v = d[o:o + n]; return (Raw(v) if t == 'R' else v.decode('utf8', 'surrogateescape')), o + n
        if t in 'fdlib':
            n, enc, cl = struct.unpack_from('<III', d, o); o += 12
            raw = d[o:o + cl]; o += cl
            if enc: raw = zlib.decompress(raw)
            import numpy as np
            dt = {'f': np.float32, 'd': np.float64, 'l': np.int64, 'i': np.int32, 'b': np.uint8}[t]
            return np.frombuffer(raw, dtype=dt, count=n), o
        raise ValueError(t)
    def node(o):
        if big: end, np_, pl = struct.unpack_from('<QQQ', d, o); o += 24
        else: end, np_, pl = struct.unpack_from('<III', d, o); o += 12
        nl = d[o]; o += 1
        if end == 0: return None, o
        name = d[o:o + nl].decode(); o += nl
        props = []
        for _ in range(np_):
            v, o = prop(o); props.append(v)
        kids = []
        while o < end:
            c, o = node(o)
            if c is None: break
            kids.append(c)
        return (name, props, kids), end
    o = 27; top = []
    while o < len(d):
        n, o = node(o)
        if n is None: break
        top.append(n)
    return top
def find(n, name): return [c for c in n[2] if c[0] == name]
def one(n, name):
    r = find(n, name); return r[0] if r else None
