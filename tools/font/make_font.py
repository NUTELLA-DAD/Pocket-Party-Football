# Pocket Block: the game's own display font (TV HUD, banners, menus).
#
# Chunky toon block letters with cut corners, the same cut as the gold PLAY button, a small forward lean, and
# lowercase drawn as small capitals. Every glyph is a few straight-edged outlines written below, so the font is ours
# (no outside font went into it). Plain Python 3, no packages:
#
#   python tools/font/make_font.py            -> Assets/_Project/Resources/Fonts/PocketBlock.ttf
#
# Change a letter, run it again, commit the .ttf. tools/font/preview.py draws a sheet of every glyph (needs Pillow).
import math
import os
import struct
import sys

UPM = 1000
CAP = 700            # capital height
STEM = 172           # vertical stroke
BAR = 146            # horizontal stroke
SLANT_DEG = 6        # forward lean
SMALL = 0.76         # small capitals (the lowercase keys): height
SMALL_W = 0.86       # ... and width
WIDE = 1.10          # every letter's width, against the numbers written below
SIDE = 40            # side bearing, each side
ASC, DESC = 790, 120 # line box: caps sit in its middle

# corner cuts, as fractions of the stroke
CUT = {'c': 0.30, 'R': 1.0, 'r': 0.55, 'k': 0.14, '0': 0.0}


class U:
    def __init__(self, small):
        k = SMALL if small else 1.0
        self.h = CAP * k
        self.s = STEM * (0.9 if small else 1.0)
        self.t = BAR * (0.9 if small else 1.0)
        self.ws = SMALL_W if small else 1.0

    def w(self, k):
        return k * CAP * self.ws * WIDE


def chamfer(pts, s):
    """pts: (x, y, kind). Every corner is cut by its kind (a fraction of the stroke s); concave corners fill in."""
    n = len(pts)
    out = []
    for i in range(n):
        x, y, kind = pts[i]
        amt = (CUT[kind] if isinstance(kind, str) else kind) * s
        if amt <= 0:
            out.append((x, y))
            continue
        ax, ay = pts[i - 1][0], pts[i - 1][1]
        bx, by = pts[(i + 1) % n][0], pts[(i + 1) % n][1]
        la, lb = math.hypot(ax - x, ay - y), math.hypot(bx - x, by - y)
        d = min(amt, la * 0.45, lb * 0.45)
        out.append((x + (ax - x) / la * d, y + (ay - y) / la * d))
        out.append((x + (bx - x) / lb * d, y + (by - y) / lb * d))
    return out


def box(x0, y0, x1, y1, k='c'):
    ks = k if isinstance(k, (list, tuple)) else [k] * 4
    return [(x0, y0, ks[0]), (x1, y0, ks[1]), (x1, y1, ks[2]), (x0, y1, ks[3])]


def mirror(contours, w):
    return [[(w - x, y, k) for (x, y, k) in reversed(c)] for c in contours]


def turn(contours, w, h):
    return [[(w - x, h - y, k) for (x, y, k) in c] for c in contours]


# ---- the letters. Each returns (width, contours); the first contour is the outside, holes follow (any direction:
# the writer fixes it). y is up, the baseline is 0.

def g_A(u):
    h, s, t, w = u.h, u.s, u.t, u.w(0.70)
    cb = 0.34 * h
    return w, [[(0, 0, 'c'), (s, 0, 'c'), (s, cb, 0), (w - s, cb, 0), (w - s, 0, 'c'), (w, 0, 'c'), (w, h, 1.55), (0, h, 1.55)],
               box(s, cb + t, w - s, h - t, ['0', '0', 0.9, 0.9])]


def g_B(u):
    h, s, t, w = u.h, u.s, u.t, u.w(0.68)
    m, n = 0.5 * h, w - 0.07 * u.w(1)
    return w, [[(0, 0, 'c'), (w, 0, 'R'), (w, m, 'r'), (n, m, 0), (n, h, 'R'), (0, h, 'c')],
               box(s, t, w - s, m - t / 2, ['0', 0.25, 0.25, '0']),
               box(s, m + t / 2, n - s, h - t, ['0', 0.25, 0.25, '0'])]


def g_C(u):
    h, s, t, w = u.h, u.s, u.t, u.w(0.66)
    lo, hi = 0.34 * h, 0.66 * h
    return w, [[(0, 0, 'R'), (w, 0, 'c'), (w, lo, 'c'), (w - s, lo, 0), (w - s, t, 0), (s, t, 'r'), (s, h - t, 'r'),
                (w - s, h - t, 0), (w - s, hi, 0), (w, hi, 'c'), (w, h, 'c'), (0, h, 'R')]]


def g_D(u):
    h, s, t, w = u.h, u.s, u.t, u.w(0.68)
    return w, [box(0, 0, w, h, ['c', 'R', 'R', 'c']), box(s, t, w - s, h - t, ['0', 'r', 'r', '0'])]


