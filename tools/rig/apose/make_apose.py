# python3 make_apose.py <in.fbx> <phi> <out.npz>
import sys, numpy as np
from skel import Rig
from arap import apose
rig = Rig(sys.argv[1]); OLD = {n: rig.REST[n].copy() for n in rig.names}
V, REST, free = apose(rig, float(sys.argv[2]), iters=40)
np.savez(sys.argv[3], V=V, **{'M_' + k: v for k, v in REST.items()}, **{'O_' + k: v for k, v in OLD.items()})
