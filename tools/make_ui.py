"""Paints the game's medieval UI kit and item icons into assets/ui/*.png.

Everything is drawn from code (signed-distance shapes, value noise and a height field lit like
embossed metal), so it is reproducible and licence-free: run `python tools/make_ui.py` from the
project root, then let Godot import the files (`godot --headless --import`). Needs numpy only.

Kit (9-slice textures; the margins are in UiTheme.cs):
  panel.png        oak planks inside an ornate gold frame (corner filigree and garnets)
  button*.png      walnut plank buttons with a thin gold rim: normal / hover / pressed / disabled
  slot*.png        recessed item slots: normal / hover / active
  bar_frame.png    gold frame for the health and stamina bars; bar_*.png are the fills
  parchment.png    tooltips and notes
  plate.png        a dark red title plate with a gold rim
  divider.png      a gold flourish rule
  icons/<id>.png   one 64 x 64 icon per item
"""
import os
import struct
import zlib

import numpy as np

OUT = os.path.join(os.path.dirname(__file__), "..", "assets", "ui")
rng = np.random.default_rng(7)
SS = 3  # supersampling for icons and ornaments


# ---------------------------------------------------------------- png

def save_png(path, rgba):
    """rgba: float array (h, w, 4) in 0..1."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    a = (np.clip(rgba, 0, 1) * 255 + 0.5).astype(np.uint8)
    h, w, _ = a.shape
    raw = b"".join(b"\x00" + a[y].tobytes() for y in range(h))

    def chunk(tag, data):
        c = tag + data
        return struct.pack(">I", len(data)) + c + struct.pack(">I", zlib.crc32(c) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


def downsample(img, f):
    h, w = img.shape[0] // f, img.shape[1] // f
    return img[: h * f, : w * f].reshape(h, f, w, f, -1).mean(axis=(1, 3))


# ---------------------------------------------------------------- fields

def grid(w, h, scale=1.0):
    """Pixel-centre coordinates (x, y) in 'design units' (scale = pixels per unit)."""
    y, x = np.mgrid[0:h, 0:w].astype(np.float64)
    return (x + 0.5) / scale, (y + 0.5) / scale


def value_noise(w, h, cells_x, cells_y, wrap=True, seed=None):
    r = np.random.default_rng(seed) if seed is not None else rng
    g = r.random((cells_y + 1, cells_x + 1))
    if wrap:
        g[-1, :] = g[0, :]
        g[:, -1] = g[:, 0]
    y, x = np.mgrid[0:h, 0:w].astype(np.float64)
    fx, fy = x / w * cells_x, y / h * cells_y
    ix, iy = np.floor(fx).astype(int), np.floor(fy).astype(int)
    tx, ty = fx - ix, fy - iy
    tx, ty = tx * tx * (3 - 2 * tx), ty * ty * (3 - 2 * ty)
    a, b = g[iy, ix], g[iy, ix + 1]
    c, d = g[iy + 1, ix], g[iy + 1, ix + 1]
    return (a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty


def fbm(w, h, cx, cy, octaves=4, wrap=True, seed=None):
    total, amp, norm = 0.0, 1.0, 0.0
    for o in range(octaves):
        total = total + amp * value_noise(w, h, cx * 2 ** o, cy * 2 ** o, wrap, None if seed is None else seed + o)
        norm += amp
        amp *= 0.5
    return total / norm


# signed distances (negative inside)
def sd_circle(x, y, cx, cy, r):
    return np.hypot(x - cx, y - cy) - r


def sd_ellipse(x, y, cx, cy, rx, ry):
    # cheap approximation, good enough for icons
    k = np.hypot((x - cx) / rx, (y - cy) / ry)
    return (k - 1) * min(rx, ry)


def sd_box(x, y, cx, cy, hw, hh, r=0.0):
    qx, qy = np.abs(x - cx) - hw + r, np.abs(y - cy) - hh + r
    return np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0) - r


def sd_segment(x, y, ax, ay, bx, by, r):
    px, py, dx, dy = x - ax, y - ay, bx - ax, by - ay
    t = np.clip((px * dx + py * dy) / (dx * dx + dy * dy + 1e-9), 0, 1)
    return np.hypot(px - dx * t, py - dy * t) - r


def sd_poly(x, y, pts):
    pts = np.asarray(pts, dtype=np.float64)
    d = np.full(x.shape, 1e9)
    inside = np.zeros(x.shape, dtype=bool)
    n = len(pts)
    for i in range(n):
        ax, ay = pts[i]
        bx, by = pts[(i + 1) % n]
        d = np.minimum(d, sd_segment(x, y, ax, ay, bx, by, 0))
        cond = ((ay > y) != (by > y)) & (x < (bx - ax) * (y - ay) / (by - ay + 1e-12) + ax)
        inside ^= cond
    return np.where(inside, -d, d)


def union(*ds):
    return np.minimum.reduce(ds)


def cover(sd, soft=0.75):
    return np.clip(0.5 - sd / soft, 0, 1)


def over(img, color, alpha):
    """Composite a flat or per-pixel colour with coverage alpha onto img (premultiplied-free)."""
    color = np.asarray(color, dtype=np.float64)
    if color.ndim == 1:
        color = np.broadcast_to(color[:3], img.shape[:2] + (3,))
    a = alpha[..., None]
    out_a = a + img[..., 3:4] * (1 - a)
    rgb = (color[..., :3] * a + img[..., :3] * img[..., 3:4] * (1 - a)) / np.maximum(out_a, 1e-6)
    img[..., :3] = rgb
    img[..., 3:4] = out_a


def lit(height, strength=1.0, light=(-0.55, -0.75, 0.9)):
    """Lambert + specular terms for a height field (image y grows downwards)."""
    gy, gx = np.gradient(height)
    nx, ny, nz = -gx * strength, -gy * strength, np.ones_like(height)
    n = np.sqrt(nx * nx + ny * ny + nz * nz)
    nx, ny, nz = nx / n, ny / n, nz / n
    L = np.array(light) / np.linalg.norm(light)
    diff = np.clip(nx * L[0] + ny * L[1] + nz * L[2], 0, 1)
    H = L + np.array([0, 0, 1.0])
    H /= np.linalg.norm(H)
    spec = np.clip(nx * H[0] + ny * H[1] + nz * H[2], 0, 1) ** 28
    return diff, spec


def ramp(t, stops):
    """Colour ramp: stops = [(t, (r, g, b)), ...]."""
    t = np.clip(t, 0, 1)
    out = np.zeros(t.shape + (3,))
    for (t0, c0), (t1, c1) in zip(stops, stops[1:]):
        m = (t >= t0) & (t <= t1)
        k = ((t - t0) / max(t1 - t0, 1e-6))[m][:, None]
        out[m] = np.array(c0) * (1 - k) + np.array(c1) * k
    return out


GOLD = [(0.0, (0.16, 0.08, 0.02)), (0.4, (0.45, 0.28, 0.08)), (0.68, (0.76, 0.54, 0.2)), (0.88, (0.96, 0.8, 0.44)), (1.0, (1.0, 0.95, 0.78))]


def gold_shade(height, strength=1.0):
    diff, spec = lit(height, strength * 1.7)
    col = ramp(diff * 0.95 + 0.05, GOLD)
    return np.clip(col + spec[..., None] * np.array([1.0, 0.95, 0.75]) * 0.7, 0, 1)


def wood(w, h, base, plank=None, horizontal=False, seed=1, grain=1.0, wrap=True):
    """Planks with grain. base = (r, g, b); plank = plank width in px (None = one board)."""
    if horizontal:
        return np.transpose(wood(h, w, base, plank, False, seed, grain, wrap), (1, 0, 2))
    x, y = grid(w, h)
    warp = fbm(w, h, 3, 2, 3, wrap, seed) * 6
    streaks = fbm(w, h, 24, 2, 3, wrap, seed + 10)
    g = 0.5 + 0.5 * np.sin((x + warp * 3) * 0.9 + streaks * 5)
    rings = fbm(w, h, 6, 4, 4, wrap, seed + 20)
    t = 0.55 + 0.25 * (g - 0.5) * grain + 0.35 * (rings - 0.5)
    col = np.array(base) * (0.65 + 0.7 * t)[..., None]
    if plank:
        idx = (x // plank).astype(int)
        tone = 0.88 + 0.24 * np.random.default_rng(seed).random(int(w // plank) + 2)[idx]
        col *= tone[..., None]
        edge = np.minimum(x % plank, plank - x % plank)
        col *= np.clip(0.55 + edge * 0.3, 0, 1)[..., None]
        # nail heads near the plank ends
    return np.clip(col, 0, 1)


def rgba(w, h):
    return np.zeros((h, w, 4))


# ---------------------------------------------------------------- frame pieces

def frame_height(x, y, w, h, bands, rr):
    """Height profile of a bevelled frame. bands: list of (from, to, kind) in px from the outer edge;
    kind 'dome' (rounded ridge), 'groove', 'flat'. Returns (height, distance-from-edge, inside mask)."""
    d = -sd_box(x, y, w / 2, h / 2, w / 2, h / 2, rr)  # >0 inside, distance from the edge
    hgt = np.zeros_like(d)
    for a, b, kind in bands:
        m = (d >= a) & (d < b)
        t = (d[m] - a) / (b - a)
        if kind == "dome":
            hgt[m] = np.sin(t * np.pi) * (b - a) * 0.5
        elif kind == "groove":
            hgt[m] = -np.sin(t * np.pi) * (b - a) * 0.35
        elif kind == "flat":
            hgt[m] = (b - a) * 0.25
        elif kind == "step":
            hgt[m] = (1 - t) * (b - a) * 0.5
    return hgt, d


def corner_ornament(x, y, cx, cy, sx, sy, size):
    """Filigree in one corner (cx, cy) whose arms run along +sx and +sy: a rosette boss, two curls
    and leaf points. Returns (height add, gem coverage)."""
    hgt = np.zeros_like(x)
    # rosette boss
    r = size * 0.3
    dd = np.hypot(x - cx, y - cy)
    boss = np.clip(1 - (dd / r) ** 2, 0, 1)
    hgt = np.maximum(hgt, np.sqrt(boss) * r * 0.55)
    # petals around the boss
    for k in range(8):
        a = k * np.pi / 4
        px, py = cx + np.cos(a) * r * 1.05, cy + np.sin(a) * r * 1.05
        pd = np.hypot(x - px, y - py)
        hgt = np.maximum(hgt, np.sqrt(np.clip(1 - (pd / (r * 0.38)) ** 2, 0, 1)) * r * 0.3)
    # curls along each arm
    for (ax, ay) in ((sx, 0), (0, sy)):
        for j, rad in enumerate((0.26, 0.17)):
            ccx = cx + ax * size * (0.62 + j * 0.42) + (0 if ax else 0)
            ccy = cy + ay * size * (0.62 + j * 0.42)
            ring = np.abs(np.hypot(x - ccx, y - ccy) - size * rad)
            tube = size * 0.055
            hgt = np.maximum(hgt, np.sqrt(np.clip(1 - (ring / tube) ** 2, 0, 1)) * tube * 1.2)
    # gem in the centre
    gem = cover(sd_circle(x, y, cx, cy, r * 0.5), 0.8)
    return hgt, gem


def gem_shade(x, y, cx, cy, r, col=(0.62, 0.05, 0.12)):
    dd = np.hypot(x - cx, y - cy) / r
    hgt = np.sqrt(np.clip(1 - dd ** 2, 0, 1)) * r * 0.8
    diff, spec = lit(hgt, 1.6)
    c = np.array(col) * (0.35 + 0.95 * diff)[..., None] + spec[..., None] * 1.2
    return np.clip(c, 0, 1)


# ---------------------------------------------------------------- kit

def make_panel():
    """Ornate frame, 256 px, 9-slice margin 52 (the centre 152 px tiles)."""
    S = 256
    M = 52
    f = SS
    x, y = grid(S * f, S * f, f)
    img = rgba(S * f, S * f)

    # Wood in the middle (tileable across the centre region): oak planks.
    wd = wood(S * f, S * f, (0.46, 0.29, 0.15), plank=38 * f, seed=3, grain=1.0)
    hgt, d = frame_height(x, y, S, S, [(0, 4, "dome"), (4, 6, "groove"), (6, 15, "dome"), (15, 17, "groove"), (17, 21, "dome"), (21, 24, "step")], 10)
    inside = d > 0
    # Wood shadowed under the frame lip.
    shade = np.clip((d - 21) / 10, 0, 1) * 0.55 + 0.45
    wood_rgb = wd * shade[..., None]
    over(img, wood_rgb, (inside & (d >= 21)).astype(float))

    # Ornaments in the four corners sit on the frame.
    gem_cov = np.zeros_like(x)
    gem_col = np.zeros(x.shape + (3,))
    orn = np.zeros_like(x)
    c0 = 22.0
    for cx, cy, sx, sy in ((c0, c0, 1, 1), (S - c0, c0, -1, 1), (c0, S - c0, 1, -1), (S - c0, S - c0, -1, -1)):
        h2, g = corner_ornament(x, y, cx, cy, sx, sy, 30)
        orn = np.maximum(orn, h2)
        gem_col += gem_shade(x, y, cx, cy, 30 * 0.3 * 0.5) * g[..., None]
        gem_cov = np.maximum(gem_cov, g)
    # Small rivets along the edges (at the 9-slice centre lines they'd stretch, so only near corners).
    for cx, cy in ((58, 10.5), (S - 58, 10.5), (58, S - 10.5), (S - 58, S - 10.5), (10.5, 58), (10.5, S - 58), (S - 10.5, 58), (S - 10.5, S - 58)):
        dd = np.hypot(x - cx, y - cy)
        orn = np.maximum(orn, np.sqrt(np.clip(1 - (dd / 3.2) ** 2, 0, 1)) * 2.4 + 0.0)
    total = np.maximum(hgt, orn + np.where(orn > 0, 1.0, 0))
    frame_mask = ((d >= 0) & (d < 21)) | (orn > 0.01)
    gold = gold_shade(total * f, 1.0)
    over(img, gold, cover(-d, 0.8 * f) * frame_mask.astype(float))
    over(img, gem_col / np.maximum(gem_cov[..., None], 1e-6), gem_cov)
    # Dark line where frame meets wood.
    line = cover(np.abs(d - 21) - 0.6, 0.8) * 0.6
    over(img, (0.12, 0.06, 0.02), line)
    save_png(os.path.join(OUT, "panel.png"), downsample(img, f))


def make_button(name, wood_base, gold_lift, pressed=False, desat=False):
    """Button, 96 x 44, 9-slice margin 14."""
    W, H = 96, 44
    f = SS
    x, y = grid(W * f, H * f, f)
    img = rgba(W * f, H * f)
    wd = wood(W * f, H * f, wood_base, plank=None, horizontal=True, seed=11, grain=0.8)
    hgt, d = frame_height(x, y, W, H, [(0, 3, "dome"), (3, 4.2, "groove")], 7)
    inside = d > 0
    # Inner bevel on the wood: raised (normal) or sunken (pressed).
    bevel = np.clip((d - 4) / 5, 0, 1)
    bh = (bevel if not pressed else -bevel) * 2.5
    diff, _ = lit(bh * f, 1.0)
    wood_rgb = wd * (0.55 + 0.7 * diff)[..., None]
    over(img, wood_rgb, (inside & (d >= 4)).astype(float))
    gold = gold_shade(hgt * f, 1.0) * gold_lift
    over(img, np.clip(gold, 0, 1), cover(-d, 0.8 * f) * ((d < 4.2)).astype(float))
    out = downsample(img, f)
    if desat:
        g = out[..., :3].mean(axis=2, keepdims=True)
        out[..., :3] = (out[..., :3] * 0.35 + g * 0.65) * 0.75
    save_png(os.path.join(OUT, name), out)


def make_slot(name, rim_lift, glow=0.0):
    """Item slot, 64 x 64, 9-slice margin 14: dark recessed leather in a gold rim."""
    S = 64
    f = SS
    x, y = grid(S * f, S * f, f)
    img = rgba(S * f, S * f)
    leather = fbm(S * f, S * f, 8, 8, 4, True, 40)
    base = np.array((0.13, 0.085, 0.06)) * (0.8 + 0.4 * leather)[..., None]
    hgt, d = frame_height(x, y, S, S, [(0, 3.5, "dome"), (3.5, 5, "groove")], 8)
    # Recess: darker toward the rim (inner shadow), a little lighter in the middle.
    inner = np.clip((d - 5) / 12, 0, 1)
    base = base * (0.45 + 0.75 * inner)[..., None]
    over(img, base, (d >= 5).astype(float))
    gold = np.clip(gold_shade(hgt * f, 1.0) * rim_lift, 0, 1)
    over(img, gold, cover(-d, 0.8 * f) * (d < 5).astype(float))
    if glow > 0:
        g = np.exp(-np.clip(d - 5, 0, None) / 4.0) * glow * (d >= 5)
        over(img, (1.0, 0.82, 0.4), np.clip(g, 0, 1))
    save_png(os.path.join(OUT, name), downsample(img, f))


def make_bar_frame():
    """Gold frame for bars, 64 x 28, 9-slice margin 10 (the centre is transparent)."""
    W, H = 64, 28
    f = SS
    x, y = grid(W * f, H * f, f)
    img = rgba(W * f, H * f)
    hgt, d = frame_height(x, y, W, H, [(0, 3, "dome"), (3, 4, "groove"), (4, 6, "step")], 6)
    gold = gold_shade(hgt * f, 1.0)
    over(img, gold, cover(-d, 0.8 * f) * (d < 6).astype(float))
    save_png(os.path.join(OUT, "bar_frame.png"), downsample(img, f))
    # Trough behind the fill.
    trough = rgba(16, 16)
    over(trough, (0.07, 0.045, 0.035), np.ones((16, 16)) * 0.92)
    save_png(os.path.join(OUT, "bar_back.png"), trough)


def make_bar_fill(name, color):
    """A 16 x 32 vertical gradient with a glossy top highlight (stretched sideways)."""
    H = 32
    t = (np.arange(H) + 0.5) / H
    shade = 0.62 + 0.55 * np.exp(-((t - 0.28) / 0.18) ** 2) - 0.25 * t
    col = np.clip(np.array(color)[None, :] * shade[:, None] + np.exp(-((t - 0.2) / 0.08) ** 2)[:, None] * 0.25, 0, 1)
    img = np.ones((H, 16, 4))
    img[..., :3] = col[:, None, :]
    save_png(os.path.join(OUT, name), img)


def make_parchment():
    """Parchment, 128 px, 9-slice margin 18: torn-feeling edge, a brown ink border."""
    S = 128
    f = 2
    x, y = grid(S * f, S * f, f)
    img = rgba(S * f, S * f)
    n = fbm(S * f, S * f, 6, 6, 5, True, 77)
    base = np.array((0.93, 0.85, 0.66)) * (0.86 + 0.22 * n)[..., None]
    d = -sd_box(x, y, S / 2, S / 2, S / 2 - 2, S / 2 - 2, 6)
    edge_n = fbm(S * f, S * f, 16, 16, 3, True, 78) * 3
    d2 = d - edge_n
    burn = np.clip(1 - d2 / 14, 0, 1) ** 2
    base = base * (1 - 0.45 * burn)[..., None] + np.array((0.45, 0.28, 0.12)) * (0.3 * burn)[..., None]
    over(img, base, cover(-d2, f))
    ink = cover(np.abs(d - 8) - 0.7, f) * 0.75
    over(img, (0.36, 0.2, 0.1), ink)
    save_png(os.path.join(OUT, "parchment.png"), downsample(img, f))


def make_plate():
    """Title plate, 128 x 48, 9-slice margin 16: deep red cloth in a gold rim."""
    W, H = 128, 48
    f = SS
    x, y = grid(W * f, H * f, f)
    img = rgba(W * f, H * f)
    weave = fbm(W * f, H * f, 32, 12, 2, True, 90)
    hgt, d = frame_height(x, y, W, H, [(0, 3, "dome"), (3, 4, "groove"), (4, 6.5, "dome")], 6)
    cloth = np.array((0.42, 0.06, 0.08)) * (0.7 + 0.35 * weave)[..., None]
    cloth = cloth * (0.55 + 0.45 * np.clip((d - 6.5) / 8, 0, 1))[..., None]
    over(img, cloth, (d >= 6.5).astype(float))
    over(img, gold_shade(hgt * f, 1.0), cover(-d, 0.8 * f) * (d < 6.5).astype(float))
    save_png(os.path.join(OUT, "plate.png"), downsample(img, f))


def make_divider():
    """A gold rule, 320 x 20: a central lozenge with curls, fading out to the ends."""
    W, H = 320, 20
    f = SS
    x, y = grid(W * f, H * f, f)
    cx, cy = W / 2, H / 2
    hgt = np.zeros_like(x)
    fade = np.clip(1 - np.abs(x - cx) / (W / 2), 0, 1) ** 0.6
    line = np.sqrt(np.clip(1 - ((y - cy) / 1.6) ** 2, 0, 1)) * 1.4 * (np.abs(x - cx) > 14)
    hgt = np.maximum(hgt, line)
    diamond = np.clip(1 - (np.abs(x - cx) / 11 + np.abs(y - cy) / 7), 0, 1)
    hgt = np.maximum(hgt, diamond * 4)
    for s in (-1, 1):
        ring = np.abs(np.hypot(x - (cx + s * 20), y - cy) - 5)
        hgt = np.maximum(hgt, np.sqrt(np.clip(1 - (ring / 1.2) ** 2, 0, 1)) * 1.4)
    mask = (hgt > 0.02).astype(float)
    img = rgba(W * f, H * f)
    over(img, gold_shade(hgt * f, 1.0), mask * fade)
    save_png(os.path.join(OUT, "divider.png"), downsample(img, f))


# ---------------------------------------------------------------- icons

class Icon:
    """64 x 64 icon canvas in design units 0..64, drawn at SS x."""

    def __init__(self):
        self.f = 4
        self.x, self.y = grid(64 * self.f, 64 * self.f, self.f)
        self.img = rgba(64 * self.f, 64 * self.f)
        self.shapes = []

    def fill(self, sd, color, light=True, outline=2.2, grad=0.35, alpha=1.0, dome=0.0):
        """Draw a shape: dark outline, a top-lit gradient and an optional dome shading."""
        x, y, f = self.x, self.y, self.f
        if outline > 0:
            over(self.img, (0.09, 0.055, 0.04), cover(sd - outline, 0.8 / f * 4) * alpha)
        c = np.array(color, dtype=np.float64)
        if light:
            # vertical gradient within the shape's own bounds
            ys = y[sd < 0]
            if ys.size:
                t = (y - ys.min()) / max(ys.max() - ys.min(), 1)
                col = c[None, None, :] * (1 + grad * 0.6 - grad * 1.2 * np.clip(t, 0, 1))[..., None]
            else:
                col = np.broadcast_to(c, x.shape + (3,))
            if dome > 0:
                hgt = np.sqrt(np.clip(-sd, 0, None)) * dome
                diff, spec = lit(hgt * f, 1.0)
                col = col * (0.55 + 0.6 * diff)[..., None] + spec[..., None] * 0.35
        else:
            col = np.broadcast_to(c, x.shape + (3,))
        over(self.img, np.clip(col, 0, 1), cover(sd, 0.8 / f * 4) * alpha)

    def line(self, sd, color, alpha=1.0):
        over(self.img, color, cover(sd, 0.8 / self.f * 4) * alpha)

    def save(self, name):
        out = downsample(self.img, self.f)
        save_png(os.path.join(OUT, "icons", name + ".png"), out)


def mix(c, k):
    return tuple(min(1, v * k) for v in c)


def icon_sword(ic, blade=(0.85, 0.88, 0.92), grip=(0.42, 0.25, 0.12), guard=(0.8, 0.62, 0.25), wooden=False):
    x, y = ic.x, ic.y
    tip = (52, 10)
    base = (22, 40)
    blade_sd = sd_poly(x, y, [(tip[0], tip[1]), (tip[0] - 6, tip[1] + 1.5), (base[0] - 1.5, base[1] - 4), (base[0] + 4, base[1] + 1.5), (tip[0] - 1.5, tip[1] + 6)])
    ic.fill(blade_sd, blade, dome=0.5)
    if not wooden:
        ic.line(sd_segment(x, y, base[0] + 2, base[1] - 2, tip[0] - 3, tip[1] + 3, 0.6), (1, 1, 1), 0.55)
    ic.fill(sd_segment(x, y, 15, 33, 29, 47, 3.2), guard, dome=0.6)
    ic.fill(sd_segment(x, y, 20, 44, 11, 53, 3.0), grip)
    ic.fill(sd_circle(x, y, 9.5, 54.5, 4), guard, dome=0.8)


def icon_club(ic):
    x, y = ic.x, ic.y
    ic.fill(sd_segment(x, y, 16, 50, 30, 30, 3.5), (0.5, 0.32, 0.16))
    ic.fill(sd_ellipse(x, y, 38, 22, 13, 10), (0.55, 0.55, 0.58), dome=0.6)
    for cx, cy in ((32, 17), (43, 17), (45, 27), (33, 28)):
        ic.fill(sd_circle(x, y, cx, cy, 2.4), (0.38, 0.38, 0.42), outline=1.0)


def icon_axe(ic, head=(0.44, 0.42, 0.42)):
    x, y = ic.x, ic.y
    ic.fill(sd_segment(x, y, 14, 54, 40, 14, 3.0), (0.5, 0.32, 0.16))
    ic.fill(sd_segment(x, y, 15, 52, 20, 45, 3.4), (0.36, 0.24, 0.14))  # grip wrap
    # wedge head: the bit flares out to the upper right
    bit = sd_poly(x, y, [(34, 13), (42, 19), (54, 14), (57, 30), (48, 33), (38, 27)])
    ic.fill(bit, head, dome=0.5)
    ic.line(sd_segment(x, y, 54, 15, 57, 29, 0.9), mix(head, 1.5), 0.9)  # bright edge
    ic.fill(sd_segment(x, y, 35, 20, 41, 24, 2.6), (0.36, 0.24, 0.14), outline=1.0)  # lashing


def icon_pickaxe(ic, head=(0.44, 0.42, 0.42)):
    x, y = ic.x, ic.y
    ic.fill(sd_segment(x, y, 14, 54, 38, 18, 3.0), (0.5, 0.32, 0.16))
    ic.fill(sd_segment(x, y, 15, 52, 20, 45, 3.4), (0.36, 0.24, 0.14))
    # a curved head across the haft's top, two points bending down
    arm = sd_poly(x, y, [(14, 22), (24, 12), (38, 9), (52, 12), (60, 22), (50, 18), (38, 16), (26, 19)])
    ic.fill(arm, head, dome=0.5)
    ic.line(sd_segment(x, y, 26, 14, 50, 14, 0.7), mix(head, 1.45), 0.6)
    ic.fill(sd_segment(x, y, 34, 18, 40, 22, 2.6), (0.36, 0.24, 0.14), outline=1.0)


def icon_ore(ic):
    x, y = ic.x, ic.y
    pts = [(12, 44), (16, 24), (30, 13), (47, 17), (55, 33), (48, 49), (26, 52)]
    ic.fill(sd_poly(x, y, pts), (0.42, 0.33, 0.3))
    for cx, cy, r in ((25, 27, 4.5), (40, 23, 3.5), (36, 40, 5.0), (47, 36, 3.0), (21, 42, 3.0)):
        ic.fill(sd_circle(x, y, cx, cy, r), (0.85, 0.5, 0.26), outline=1.0, dome=0.8)


def icon_ingot(ic):
    x, y = ic.x, ic.y
    top = sd_poly(x, y, [(14, 30), (24, 20), (52, 20), (44, 30)])
    front = sd_poly(x, y, [(14, 30), (44, 30), (50, 46), (8, 46)])
    side = sd_poly(x, y, [(44, 30), (52, 20), (58, 36), (50, 46)])
    ic.fill(front, (0.58, 0.6, 0.66), grad=0.2)
    ic.fill(side, (0.45, 0.47, 0.52), grad=0.1)
    ic.fill(top, (0.82, 0.84, 0.9), grad=0.1)
    ic.line(sd_segment(x, y, 20, 25, 44, 25, 0.6), (1, 1, 1), 0.5)


def icon_log(ic):
    x, y = ic.x, ic.y
    body = sd_box(x, y, 30, 34, 20, 11, 6)
    ic.fill(body, (0.55, 0.34, 0.17))
    for k in range(3):
        ic.line(sd_segment(x, y, 14, 28 + k * 5, 42, 28 + k * 5 + (k - 1), 0.5), (0.3, 0.17, 0.08), 0.6)
    end = sd_ellipse(x, y, 50, 34, 7, 11)
    ic.fill(end, (0.86, 0.68, 0.42), grad=0.15)
    for r in (2.5, 5.0, 7.5):
        ic.line(np.abs(sd_ellipse(x, y, 50, 34, r * 0.64, r)) - 0.45, (0.55, 0.36, 0.18), 0.8)


def icon_stone(ic):
    x, y = ic.x, ic.y
    pts = [(14, 44), (18, 26), (30, 16), (46, 19), (54, 34), (48, 48), (28, 51)]
    ic.fill(sd_poly(x, y, pts), (0.6, 0.6, 0.64))
    facet = sd_poly(x, y, [(18, 26), (30, 16), (46, 19), (36, 30)])
    ic.fill(facet, (0.78, 0.78, 0.82), outline=0, grad=0.1)
    ic.line(sd_segment(x, y, 36, 30, 34, 50, 0.5), (0.3, 0.3, 0.34), 0.6)
    ic.line(sd_segment(x, y, 36, 30, 54, 34, 0.5), (0.3, 0.3, 0.34), 0.6)


def icon_pelt(ic, col=(0.55, 0.5, 0.46)):
    x, y = ic.x, ic.y
    pts = [(32, 8), (40, 14), (52, 12), (50, 24), (56, 34), (48, 40), (50, 54), (38, 50), (32, 58), (26, 50), (14, 54), (16, 40), (8, 34), (14, 24), (12, 12), (24, 14)]
    ic.fill(sd_poly(x, y, pts), col)
    ic.fill(sd_ellipse(x, y, 32, 32, 9, 14), mix(col, 1.25), outline=0, grad=0.1)
    for k in range(7):
        a = k * 0.9
        ic.line(sd_segment(x, y, 32 + np.cos(a) * 4, 30 + np.sin(a) * 8, 32 + np.cos(a) * 7, 30 + np.sin(a) * 12, 0.45), mix(col, 0.6), 0.6)


def icon_feather(ic, col=(0.55, 0.6, 0.75)):
    x, y = ic.x, ic.y
    vane = sd_poly(x, y, [(50, 8), (54, 14), (42, 36), (24, 50), (20, 46), (30, 28)])
    ic.fill(vane, col, dome=0.3)
    ic.line(sd_segment(x, y, 14, 56, 50, 10, 0.9), (0.95, 0.92, 0.85))
    for k in range(5):
        t = 0.3 + k * 0.12
        px, py = 14 + 36 * t, 56 - 46 * t
        ic.line(sd_segment(x, y, px, py, px + 5, py + 4, 0.4), mix(col, 0.6), 0.7)


def icon_nails(ic):
    x, y = ic.x, ic.y
    for i, (ax, ay, bx, by) in enumerate(((16, 14, 26, 52), (30, 10, 32, 50), (46, 14, 38, 52))):
        ic.fill(sd_segment(x, y, ax, ay, bx, by, 2.0), (0.6, 0.62, 0.66), dome=0.4)
        ic.fill(sd_ellipse(x, y, ax, ay, 5.2, 2.6), (0.7, 0.72, 0.76), dome=0.5)


def icon_coin(ic):
    x, y = ic.x, ic.y
    ic.fill(sd_circle(x, y, 32, 33, 20), (0.95, 0.74, 0.25), dome=0.5)
    ic.line(np.abs(sd_circle(x, y, 32, 33, 15)) - 0.7, (0.6, 0.4, 0.1), 0.8)
    ic.fill(sd_poly(x, y, [(32, 23), (38, 33), (32, 43), (26, 33)]), (1.0, 0.9, 0.5), outline=1.0)


def icon_wheat(ic):
    x, y = ic.x, ic.y
    for dx, ang in ((-6, -0.25), (0, 0), (6, 0.25)):
        ic.line(sd_segment(x, y, 32 + dx * 0.3, 58, 32 + dx + ang * 20, 24, 0.9), (0.6, 0.55, 0.2))
        for k in range(5):
            cy = 26 - k * 4.2
            cx = 32 + dx + ang * 20 + ang * (26 - cy) * 0.7
            for s in (-1, 1):
                ic.fill(sd_ellipse(x, y, cx + s * 2.6, cy, 2.6, 3.8), (0.92, 0.76, 0.32), outline=1.0, grad=0.2)


def icon_sack(ic, tint=(0.75, 0.6, 0.4), seeds=(0.55, 0.4, 0.2)):
    x, y = ic.x, ic.y
    ic.fill(union(sd_ellipse(x, y, 32, 40, 17, 16), sd_poly(x, y, [(24, 26), (40, 26), (36, 18), (28, 18)])), (0.72, 0.58, 0.4))
    ic.fill(sd_box(x, y, 32, 22, 7, 2, 1), (0.42, 0.26, 0.12), outline=1.0)
    ic.fill(sd_ellipse(x, y, 32, 15, 7, 4), (0.65, 0.5, 0.32), outline=1.2)
    for cx, cy in ((26, 40), (34, 44), (38, 36), (30, 34), (28, 48)):
        ic.fill(sd_ellipse(x, y, cx, cy, 2.2, 1.6), tint, outline=0.8, grad=0.1)


def icon_turnip(ic, body=(0.92, 0.88, 0.95), top=(0.62, 0.3, 0.6), roasted=False):
    x, y = ic.x, ic.y
    for a in (-0.5, 0, 0.5):
        ic.fill(sd_ellipse(x, y, 32 + a * 18, 14 - abs(a) * 4, 4.5, 9), (0.3, 0.6, 0.25), outline=1.4)
    bulb = union(sd_circle(x, y, 32, 38, 14), sd_poly(x, y, [(26, 48), (38, 48), (32, 60)]))
    if roasted:
        ic.fill(bulb, (0.72, 0.42, 0.2), dome=0.6)
    else:
        ic.fill(bulb, body, dome=0.5)
        ic.fill(union(sd_circle(x, y, 32, 34, 13.5)) + 0 * x + np.maximum(0, y - 34) * 10, top, outline=0, grad=0.1)
    if roasted:
        for k in range(3):
            ic.line(sd_segment(x, y, 22 + k * 10, 6 + (k % 2) * 3, 24 + k * 10, 14, 0.8), (1, 1, 1), 0.45)


def icon_bread(ic):
    x, y = ic.x, ic.y
    ic.fill(sd_ellipse(x, y, 32, 36, 22, 13), (0.8, 0.52, 0.22), dome=0.6)
    for k in range(3):
        ic.line(sd_segment(x, y, 20 + k * 9, 30, 26 + k * 9, 40, 0.8), (0.95, 0.82, 0.55), 0.9)


def icon_water(ic):
    x, y = ic.x, ic.y
    body = union(sd_ellipse(x, y, 32, 40, 15, 16), sd_box(x, y, 32, 20, 5, 7, 1))
    ic.fill(body, (0.55, 0.38, 0.22), dome=0.4)
    ic.fill(sd_box(x, y, 32, 12, 6, 3, 1), (0.4, 0.25, 0.12), outline=1.2)
    ic.fill(sd_ellipse(x, y, 32, 42, 9, 9), (0.35, 0.6, 0.9), outline=1.0, dome=0.5)
    ic.line(sd_segment(x, y, 18, 30, 46, 30, 0.9), (0.35, 0.2, 0.1))


def icon_scroll(ic):
    x, y = ic.x, ic.y
    ic.fill(sd_box(x, y, 32, 32, 18, 14, 1), (0.36, 0.55, 0.85))
    ic.fill(sd_ellipse(x, y, 14, 32, 4, 15), (0.3, 0.45, 0.72))
    ic.fill(sd_ellipse(x, y, 50, 32, 4, 15), (0.3, 0.45, 0.72))
    for k in range(3):
        ic.line(sd_segment(x, y, 22, 25 + k * 6, 42, 25 + k * 6, 0.5), (0.9, 0.95, 1), 0.8)
    ic.line(np.abs(sd_poly(x, y, [(26, 42), (32, 34), (38, 42)])) - 0.5, (0.9, 0.95, 1), 0.8)


def icon_glider(ic):
    x, y = ic.x, ic.y
    ic.fill(sd_poly(x, y, [(6, 40), (32, 14), (58, 40), (32, 32)]), (0.82, 0.32, 0.28), dome=0.2)
    ic.line(sd_segment(x, y, 32, 14, 32, 52, 1.0), (0.42, 0.25, 0.12))
    ic.line(sd_segment(x, y, 14, 38, 50, 38, 1.0), (0.42, 0.25, 0.12))


def icon_cap(ic, col, metal=False):
    x, y = ic.x, ic.y
    dome = union(sd_ellipse(x, y, 32, 36, 19, 20)) + np.maximum(0, y - 38) * 10
    ic.fill(dome, col, dome=0.8 if metal else 0.4)
    ic.fill(sd_box(x, y, 32, 40, 21, 3.5, 1.5), mix(col, 0.8), outline=1.6)
    if metal:
        ic.fill(sd_box(x, y, 32, 46, 2.5, 8, 1), mix(col, 1.1), outline=1.4, dome=0.4)
        ic.line(sd_segment(x, y, 32, 17, 32, 38, 1.0), mix(col, 1.25), 0.8)
        for cx in (16, 24, 40, 48):
            ic.fill(sd_circle(x, y, cx, 40, 1.4), (0.85, 0.85, 0.9), outline=0.6)
    else:
        for k in range(4):
            ic.line(sd_segment(x, y, 19 + k * 9, 30, 21 + k * 9, 36, 0.5), mix(col, 0.6), 0.7)


def icon_tunic(ic, col, studs=False):
    x, y = ic.x, ic.y
    pts = [(22, 10), (28, 14), (36, 14), (42, 10), (56, 18), (52, 30), (46, 28), (46, 56), (18, 56), (18, 28), (12, 30), (8, 18)]
    ic.fill(sd_poly(x, y, pts), col, dome=0.25)
    ic.line(np.abs(sd_poly(x, y, [(28, 14), (32, 22), (36, 14)])) - 0.5, mix(col, 0.55), 0.9)
    ic.fill(sd_box(x, y, 32, 44, 14, 2.5, 0.5), (0.3, 0.18, 0.1), outline=1.0)
    ic.fill(sd_box(x, y, 32, 44, 2.6, 2.6, 0.5), (0.9, 0.72, 0.3), outline=0.8)
    if studs:
        for cx in (23, 29, 35, 41):
            for cy in (26, 33):
                ic.fill(sd_circle(x, y, cx, cy, 1.6), (0.85, 0.85, 0.9), outline=0.6, dome=0.6)


def icon_glove(ic, col):
    x, y = ic.x, ic.y
    palm = sd_box(x, y, 30, 38, 11, 12, 5)
    fingers = union(*[sd_segment(x, y, 22 + k * 5.5, 30, 22 + k * 5.5, 14 + abs(k - 1.5) * 3, 2.6) for k in range(4)])
    thumb = sd_segment(x, y, 40, 40, 48, 30, 2.8)
    ic.fill(union(palm, fingers, thumb), col, dome=0.3)
    ic.fill(sd_box(x, y, 30, 52, 12, 4, 1.5), mix(col, 0.75), outline=1.4)


def icon_boot(ic, col, trim=None):
    x, y = ic.x, ic.y
    shape = union(sd_box(x, y, 26, 28, 9, 18, 3), sd_box(x, y, 36, 47, 18, 7, 4))
    ic.fill(shape, col, dome=0.3)
    ic.fill(sd_box(x, y, 36, 54, 19, 2.2, 1), (0.22, 0.14, 0.08), outline=1.0)
    ic.fill(sd_box(x, y, 26, 12, 10, 3, 1.5), trim or mix(col, 0.75), outline=1.2)
    if trim:
        for k in range(3):
            ic.line(sd_segment(x, y, 20, 20 + k * 6, 32, 22 + k * 6, 0.7), trim, 0.9)


def icon_cloak(ic, col, clasp=(0.9, 0.72, 0.3), feathers=False):
    x, y = ic.x, ic.y
    pts = [(24, 10), (40, 10), (46, 20), (54, 56), (10, 56), (18, 20)]
    ic.fill(sd_poly(x, y, pts), col, dome=0.2)
    for k in range(3):
        ic.line(sd_segment(x, y, 24 + k * 8, 22, 20 + k * 12, 54, 0.6), mix(col, 0.6), 0.7)
    ic.fill(sd_box(x, y, 32, 12, 9, 3, 1.5), mix(col, 0.8), outline=1.2)
    ic.fill(sd_circle(x, y, 32, 14, 3.4), clasp, outline=1.0, dome=0.7)
    if feathers:
        for cx, cy in ((18, 50), (26, 52), (34, 52), (42, 51), (49, 49)):
            ic.fill(sd_ellipse(x, y, cx, cy, 3, 6), (0.85, 0.85, 0.9), outline=0.9)


def icon_charm(ic, col, gem):
    x, y = ic.x, ic.y
    ic.line(np.abs(sd_ellipse(x, y, 32, 22, 16, 16)) - 0.8, (0.6, 0.45, 0.25))
    ic.fill(sd_circle(x, y, 32, 42, 12), col, dome=0.6)
    ic.fill(sd_circle(x, y, 32, 42, 6), gem, outline=1.0, dome=0.9)


def icon_tooth(ic):
    x, y = ic.x, ic.y
    ic.line(np.abs(sd_ellipse(x, y, 32, 22, 16, 16)) - 0.8, (0.45, 0.3, 0.18))
    ic.fill(sd_poly(x, y, [(26, 34), (38, 34), (34, 56), (31, 58)]), (0.95, 0.92, 0.82), dome=0.4)
    ic.fill(sd_box(x, y, 32, 33, 8, 3, 1), (0.5, 0.32, 0.16), outline=1.0)


def icon_shard(ic):
    x, y = ic.x, ic.y
    ic.fill(sd_poly(x, y, [(32, 6), (46, 26), (40, 56), (24, 56), (18, 26)]), (0.35, 0.72, 0.85), dome=0.3)
    ic.fill(sd_poly(x, y, [(32, 6), (46, 26), (32, 30)]), (0.7, 0.92, 1.0), outline=0, grad=0)
    rune = union(sd_segment(x, y, 32, 30, 32, 48, 0.9), sd_segment(x, y, 32, 36, 38, 30, 0.9), sd_segment(x, y, 32, 42, 26, 36, 0.9))
    ic.line(rune - 1.6, (0.6, 0.95, 1.0), 0.5)
    ic.line(rune, (1.0, 1.0, 1.0), 1.0)


def icon_altar(ic):
    x, y = ic.x, ic.y
    ic.fill(sd_box(x, y, 32, 46, 20, 10, 2), (0.55, 0.55, 0.6))
    ic.fill(sd_box(x, y, 32, 26, 9, 14, 2), (0.6, 0.6, 0.66))
    ic.line(sd_segment(x, y, 32, 18, 32, 34, 1.0), (0.5, 0.9, 1.0))


ICONS = {
    "wooden_sword": lambda ic: icon_sword(ic, blade=(0.78, 0.58, 0.34), wooden=True),
    "stone_club": icon_club,
    "iron_sword": lambda ic: icon_sword(ic, blade=(0.82, 0.85, 0.9), guard=(0.55, 0.57, 0.62)),
    "stone_axe": icon_axe,
    "iron_axe": lambda ic: icon_axe(ic, head=(0.75, 0.77, 0.84)),
    "stone_pickaxe": icon_pickaxe,
    "iron_pickaxe": lambda ic: icon_pickaxe(ic, head=(0.75, 0.77, 0.84)),
    "iron_ore": icon_ore,
    "iron_ingot": icon_ingot,
    "wood": icon_log,
    "stone": icon_stone,
    "wolf_pelt": icon_pelt,
    "harpy_feather": icon_feather,
    "iron_nails": icon_nails,
    "coin": icon_coin,
    "wheat": icon_wheat,
    "wheat_seeds": lambda ic: icon_sack(ic, tint=(0.92, 0.8, 0.4)),
    "turnip_seeds": lambda ic: icon_sack(ic, tint=(0.7, 0.4, 0.7)),
    "turnip": icon_turnip,
    "roast_turnip": lambda ic: icon_turnip(ic, roasted=True),
    "bread": icon_bread,
    "water": icon_water,
    "cottage_blueprint": icon_scroll,
    "glider": icon_glider,
    "leather_cap": lambda ic: icon_cap(ic, (0.55, 0.34, 0.18)),
    "iron_helm": lambda ic: icon_cap(ic, (0.62, 0.64, 0.7), metal=True),
    "leather_jerkin": lambda ic: icon_tunic(ic, (0.55, 0.34, 0.18)),
    "studded_jerkin": lambda ic: icon_tunic(ic, (0.45, 0.28, 0.16), studs=True),
    "leather_gloves": lambda ic: icon_glove(ic, (0.55, 0.34, 0.18)),
    "leather_boots": lambda ic: icon_boot(ic, (0.5, 0.31, 0.16)),
    "grip_boots": lambda ic: icon_boot(ic, (0.4, 0.3, 0.22), trim=(0.75, 0.78, 0.9)),
    "travel_cloak": lambda ic: icon_cloak(ic, (0.24, 0.42, 0.3)),
    "feather_cloak": lambda ic: icon_cloak(ic, (0.5, 0.55, 0.7), feathers=True),
    "wolf_charm": icon_tooth,
    "blossom_charm": lambda ic: icon_charm(ic, (0.9, 0.72, 0.3), (1.0, 0.6, 0.75)),
    "rune_shard": icon_shard,
    "rune_altar": icon_altar,
    "fists": lambda ic: icon_glove(ic, (0.88, 0.66, 0.5)),
}


def make_icons():
    for name, draw in ICONS.items():
        ic = Icon()
        draw(ic)
        ic.save(name)


def make_glint():
    """Enchantment sparkle drawn over an enchanted item's icon (64 x 64, mostly transparent)."""
    ic = Icon()
    x, y = ic.x, ic.y
    for cx, cy, r in ((50, 12, 7), (12, 50, 4.5), (54, 46, 3.5)):
        star = union(sd_segment(x, y, cx - r, cy, cx + r, cy, 0.5), sd_segment(x, y, cx, cy - r, cx, cy + r, 0.5))
        ic.line(star - 1.2, (0.55, 0.85, 1.0), 0.6)
        ic.line(star, (1, 1, 1), 1.0)
    ic.save("_glint")


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    make_panel()
    make_button("button.png", (0.36, 0.22, 0.12), 1.0)
    make_button("button_hover.png", (0.46, 0.29, 0.15), 1.15)
    make_button("button_pressed.png", (0.3, 0.18, 0.1), 0.9, pressed=True)
    make_button("button_disabled.png", (0.3, 0.22, 0.16), 0.8, desat=True)
    make_slot("slot.png", 0.85)
    make_slot("slot_hover.png", 1.1, glow=0.25)
    make_slot("slot_active.png", 1.25, glow=0.6)
    make_bar_frame()
    make_bar_fill("bar_health.png", (0.85, 0.18, 0.2))
    make_bar_fill("bar_stamina.png", (0.95, 0.72, 0.22))
    make_parchment()
    make_plate()
    make_divider()
    make_icons()
    make_glint()
    print("ui kit written to", os.path.normpath(OUT))