def g_E(u):
    h, s, t, w = u.h, u.s, u.t, u.w(0.60)
    m, mw = 0.5 * h, 0.86 * w
    return w, [[(0, 0, 'c'), (w, 0, 'c'), (w, t, 'c'), (s, t, 0), (s, m - t / 2, 0), (mw, m - t / 2, 'c'), (mw, m + t / 2, 'c'),
                (s, m + t / 2, 0), (s, h - t, 0), (w, h - t, 'c'), (w, h, 'c'), (0, h, 'c')]]


def g_F(u):
    h, s, t, w = u.h, u.s, u.t, u.w(0.58)
    m, mw = 0.47 * h, 0.86 * w
    return w, [[(0, 0, 'c'), (s, 0, 'c'), (s, m - t / 2, 0), (mw, m - t / 2, 'c'), (mw, m + t / 2, 'c'),
                (s, m + t / 2, 0), (s, h - t, 0), (w, h - t, 'c'), (w, h, 'c'), (0, h, 'c')]]


def g_G(u):
    h, s, t, w = u.h, u.s, u.t, u.w(0.68)
    g, hi = 0.52 * h, 0.68 * h
    return w, [[(0, 0, 'R'), (w, 0, 'R'), (w, g, 'c'), (0.46 * w, g, 'c'), (0.46 * w, g - t, 'c'), (w - s, g - t, 0),
                (w - s, t, 'r'), (s, t, 'r'), (s, h - t, 'r'), (w - s, h - t, 0), (w - s, hi, 0), (w, hi, 'c'), (w, h, 'c'), (0, h, 'R')]]


def g_H(u):
    h, s, t, w = u.h, u.s, u.t, u.w(0.70)
    m = 0.5 * h
    return w, [[(0, 0, 'c'), (s, 0, 'c'), (s, m - t / 2, 0), (w - s, m - t / 2, 0), (w - s, 0, 'c'), (w, 0, 'c'), (w, h, 'c'),
                (w - s, h, 'c'), (w - s, m + t / 2, 0), (s, m + t / 2, 0), (s, h, 'c'), (0, h, 'c')]]


def g_I(u):
    return u.s, [box(0, 0, u.s, u.h)]


def g_J(u):
    h, s, t, w = u.h, u.s, u.t, u.w(0.58)
    lip = 0.36 * h
    return w, [[(0, 0, 'R'), (w, 0, 'R'), (w, h, 'c'), (w - s, h, 'c'), (w - s, t, 'r'), (s, t, 'r'), (s, lip, 'c'), (0, lip, 'c')]]


def g_K(u):
    h, s, t, w = u.h, u.s, u.t, u.w(0.68)
    m, a, j = 0.5 * h, 1.25 * s, s + 0.55 * s
    return w, [[(0, 0, 'c'), (s, 0, 'c'), (s, m - 0.2 * h, 0), (w - a, 0, 'c'), (w, 0, 'c'), (j, m, 0), (w, h, 'c'), (w - a, h, 'c'),
                (s, m + 0.2 * h, 0), (s, h, 'c'), (0, h, 'c')]]


def g_L(u):
    h, s, t, w = u.h, u.s, u.t, u.w(0.56)
    return w, [[(0, 0, 'c'), (w, 0, 'c'), (w, t, 'c'), (s, t, 0), (s, h, 'c'), (0, h, 'c')]]


def g_M(u):
    h, s, t, w = u.h, u.s, u.t, u.w(0.86)
    v = 0.42 * h
    return w, [[(0, 0, 'c'), (s, 0, 'c'), (s, h - v - 0.04 * h, 0), (w / 2, h - v - 0.36 * h, 0), (w - s, h - v - 0.04 * h, 0), (w - s, 0, 'c'),
                (w, 0, 'c'), (w, h, 'c'), (w - 1.1 * s, h, 0), (w / 2, h - v, 0), (1.1 * s, h, 0), (0, h, 'c')]]


def g_N(u):
    h, s, t, w = u.h, u.s, u.t, u.w(0.72)
    return w, [[(0, 0, 'c'), (s, 0, 'c'), (s, 0.5 * h, 0), (w - 1.25 * s, 0, 0), (w, 0, 'c'), (w, h, 'c'), (w - s, h, 'c'),
                (w - s, 0.5 * h, 0), (1.25 * s, h, 0), (0, h, 'c')]]


def g_O(u):
    h, s, t, w = u.h, u.s, u.t, u.w(0.70)
    return w, [box(0, 0, w, h, 'R'), box(s, t, w - s, h - t, 'r')]


def g_P(u):
    h, s, t, w = u.h, u.s, u.t, u.w(0.66)
    pb = 0.40 * h
    return w, [[(0, 0, 'c'), (s, 0, 'c'), (s, pb, 0), (w, pb, 'R'), (w, h, 'R'), (0, h, 'c')],
               box(s, pb + t, w - s, h - t, ['0', 0.25, 0.25, '0'])]


def g_Q(u):
    h, s, t, w = u.h, u.s, u.t, u.w(0.70)
    o = g_O(u)[1]
    tail = [(0.56 * w, 0.24 * h, 'k'), (0.56 * w + 0.85 * s, 0.24 * h, 'k'), (w + 0.06 * w, -0.11 * h, 'c'), (w + 0.06 * w - 0.85 * s, -0.11 * h, 'c')]
    return w, o + [tail]


