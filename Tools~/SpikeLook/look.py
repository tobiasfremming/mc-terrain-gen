# SPIKE (spike/kit-archetypes): the offline look loop as one command.
#   python look.py --tg <TerrainGen> --out <dir> A,B [C,D ...] [--palette stone] [--check]
# Each argument is a row of species shown side by side (e.g. "RockCrab,SpikeArchCrab" = before, after). It builds
# the creature harness with ./extra (SpikeDump / SpikeRest / SpikeGait), dumps every species walking on flat ground
# (skinned mesh in the body frame), renders side / front 3/4 / top views with render.py and writes
#   <out>/compare.png        one row per argument, the species of a row next to each other
#   <out>/<Species>.png      the full 4-view sheet
#   <out>/metrics.txt        rest stance per leg (IK s, pitch: 0 = the leg stands in its authored shape) and gait
#                            (lift / leg, step time in Froude units, duty, knee rise)
# --check also runs the kit checks (creature_kit.py check) per species and appends the summary.
import argparse, os, subprocess, sys
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import render  # noqa: E402

ap = argparse.ArgumentParser()
ap.add_argument('--tg', required=True)
ap.add_argument('--out', required=True)
ap.add_argument('--palette', default='stone')
ap.add_argument('--check', action='store_true')
ap.add_argument('rows', nargs='+')
a = ap.parse_args()
os.makedirs(a.out, exist_ok=True)
rows = [r.split(',') for r in a.rows]
names = [n for r in rows for n in r]
H = os.path.join(a.tg, 'Tools~', 'CreatureHarness')
env = dict(os.environ, SPIKE_DIR=os.path.abspath(a.out), SPIKE_ONLY=';'.join(names), CREATURE_HARNESS_TG=a.tg)

b = subprocess.run(['dotnet', 'build', '-c', 'Release', '-nologo', '-v', 'q', '-p:HarnessExtra=' + os.path.join(HERE, 'extra')], cwd=H, capture_output=True, text=True)
if b.returncode != 0: sys.exit(b.stdout[-3000:])
dll = os.path.join(H, 'bin', 'Release', 'net8.0', 'CreatureHarness.dll')
log = []
for t in ('SpikeDump', 'SpikeRest', 'SpikeGait'):
    r = subprocess.run(['dotnet', dll, 'test', t, '-v'], cwd=H, env=env, capture_output=True, text=True)
    log += [l[6:] for l in r.stdout.splitlines() if l.startswith('    | ')]
if a.check:
    kit = os.path.join(a.tg, '..', '..', '..', '..', 'MarchingCubes', '.claude', 'skills', 'creature-kit', 'scripts', 'creature_kit.py')
    kit = os.environ.get('CREATURE_KIT', kit)
    for n in names:
        r = subprocess.run([sys.executable, kit, '--tg', a.tg, 'check', n], capture_output=True, text=True)
        log.append(n + ' kit check: ' + (r.stdout.strip().splitlines() or ['?'])[-1])
open(os.path.join(a.out, 'metrics.txt'), 'w').write('\n'.join(log) + '\n')

VIEWS = ('side', 'front34', 'top')
W, Hh = 420, 320
cells = {}
for n in names:
    m = render.load(os.path.join(a.out, n + '_walk.obj'))
    C = render.colours(m, render.PAL[a.palette])
    ims = []
    for v in ('side', 'front', 'front34', 'top'):
        R = render.VIEWS[v]; X = m['V'] @ R.T
        ims.append(render.draw(m, R, X.min(axis=0), X.max(axis=0), C, W, Hh, label=n + ' ' + v))
    sheet = Image.new('RGB', (W * 2, Hh * 2), 'white')
    for k, im in enumerate(ims): sheet.paste(im, ((k % 2) * W, (k // 2) * Hh))
    sheet.save(os.path.join(a.out, n + '.png'))
    cells[n] = [ims[0], ims[2], ims[3]]
    tp = os.path.join(a.out, n + '_tuck.obj')
    if os.path.exists(tp):
        # the tucked pose (a species with a Tuck): its own sheet
        render.render(tp, os.path.join(a.out, n + '_tuck.png'), a.palette, ('top', 'side', 'front', 'front34', 'lowfront34', 'rear34'), W, Hh)
cols = max(len(r) for r in rows)
out = Image.new('RGB', (W * len(VIEWS) * cols, Hh * len(rows)), 'white')
for i, r in enumerate(rows):
    for j, n in enumerate(r):
        for k, im in enumerate(cells[n]): out.paste(im, ((j * len(VIEWS) + k) * W, i * Hh))
d = ImageDraw.Draw(out)
for j in range(1, cols): d.line([(j * len(VIEWS) * W, 0), (j * len(VIEWS) * W, out.height)], fill=(0, 0, 0), width=4)
out.save(os.path.join(a.out, 'compare.png'))
print('\n'.join(log))
print('wrote', os.path.join(a.out, 'compare.png'))
