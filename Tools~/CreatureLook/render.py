#!/usr/bin/env python3
"""The offline look renderer (creature kit, checked in so agents stop copying scratch renderers).

  render.py <pose.obj> [more.obj ...] --out <sheet.png> [--views side,front34,top,...] [--size 420x320] [--no-cull]

Draws OBJ poses written by the harness's `pose-dump` (creature_kit.py render does both): lines
`v x y z r g b nx ny nz` (heading +z, ground y = 0, colours from the species' look and layer rows) and `f a b c`.
A z-buffered painter with back-face culling (thin sheets: wings, fins, membranes would z-fight without it), a
chequered ground with the shadow-free floor, a sky gradient, one key light + ambient. One row per OBJ, one column per
view. It is a proxy for Unity's shading: shape, silhouette, pose - the user judges the look in the Editor.
Needs numpy and Pillow.
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image, ImageDraw


def load(path):
    V, C, N, F, title = [], [], [], [], os.path.basename(path)
    for line in open(path, encoding='utf-8'):
        if line.startswith('# '):
            title = line[2:].strip()
        elif line.startswith('v '):
            a = line.split()
            V.append([float(a[1]), float(a[2]), float(a[3])])
            C.append([float(a[4]), float(a[5]), float(a[6])])
            N.append([float(a[7]), float(a[8]), float(a[9])])
        elif line.startswith('f '):
            a = line.split()
            F.append([int(a[1]) - 1, int(a[2]) - 1, int(a[3]) - 1])
    return dict(V=np.array(V), C=np.clip(np.array(C), 0, 1), N=np.array(N), F=np.array(F, dtype=np.int64), title=title)


def rot(yaw, pitch):
    cy, sy, cp, sp = np.cos(yaw), np.sin(yaw), np.cos(pitch), np.sin(pitch)
    Ry = np.array([[cy, 0, -sy], [0, 1, 0], [sy, 0, cy]])
    Rx = np.array([[1, 0, 0], [0, cp, -sp], [0, sp, cp]])
    return Rx @ Ry


VIEWS = {'top': rot(np.pi, np.pi / 2), 'side': rot(-np.pi / 2, 0.0), 'back': rot(np.pi, 0.05), 'rear34': rot(np.pi * 0.75, 0.45),
         'front34': rot(np.pi * 0.2, 0.5), 'front': rot(0.0, 0.12), 'lowfront34': rot(np.pi * 0.25, 0.2), 'below': rot(np.pi - 0.3, -0.6),
         'hero': rot(np.pi * 0.3, 0.28)}


def draw(m, R, W=420, H=320, label=None, cull=True, frame=None):
    V, N, F, C = m['V'], m['N'], m['F'], m['C']
    X = V @ R.T
    Nn = N @ R.T
    lo, hi = (X.min(axis=0), X.max(axis=0)) if frame is None else frame
    span = max((hi[0] - lo[0]) / W, (hi[1] - lo[1]) / H) * 1.1 + 1e-9
    cx, cy = (lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2
    px = (X[:, 0] - cx) / span + W / 2
    py = H / 2 - (X[:, 1] - cy) / span
    pz = X[:, 2]
    gy = np.linspace(0, 1, H)[:, None, None]
    img = np.ones((H, W, 3)) * (np.array([0.80, 0.86, 0.93]) * (1 - gy) + np.array([0.95, 0.95, 0.96]) * gy)
    zb = np.full((H, W), -1e18)
    Ri = R.T
    gxx, gyy = np.meshgrid(np.arange(W) + 0.5, np.arange(H) + 0.5)
    sx = (gxx - W / 2) * span + cx
    sy = (H / 2 - gyy) * span + cy
    if Ri[1, 2] > 1e-3:   # the ground plane y = 0, seen from above (screen +z, toward the viewer, points up)
        z = -(Ri[1, 0] * sx + Ri[1, 1] * sy) / Ri[1, 2]
        wx = Ri[0, 0] * sx + Ri[0, 1] * sy + Ri[0, 2] * z
        wz = Ri[2, 0] * sx + Ri[2, 1] * sy + Ri[2, 2] * z
        cell = max(0.05, round(float(hi[0] - lo[0]) / 12, 2))
        chk = (np.floor(wx / cell) + np.floor(wz / cell)) % 2
        g = np.where(chk[..., None] > 0, np.array([0.70, 0.67, 0.61]), np.array([0.77, 0.74, 0.67]))
        # beyond the far edge of a low view the plane is sky; a body below it (a burrower) stays hidden by it
        far = z < pz.min() - 4.0 * (hi[2] - lo[2] + 1e-6)
        img = np.where(far[..., None], img, g)
        zb = np.where(far, zb, z)
    L = np.array([0.35, 0.8, 0.5])
    L /= np.linalg.norm(L)
    Lv = L @ R.T
    nrm = Nn / np.maximum(np.linalg.norm(Nn, axis=1, keepdims=True), 1e-9)
    lam = np.clip(nrm @ Lv, 0, 1)
    shade = (0.38 + 0.7 * lam)[:, None] * C
    for f in F:
        x = px[f]
        y = py[f]
        d = (x[1] - x[0]) * (y[2] - y[0]) - (x[2] - x[0]) * (y[1] - y[0])
        if abs(d) < 1e-12:
            continue
        if cull and d > 0:   # facing away (screen y grows down)
            continue
        x0 = max(int(np.floor(x.min())), 0)
        x1 = min(int(np.ceil(x.max())), W - 1)
        y0 = max(int(np.floor(y.min())), 0)
        y1 = min(int(np.ceil(y.max())), H - 1)
        if x0 > x1 or y0 > y1:
            continue
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        w1 = ((xs - x[0]) * (y[2] - y[0]) - (x[2] - x[0]) * (ys - y[0])) / d
        w2 = ((x[1] - x[0]) * (ys - y[0]) - (xs - x[0]) * (y[1] - y[0])) / d
        w0 = 1 - w1 - w2
        ins = (w0 >= -1e-6) & (w1 >= -1e-6) & (w2 >= -1e-6)
        if not ins.any():
            continue
        zz = w0 * pz[f[0]] + w1 * pz[f[1]] + w2 * pz[f[2]]
        sub = zb[y0:y1 + 1, x0:x1 + 1]
        ok = ins & (zz > sub)
        if not ok.any():
            continue
        col = w0[..., None] * shade[f[0]] + w1[..., None] * shade[f[1]] + w2[..., None] * shade[f[2]]
        sub[ok] = zz[ok]
        im = img[y0:y1 + 1, x0:x1 + 1]
        im[ok] = col[ok]
    out = Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8))
    if label:
        ImageDraw.Draw(out).text((6, 4), label, fill=(0, 0, 0))
    return out


def sheet(objs, out, views=('side', 'front34', 'top'), W=420, H=320, cull=True):
    rows = [load(p) for p in objs]
    img = Image.new('RGB', (W * len(views), H * len(rows)), 'white')
    for i, m in enumerate(rows):
        for j, v in enumerate(views):
            img.paste(draw(m, VIEWS[v], W, H, f'{m["title"]} - {v}' if j == 0 else v, cull), (j * W, i * H))
    img.save(out)
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('objs', nargs='+')
    ap.add_argument('--out', required=True)
    ap.add_argument('--views', default='side,front34,top')
    ap.add_argument('--size', default='420x320')
    ap.add_argument('--no-cull', action='store_true')
    a = ap.parse_args()
    W, H = (int(x) for x in a.size.split('x'))
    views = [v for v in a.views.split(',') if v]
    bad = [v for v in views if v not in VIEWS]
    if bad:
        sys.exit(f'unknown views {bad}; known: {", ".join(VIEWS)}')
    print('wrote', sheet(a.objs, a.out, views, W, H, not a.no_cull))


if __name__ == '__main__':
    main()