def g_R(u):
    h, s, t, w = u.h, u.s, u.t, u.w(0.68)
    pb = 0.40 * h
    a = 1.22 * s
    xl = 0.30 * w
    return w, [[(0, 0, 'c'), (s, 0, 'c'), (s, pb, 0), (xl, pb, 0), (w - a, 0, 'c'), (w, 0, 'c'), (xl + a + 0.04 * w, pb, 0), (w, pb + 0.02 * h, 'r'),
                (w, h, 'R'), (0, h, 'c')],
               box(s, pb + t, w - s, h - t, ['0', 0.25, 0.25, '0'])]


def g_S(u, square_left=False):
    h, s, t, w = u.h, u.s, u.t, u.w(0.66)
    m = 0.5 * h
    L = 'c' if square_left else 'R'
    return w, [[(0, 0, 'c'), (w, 0, 'R'), (w, m + t / 2, 'R'), (s, m + t / 2, 'r'), (s, h - t, 0), (w, h - t, 'c'), (w, h, 'c'),
                (0, h, L), (0, m - t / 2, L), (w - s, m - t / 2, 'r'), (w - s, t, 0), (0, t, 'c')]]


def g_T(u):
    h, s, t, w = u.h, u.s, u.t, u.w(0.64)
    c = w / 2
    return w, [[(c - s / 2, 0, 'c'), (c + s / 2, 0, 'c'), (c + s / 2, h - t, 0), (w, h - t, 'c'), (w, h, 'c'), (0, h, 'c'), (0, h - t, 'c'), (c - s / 2, h - t, 0)]]


def g_U(u):
    h, s, t, w = u.h, u.s, u.t, u.w(0.70)
    return w, [[(0, 0, 'R'), (w, 0, 'R'), (w, h, 'c'), (w - s, h, 'c'), (w - s, t, 'r'), (s, t, 'r'), (s, h, 'c'), (0, h, 'c')]]


def g_V(u):
    h, s, t, w = u.h, u.s, u.t, u.w(0.72)
    a, b = 1.2 * s, 0.62 * s
    # the inside point, where the two inner edges meet (each a stroke right of its outer edge)
    yi = h * (1 - (w / 2 - a) / (w / 2 - b))
    return w, [[(0, h, 'c'), (a, h, 'c'), (w / 2, yi, 0), (w - a, h, 'c'), (w, h, 'c'), (w / 2 + b, 0, 'c'), (w / 2 - b, 0, 'c')]]


def g_W(u):
    h, s, t, w = u.h, u.s, u.t, u.w(0.92)
    v = 0.42 * h
    return w, [[(0, 0, 'c'), (1.1 * s, 0, 0), (w / 2, v, 0), (w - 1.1 * s, 0, 0), (w, 0, 'c'), (w, h, 'c'), (w - s, h, 'c'),
                (w - s, v + 0.04 * h, 0), (w / 2, v + 0.36 * h, 0), (s, v + 0.04 * h, 0), (s, h, 'c'), (0, h, 'c')]]


def g_X(u):
    h, s, t, w = u.h, u.s, u.t, u.w(0.72)
    a, j, k = 1.25 * s, 0.30 * h, 0.17 * w
    return w, [[(0, 0, 'c'), (a, 0, 'c'), (w / 2, j, 0), (w - a, 0, 'c'), (w, 0, 'c'), (w / 2 + k, h / 2, 0), (w, h, 'c'), (w - a, h, 'c'),
                (w / 2, h - j, 0), (a, h, 'c'), (0, h, 'c'), (w / 2 - k, h / 2, 0)]]


def g_Y(u):
    h, s, t, w = u.h, u.s, u.t, u.w(0.70)
    a, c = 1.22 * s, w / 2
    return w, [[(c - s / 2, 0, 'c'), (c + s / 2, 0, 'c'), (c + s / 2, 0.40 * h, 0), (w, h, 'c'), (w - a, h, 'c'), (c, 0.64 * h, 0),
                (a, h, 'c'), (0, h, 'c'), (c - s / 2, 0.40 * h, 0)]]


def g_Z(u):
    h, s, t, w = u.h, u.s, u.t, u.w(0.64)
    xb = 1.35 * s
    return w, [[(0, 0, 'c'), (w, 0, 'c'), (w, t, 'c'), (xb, t, 0), (w, h - t, 'k'), (w, h, 'c'), (0, h, 'c'), (0, h - t, 'c'),
                (w - xb, h - t, 0), (0, t, 'k')]]


# ---- digits: one width, so the clock and the scores do not jiggle

DIG = 0.62


def d0(u):
    h, s, t, w = u.h, u.s, u.t, u.w(DIG)
    return w, [box(0, 0, w, h, 'R'), box(s, t, w - s, h - t, 'r')]


