import numpy as np, math
def q_of(R):
    t = np.trace(R)
    if t > 0:
        s = math.sqrt(t + 1) * 2; return np.array([0.25 * s, (R[2,1]-R[1,2])/s, (R[0,2]-R[2,0])/s, (R[1,0]-R[0,1])/s])
    i = int(np.argmax(np.diag(R)))
    if i == 0:
        s = math.sqrt(1 + R[0,0] - R[1,1] - R[2,2]) * 2; return np.array([(R[2,1]-R[1,2])/s, 0.25*s, (R[0,1]+R[1,0])/s, (R[0,2]+R[2,0])/s])
    if i == 1:
        s = math.sqrt(1 + R[1,1] - R[0,0] - R[2,2]) * 2; return np.array([(R[0,2]-R[2,0])/s, (R[0,1]+R[1,0])/s, 0.25*s, (R[1,2]+R[2,1])/s])
    s = math.sqrt(1 + R[2,2] - R[0,0] - R[1,1]) * 2; return np.array([(R[1,0]-R[0,1])/s, (R[0,2]+R[2,0])/s, (R[1,2]+R[2,1])/s, 0.25*s])
def R_of(q):
    w, x, y, z = q / np.linalg.norm(q)
    return np.array([[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)], [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)], [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]])
def slerp(R, t):
    q = q_of(R)
    if q[0] < 0: q = -q
    ang = 2 * math.acos(min(1, q[0])); s = math.sin(ang / 2)
    if s < 1e-9: return np.eye(3)
    ax = q[1:] / s; a = ang * t
    return R_of(np.r_[math.cos(a / 2), ax * math.sin(a / 2)])
def split_twist(R, axis):
    q = q_of(R); v = q[1:]; along = axis * (v @ axis)
    tw = np.r_[q[0], along]; n = np.linalg.norm(tw)
    twist = R_of(tw / n) if n > 1e-6 else np.eye(3)
    return R @ twist.T, twist