def d1(u):
    h, s, t, w = u.h, u.s, u.t, u.w(DIG)
    x = w / 2 - s / 2 + 0.06 * w
    return w, [[(x, 0, 'c'), (x + s, 0, 'c'), (x + s, h, 'c'), (x - 0.25 * s, h, 'k'), (x - 0.75 * s, h - 0.24 * h, 'c'), (x, h - 0.24 * h, 0)]]


def d2(u):
    h, s, t, w = u.h, u.s, u.t, u.w(DIG)
    m = 0.5 * h
    return w, mirror([[(0, 0, 'c'), (w, 0, 'c'), (w, m + t / 2, 'R'), (s, m + t / 2, 'r'), (s, h - t, 0), (w, h - t, 'c'), (w, h, 'c'),
                       (0, h, 'R'), (0, m - t / 2, 'R'), (w - s, m - t / 2, 'r'), (w - s, t, 0), (0, t, 'c')]], w)


def d3(u):
    h, s, t, w = u.h, u.s, u.t, u.w(DIG)
    m = 0.5 * h
    return w, [[(0, 0, 'c'), (w, 0, 'R'), (w, h, 'R'), (0, h, 'c'), (0, h - t, 'c'), (w - s, h - t, 'r'), (w - s, m + t / 2, 0),
                (0.28 * w, m + t / 2, 'c'), (0.28 * w, m - t / 2, 'c'), (w - s, m - t / 2, 0), (w - s, t, 'r'), (0, t, 'c')]]


def d4(u):
    h, s, t, w = u.h, u.s, u.t, u.w(DIG)
    xs, cb = w - s - 0.06 * w, 0.30 * h
    return w, [[(xs, 0, 'c'), (xs + s, 0, 'c'), (xs + s, cb, 0), (w, cb, 'c'), (w, cb + t, 'c'), (xs + s, cb + t, 0), (xs + s, h, 'c'),
                (xs, h, 'c'), (xs, cb + t, 0), (s, cb + t, 0), (s, h, 'c'), (0, h, 'c'), (0, cb, 'c'), (xs, cb, 0)]]


def d5(u):
    h, s, t, w = u.h, u.s, u.t, u.w(DIG)
    m = 0.54 * h
    return w, [[(0, 0, 'c'), (w, 0, 'R'), (w, m + t / 2, 'R'), (s, m + t / 2, 0), (s, h - t, 0), (w, h - t, 'c'), (w, h, 'c'),
                (0, h, 'c'), (0, m - t / 2, 'c'), (w - s, m - t / 2, 'r'), (w - s, t, 'r'), (0, t, 'c')]]


def d6(u):
    h, s, t, w = u.h, u.s, u.t, u.w(DIG)
    b = 0.56 * h
    return w, [[(0, 0, 'R'), (w, 0, 'R'), (w, b, 'c'), (s, b, 0), (s, h - t, 'r'), (w, h - t, 'c'), (w, h, 'c'), (0, h, 'R')],
               box(s, t, w - s, b - t, ['r', 'r', 'r', '0'])]


def d7(u):
    h, s, t, w = u.h, u.s, u.t, u.w(DIG)
    x2, a = 0.56 * w, 1.2 * s
    return w, [[(x2 - a, 0, 'c'), (x2, 0, 'c'), (w, h - t, 'k'), (w, h, 'c'), (0, h, 'c'), (0, h - t, 'c'), (w - a, h - t, 0)]]


def d8(u):
    h, s, t, w = u.h, u.s * 0.84, u.t * 0.8, u.w(DIG)
    m, n = 0.52 * h, 0.06 * w
    return w, [[(0, 0, 'R'), (w, 0, 'R'), (w, m, 'k'), (w - n, m, 0), (w - n, h, 'R'), (n, h, 'R'), (n, m, 0), (0, m, 'k')],
               box(s, t, w - s, m - t / 2, 0.3), box(n + s, m + t / 2, w - n - s, h - t, 0.3)]


def d9(u):
    w, c = d6(u)
    return w, turn(c, w, u.h)


# ---- marks

def p_period(u):
    s = u.s
    return s, [box(0, 0, s, s * 0.95, 'c')]


def p_comma(u):
    s = u.s
    return s, [[(0, 0, 'c'), (0.25 * s, -0.75 * s, 'k'), (0.75 * s, -0.75 * s, 'k'), (s, 0, 0), (s, s * 0.95, 'c'), (0, s * 0.95, 'c')]]


def p_colon(u):
    s, h = u.s, u.h
    return s, [box(0, 0, s, s * 0.95, 'c'), box(0, 0.5 * h, s, 0.5 * h + s * 0.95, 'c')]


def p_semicolon(u):
    s, h = u.s, u.h
    return s, p_comma(u)[1] + [box(0, 0.5 * h, s, 0.5 * h + s * 0.95, 'c')]


def p_excl(u):
    s, h = u.s, u.h
    return s, [[(0, 0.30 * h, 'k'), (s, 0.30 * h, 'k'), (s * 1.08, h, 'c'), (-s * 0.08, h, 'c')], box(0, 0, s, s * 0.95, 'c')]


def p_quest(u):
    h, s, t, w = u.h, u.s, u.t, u.w(0.60)
    c = w / 2
    j = 0.44 * h
    return w, [[(c - s / 2, 0.30 * h, 'k'), (c + s / 2, 0.30 * h, 'k'), (c + s / 2, j, 0), (w, j, 'R'), (w, h, 'R'), (0, h, 'R'),
                (0, 0.64 * h, 'c'), (s, 0.64 * h, 'c'), (s, h - t, 'r'), (w - s, h - t, 'r'), (w - s, j + t, 'r'), (c - s / 2, j + t, 0)],
               box(c - s / 2, 0, c + s / 2, s * 0.95, 'c')]


def p_quote(u):
    s, h = u.s, u.h
    return s * 0.8, [[(0, 0.64 * h, 'k'), (s * 0.8, 0.64 * h, 'k'), (s * 0.9, h, 'c'), (-s * 0.1, h, 'c')]]


def p_dquote(u):
    s, h = u.s, u.h
    one = p_quote(u)[1][0]
    return s * 1.9, [one, [(x + s * 1.1, y, k) for (x, y, k) in one]]


def p_hyphen(u):
    h, t = u.h, u.t
    w = 0.40 * CAP * u.ws
    return w, [box(0, 0.5 * h - t / 2, w, 0.5 * h + t / 2, 'c')]


def p_dash(u):
    h, t = u.h, u.t
    w = 0.80 * CAP * u.ws
    return w, [box(0, 0.5 * h - t / 2, w, 0.5 * h + t / 2, 'c')]


def p_plus(u):
    h, s, t = u.h, u.s, u.t
    w = 0.56 * CAP * u.ws
    c, m = w / 2, 0.5 * h
    a = w / 2
    return w, [[(c - s / 2, m - a, 'c'), (c + s / 2, m - a, 'c'), (c + s / 2, m - t / 2, 0), (w, m - t / 2, 'c'), (w, m + t / 2, 'c'),
                (c + s / 2, m + t / 2, 0), (c + s / 2, m + a, 'c'), (c - s / 2, m + a, 'c'), (c - s / 2, m + t / 2, 0), (0, m + t / 2, 'c'),
                (0, m - t / 2, 'c'), (c - s / 2, m - t / 2, 0)]]


def p_equal(u):
    h, t = u.h, u.t
    w = 0.50 * CAP * u.ws
    m = 0.5 * h
    return w, [box(0, m - t * 1.25, w, m - t * 0.25, 'c'), box(0, m + t * 0.25, w, m + t * 1.25, 'c')]


def p_slash(u):
    h, s = u.h, u.s
    w = 0.46 * CAP * u.ws
    return w, [[(0, -0.04 * h, 'k'), (1.1 * s, -0.04 * h, 'k'), (w, h, 'k'), (w - 1.1 * s, h, 'k')]]


def p_bar(u):
    h, s = u.h, u.s
    return s * 0.7, [box(0, -0.1 * h, s * 0.7, h * 1.04, 'k')]


def p_paren(u, right=False):
    h, s = u.h, u.s
    w = 1.6 * s
    c = [[(0, -0.06 * h, 'R'), (w, -0.06 * h, 'c'), (w, 0.08 * h, 'c'), (s, 0.08 * h, 'r'), (s, h * 0.98, 'r'), (w, h * 0.98, 'c'),
          (w, h * 1.12, 'c'), (0, h * 1.12, 'R')]]
    return w, mirror(c, w) if right else c


def p_bracket(u, right=False):
    h, s = u.h, u.s
    w = 1.5 * s
    c = [[(0, -0.06 * h, 'c'), (w, -0.06 * h, 'c'), (w, 0.08 * h, 'c'), (s, 0.08 * h, 0), (s, h * 0.98, 0), (w, h * 0.98, 'c'),
          (w, h * 1.12, 'c'), (0, h * 1.12, 'c')]]
    return w, mirror(c, w) if right else c


def p_hash(u):
    h, s, t = u.h, u.s, u.t
    w = 0.72 * CAP * u.ws
    v = 0.82 * s
    a = 0.24 * w
    out = []
    for x in (0.30 * w, 0.66 * w):
        out.append([(x - a * 0.5 - v / 2 + 0.0, 0, 'k'), (x - a * 0.5 + v / 2, 0, 'k'), (x + a * 0.5 + v / 2, h, 'k'), (x + a * 0.5 - v / 2, h, 'k')])
    for y in (0.30 * h, 0.64 * h):
        out.append(box(0, y - t * 0.4, w, y + t * 0.4, 'k'))
    return w, out


def p_percent(u):
    h, s = u.h, u.s
    w = 0.80 * CAP * u.ws
    r = 0.38 * w
    v = 0.4 * s
    return w, [box(0, 0.56 * h, r, h, 'c'), box(v, 0.56 * h + v, r - v, h - v, 'k'),
               box(w - r, 0, w, 0.44 * h, 'c'), box(w - r + v, v, w - v, 0.44 * h - v, 'k'),
               [(0.12 * w, -0.02 * h, 'k'), (0.12 * w + 0.8 * s, -0.02 * h, 'k'), (0.88 * w, h * 1.02, 'k'), (0.88 * w - 0.8 * s, h * 1.02, 'k')]]


def p_amp(u):
    h, s, t = u.h, u.s, u.t
    w = 0.80 * CAP * u.ws
    v, top = 0.64 * s, 0.54 * h
    return w, [box(0.10 * w, top, 0.62 * w, h, 'R'), box(0.10 * w + v, top + v * 0.9, 0.62 * w - v, h - v * 0.9, 0.3),
               [(0, 0, 'R'), (0.70 * w, 0, 'c'), (0.70 * w, v, 0), (v, v, 0.3), (v, 0.48 * h, 'k'), (0, 0.48 * h, 'k')],
               [(w - 0.95 * s, 0, 'c'), (w, 0, 'c'), (0.10 * w + 0.95 * s, top + 0.10 * h, 'k'), (0.10 * w, top + 0.10 * h, 'k')],
               box(0.60 * w, 0.34 * h, w, 0.34 * h + 0.8 * t, 'c')]


def p_underscore(u):
    t = u.t
    w = 0.56 * CAP * u.ws
    return w, [box(0, -0.10 * CAP, w, -0.10 * CAP + t * 0.8, 'k')]


def p_star(u):
    h = u.h
    w = 0.72 * CAP * u.ws
    c, m, R, r = w / 2, 0.5 * h, 0.40 * h, 0.17 * h
    pts = []
    for i in range(10):
        a = math.radians(90 + i * 36)
        rr = R if i % 2 == 0 else r
        pts.append((c + math.cos(a) * rr, m + math.sin(a) * rr, 'k' if i % 2 == 0 else 0))
    return w, [pts]


def p_degree(u):
    h, s = u.h, u.s
    w = 0.40 * CAP * u.ws
    v = 0.6 * s
    return w, [box(0, 0.56 * h, w, h, 'R'), box(v, 0.56 * h + v, w - v, h - v, 'r')]


def p_middot(u):
    s, h = u.s, u.h
    return s, [box(0, 0.5 * h - s * 0.48, s, 0.5 * h + s * 0.48, 'c')]


def p_tri(u, dirn):
    h = u.h
    w = 0.70 * CAP * u.ws
    if dirn == 'left':
        return w, [[(0, 0.5 * h, 'k'), (w, -0.02 * h, 'k'), (w, h * 1.02, 'k')]]
    if dirn == 'right':
        return w, [[(w, 0.5 * h, 'k'), (0, h * 1.02, 'k'), (0, -0.02 * h, 'k')]]
    if dirn == 'up':
        return w, [[(0, 0.02 * h, 'k'), (w, 0.02 * h, 'k'), (w / 2, h, 'k')]]
    return w, [[(w / 2, 0, 'k'), (w, h * 0.98, 'k'), (0, h * 0.98, 'k')]]


def p_dot(u):
    h = u.h
    w = 0.76 * CAP * u.ws
    pts = []
    c, m, r = w / 2, 0.5 * h, 0.44 * min(w, h)
    for i in range(16):
        a = math.radians(i * 22.5 + 11.25)
        pts.append((c + math.cos(a) * r, m + math.sin(a) * r, 0))
    return w, [pts]


def p_check(u):
    h, s = u.h, u.s
    w = 0.84 * CAP * u.ws
    v = 1.25 * s
    return w, [[(0.36 * w, 0, 'k'), (w, h, 'k'), (w - v, h, 'k'), (0.36 * w, 0.30 * h, 0), (0.17 * w, 0.52 * h, 'k'), (0, 0.38 * h, 'k')]]


def p_notdef(u):
    h, s = u.h, u.s
    w = 0.6 * CAP
    return w, [box(0, 0, w, h, 'k'), box(s * 0.6, s * 0.6, w - s * 0.6, h - s * 0.6, 'k')]


def p_space(u):
    return 0.30 * CAP * u.ws - 2 * SIDE, []


CAPS = {chr(ord('A') + i): globals()['g_' + chr(ord('A') + i)] for i in range(26)}
DIGITS = {str(i): globals()['d' + str(i)] for i in range(10)}
MARKS = {
    ' ': p_space, '.': p_period, ',': p_comma, ':': p_colon, ';': p_semicolon, '!': p_excl, '?': p_quest, "'": p_quote, '"': p_dquote,
    '-': p_hyphen, '–': p_hyphen, '—': p_dash, '+': p_plus, '=': p_equal, '/': p_slash, '|': p_bar,
    '(': p_paren, ')': lambda u: p_paren(u, True), '[': p_bracket, ']': lambda u: p_bracket(u, True), '#': p_hash, '%': p_percent,
    '&': p_amp, '_': p_underscore, '*': p_star, '☆': p_star, '★': p_star, '°': p_degree, '·': p_middot,
    '◄': lambda u: p_tri(u, 'left'), '►': lambda u: p_tri(u, 'right'), '▲': lambda u: p_tri(u, 'up'),
    '▼': lambda u: p_tri(u, 'down'), '●': p_dot, '✓': p_check, '✔': p_check,
    ' ': p_space,
}


def build_glyphs():
    """[(char or None, advance, [contour of (x, y)])] in code-point order, .notdef first."""
    big, small = U(False), U(True)
    table = {}
    for ch, f in CAPS.items():
        table[ch] = (f, big)
        table[ch.lower()] = (f, small)
    for ch, f in DIGITS.items():
        table[ch] = (f, big)
    for ch, f in MARKS.items():
        table[ch] = (f, big)
    glyphs = [(None,) + shape(p_notdef, big)]
    for ch in sorted(table):
        f, u = table[ch]
        glyphs.append((ch,) + shape(f, u))
    return glyphs


def shape(f, u):
    w, contours = f(u)
    k = math.tan(math.radians(SLANT_DEG))
    out = []
    for i, c in enumerate(contours):
        pts = chamfer(c, u.s)
        pts = [(x + SIDE + y * k, y) for (x, y) in pts]
        pts = [(int(round(x)), int(round(y))) for (x, y) in pts]
        # drop repeats left by rounding
        clean = []
        for p in pts:
            if not clean or clean[-1] != p:
                clean.append(p)
        if len(clean) > 1 and clean[0] == clean[-1]:
            clean.pop()
        if len(clean) < 3:
            continue
        out.append(clean)
    out = orient(out)
    return int(round(w + 2 * SIDE)), out


def area(c):
    return sum(c[i][0] * c[(i + 1) % len(c)][1] - c[(i + 1) % len(c)][0] * c[i][1] for i in range(len(c))) / 2


def inside(pt, c):
    x, y = pt
    hit = False
    for i in range(len(c)):
        (x0, y0), (x1, y1) = c[i - 1], c[i]
        if (y0 > y) != (y1 > y) and x < (x1 - x0) * (y - y0) / (y1 - y0) + x0:
            hit = not hit
    return hit


def orient(contours):
    """TrueType: the outside runs clockwise, a hole counter-clockwise. A hole is a contour lying wholly inside another
    (a piece that only overlaps another, like the tail of the Q, stays solid)."""
    out = []
    for i, c in enumerate(contours):
        hole = any(j != i and all(inside(p, d) for p in c) for j, d in enumerate(contours))
        is_cw = area(c) < 0
        out.append(c if is_cw != hole else list(reversed(c)))
    return out


# ---- the TrueType writer (only straight lines: every point is on the curve)

def checksum(data):
    data += b'\0' * (-len(data) % 4)
    return sum(struct.unpack('>%dI' % (len(data) // 4), data)) & 0xFFFFFFFF


def glyf_entry(contours):
    if not contours:
        return b'', (0, 0, 0, 0), 0
    xs = [p[0] for c in contours for p in c]
    ys = [p[1] for c in contours for p in c]
    bbox = (min(xs), min(ys), max(xs), max(ys))
    ends, n = [], 0
    for c in contours:
        n += len(c)
        ends.append(n - 1)
    out = struct.pack('>hhhhh', len(contours), *bbox)
    out += struct.pack('>%dH' % len(ends), *ends)
    out += struct.pack('>H', 0)
    out += bytes([0x01]) * n
    px = py = 0
    xb, yb = b'', b''
    for c in contours:
        for x, y in c:
            xb += struct.pack('>h', x - px)
            yb += struct.pack('>h', y - py)
            px, py = x, y
    out += xb + yb
    out += b'\0' * (-len(out) % 4)
    return out, bbox, n


def name_table(names):
    recs, strings = [], b''
    for nid, text in sorted(names.items()):
        raw = text.encode('utf-16-be')
        recs.append(struct.pack('>HHHHHH', 3, 1, 0x409, nid, len(raw), len(strings)))
        strings += raw
    head = struct.pack('>HHH', 0, len(recs), 6 + 12 * len(recs))
    return head + b''.join(recs) + strings


def cmap_table(codes):
    """codes: sorted [(code point, glyph id)], BMP only. Format 4, one segment per run of codes with consecutive ids."""
    segs = []
    for cp, gid in codes:
        if segs and cp == segs[-1][1] + 1 and gid == segs[-1][2] + (cp - segs[-1][0]):
            segs[-1][1] = cp
        else:
            segs.append([cp, cp, gid])
    segs.append([0xFFFF, 0xFFFF, None])
    n = len(segs)
    ends = [s[1] for s in segs]
    starts = [s[0] for s in segs]
    deltas = [((s[2] - s[0]) & 0xFFFF) if s[2] is not None else 1 for s in segs]
    search = 2 * (2 ** int(math.floor(math.log2(n))))
    sub = struct.pack('>HHHHHHH', 4, 0, 0, 2 * n, search, int(math.log2(search // 2)), 2 * n - search)
    sub += struct.pack('>%dH' % n, *ends) + struct.pack('>H', 0) + struct.pack('>%dH' % n, *starts)
    sub += struct.pack('>%dH' % n, *deltas) + struct.pack('>%dH' % n, *([0] * n))
    sub = sub[:2] + struct.pack('>H', len(sub)) + sub[4:]
    return struct.pack('>HHHHI', 0, 1, 3, 1, 12) + sub


def build_font(glyphs):
    glyf, loca, hmtx = b'', [], b''
    xmin = ymin = 10 ** 6
    xmax = ymax = -10 ** 6
    maxpts = maxcont = 0
    adv_max, min_lsb, min_rsb, max_ext = 0, 10 ** 6, 10 ** 6, 0
    for ch, adv, contours in glyphs:
        loca.append(len(glyf))
        data, bbox, n = glyf_entry(contours)
        glyf += data
        lsb = bbox[0] if contours else 0
        hmtx += struct.pack('>Hh', adv, lsb)
        adv_max = max(adv_max, adv)
        if contours:
            xmin, ymin = min(xmin, bbox[0]), min(ymin, bbox[1])
            xmax, ymax = max(xmax, bbox[2]), max(ymax, bbox[3])
            min_lsb = min(min_lsb, bbox[0])
            min_rsb = min(min_rsb, adv - bbox[2])
            max_ext = max(max_ext, bbox[2])
            maxpts = max(maxpts, n)
            maxcont = max(maxcont, len(contours))
    loca.append(len(glyf))
    ng = len(glyphs)

    head = struct.pack('>IIIIHHqqhhhhHHhhh', 0x00010000, 0x00010000, 0, 0x5F0F3CF5, 0x000B, UPM, 0, 0,
                       xmin, ymin, xmax, ymax, 0x0001, 8, 2, 1, 0)
    hhea = struct.pack('>IhhhHhhhhhhhhhhhH', 0x00010000, ASC, -DESC, 0, adv_max, min_lsb, min_rsb, max_ext, 1, 0, 0, 0, 0, 0, 0, 0, ng)
    maxp = struct.pack('>IHHHHHHHHHHHHHH', 0x00010000, ng, maxpts, maxcont, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0)
    codes = [(ord(ch), i) for i, (ch, _, _) in enumerate(glyphs) if ch is not None]
    avg = int(sum(a for _, a, _ in glyphs) / ng)
    first, last = codes[0][0], min(codes[-1][0], 0xFFFF)
    os2 = struct.pack('>HhHHHhhhhhhhhhhh', 4, avg, 900, 5, 0, 650, 600, 0, 75, 650, 600, 0, 350, 60, 260, 0)
    os2 += bytes([2, 11, 9, 2, 2, 2, 2, 2, 2, 4])           # panose: sans, black
    os2 += struct.pack('>IIII', 0x80000003, 0, 0, 0)        # Latin, Latin-1 (and the arrows and shapes fall back)
    os2 += b'PPF '
    os2 += struct.pack('>HHHhhhHHII', 0x0020 | 0x0080, first, last, ASC, -DESC, 0, ASC, DESC, 0x00000001, 0)
    os2 += struct.pack('>hhHHH', int(CAP * SMALL), CAP, 0, 32, 2)
    post = struct.pack('>IihhIIIII', 0x00030000, -int(SLANT_DEG * 65536), -100, 50, 0, 0, 0, 0, 0)
    names = {0: 'Pocket Block. Made for Pocket Party Football; drawn in tools/font/make_font.py.', 1: 'Pocket Block', 2: 'Bold',
             3: 'Pocket Block Bold 1.0', 4: 'Pocket Block Bold', 5: 'Version 1.0', 6: 'PocketBlock-Bold'}
    tables = {
        b'OS/2': os2, b'cmap': cmap_table(codes), b'glyf': glyf, b'head': head, b'hhea': hhea, b'hmtx': hmtx,
        b'loca': struct.pack('>%dI' % len(loca), *loca), b'maxp': maxp, b'name': name_table(names), b'post': post,
    }
    tags = sorted(tables)
    n = len(tags)
    es = int(math.floor(math.log2(n)))
    sr = (2 ** es) * 16
    font = struct.pack('>IHHHH', 0x00010000, n, sr, es, n * 16 - sr)
    offset = 12 + 16 * n
    dirs, body = b'', b''
    for tag in tags:
        data = tables[tag]
        dirs += struct.pack('>4sIII', tag, checksum(data), offset + len(body), len(data))
        body += data + b'\0' * (-len(data) % 4)
    font += dirs + body
    adj = (0xB1B0AFBA - checksum(font)) & 0xFFFFFFFF
    hpos = 12 + 16 * n + sum(len(tables[t]) + (-len(tables[t]) % 4) for t in tags[:tags.index(b'head')])
    font = font[:hpos + 8] + struct.pack('>I', adj) + font[hpos + 12:]
    return font


def main():
    root = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(root, 'Assets', '_Project', 'Resources', 'Fonts', 'PocketBlock.ttf')
    glyphs = build_glyphs()
    data = build_font(glyphs)
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, 'wb') as f:
        f.write(data)
    print('wrote %s: %d glyphs, %d bytes' % (out, len(glyphs), len(data)))


if __name__ == '__main__':
    main()
