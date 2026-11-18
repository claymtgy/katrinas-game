"""Synthesizes the game's placeholder sound set into assets/audio/*.wav.

Everything is generated from noise and oscillators (no recordings, no licences), so the set is
reproducible: run `python tools/make_audio.py` from the project root, then let Godot import the
files (open the editor once, or `godot --headless --import`). Replace any file with a real
recording of the same name later; the game only cares about the names.

Loops (wind, rain, birds, crickets, waterfall, fire) are cross-faded end-into-start so they
repeat without a click. Needs numpy.
"""
import os
import wave

import numpy as np

SR = 22050
OUT = os.path.join(os.path.dirname(__file__), "..", "assets", "audio")
rng = np.random.default_rng(1234)


# ---------------------------------------------------------------- building blocks

def t_of(sec):
    return np.arange(int(sec * SR)) / SR


def noise(sec):
    return rng.uniform(-1, 1, int(sec * SR))


def lowpass(x, hz):
    """One-pole low-pass, run twice for a gentler 12 dB/oct slope."""
    a = np.exp(-2 * np.pi * hz / SR)
    for _ in range(2):
        y = np.empty_like(x)
        acc = 0.0
        for i, v in enumerate(x):
            acc = (1 - a) * v + a * acc
            y[i] = acc
        x = y
    return x


def highpass(x, hz):
    return x - lowpass(x, hz)


def band(x, lo, hi):
    return lowpass(highpass(x, lo), hi)


def env(n, attack, decay_tau, sustain=0.0):
    """Attack in seconds, exponential decay with time-constant decay_tau."""
    t = np.arange(n) / SR
    a = np.clip(t / max(attack, 1e-4), 0, 1)
    d = np.exp(-np.maximum(t - attack, 0) / decay_tau)
    return a * (sustain + (1 - sustain) * d)


def fade(x, fin=0.005, fout=0.02):
    x = x.copy()
    ni, no = int(fin * SR), int(fout * SR)
    if ni: x[:ni] *= np.linspace(0, 1, ni)
    if no: x[-no:] *= np.linspace(1, 0, no)
    return x


def tone(freqs, sec, phase_jitter=True):
    """Sum of sines; freqs may be arrays (glides)."""
    n = int(sec * SR)
    out = np.zeros(n)
    for f, amp in freqs:
        f = np.broadcast_to(f, (n,)) if np.ndim(f) else np.full(n, f)
        ph = np.cumsum(2 * np.pi * f / SR) + (rng.uniform(0, 6.28) if phase_jitter else 0)
        out += amp * np.sin(ph)
    return out


def loopify(x, xfade=1.0):
    """Cross-fade the tail into the head so the clip loops seamlessly."""
    n = int(xfade * SR)
    head, body, tail = x[:n], x[n:-n] if n else x, x[-n:]
    w = np.linspace(0, 1, n)
    mixed = tail * (1 - w) + head * w
    return np.concatenate([body, mixed]) if n else x


def norm(x, peak=0.85):
    m = np.max(np.abs(x))
    return x * (peak / m) if m > 0 else x


def save(name, x, peak=0.85):
    os.makedirs(OUT, exist_ok=True)
    x = norm(x, peak)
    pcm = (np.clip(x, -1, 1) * 32767).astype(np.int16)
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    print(f"  {name}.wav  {len(x) / SR:.2f}s")


def slow_lfo(sec, rate, depth, base=1.0):
    t = t_of(sec)
    return base + depth * np.sin(2 * np.pi * rate * t + rng.uniform(0, 6.28))


# ---------------------------------------------------------------- ambience loops

def wind():
    sec = 11
    x = lowpass(noise(sec + 2), 380) * 0.9 + band(noise(sec + 2), 500, 1100) * 0.25
    t = t_of(sec + 2)
    gust = 0.55 + 0.3 * np.sin(2 * np.pi * t / 6.5) + 0.15 * np.sin(2 * np.pi * t / 2.3 + 1)
    return loopify(x * gust, 2.0)


def rain():
    sec = 8
    hiss = band(noise(sec + 1), 900, 5500) * 0.5
    patter = np.zeros(int((sec + 1) * SR))
    for _ in range(int(sec * 140)):
        i = rng.integers(0, len(patter) - 400)
        d = band(noise(0.018), 1500, 6000) * env(int(0.018 * SR), 0.0005, 0.004)
        patter[i:i + len(d)] += d * rng.uniform(0.2, 1.0)
    rumble = lowpass(noise(sec + 1), 220) * 0.6
    return loopify(hiss + patter * 0.9 + rumble, 1.0)


def birds():
    sec = 14
    x = np.zeros(int(sec * SR)) + band(noise(sec), 2000, 4000) * 0.004
    for _ in range(11):
        start = rng.uniform(0.2, sec - 2)
        kind = rng.integers(0, 3)
        notes = rng.integers(2, 6)
        for k in range(notes):
            d = rng.uniform(0.06, 0.16)
            f0 = rng.uniform(2600, 4200)
            t = t_of(d)
            if kind == 0:   # falling whistle
                f = f0 * (1.15 - 0.3 * t / d)
            elif kind == 1:  # rising chirp
                f = f0 * (0.8 + 0.35 * t / d)
            else:            # trill
                f = f0 * (1 + 0.06 * np.sin(2 * np.pi * 38 * t))
            c = tone([(f, 1.0), (f * 2, 0.15)], d) * np.sin(np.pi * t / d) ** 2
            i = int((start + k * (d + rng.uniform(0.03, 0.09))) * SR)
            if i + len(c) < len(x): x[i:i + len(c)] += c * rng.uniform(0.3, 0.7)
    return loopify(x, 0.5)


def crickets():
    sec = 7
    x = np.zeros(int(sec * SR))
    for voice in range(3):
        f = rng.uniform(4200, 5200)
        rate = rng.uniform(13, 17)       # pulses per second within a chirp
        period = rng.uniform(0.7, 1.3)   # chirp every …
        t = 0.05 + rng.uniform(0, 0.4)
        while t < sec - 0.4:
            for p in range(rng.integers(3, 5)):
                d = 0.022
                c = tone([(f, 1.0), (f * 1.5, 0.1)], d) * np.sin(np.pi * t_of(d) / d)
                i = int((t + p / rate) * SR)
                x[i:i + len(c)] += c * (0.5 - voice * 0.12)
            t += period * rng.uniform(0.9, 1.1)
    return loopify(x + lowpass(noise(sec), 300) * 0.05, 0.3)


def waterfall():
    sec = 8
    x = lowpass(noise(sec + 1), 1600) + band(noise(sec + 1), 2000, 6000) * 0.25 + lowpass(noise(sec + 1), 140) * 1.2
    return loopify(x * slow_lfo(sec + 1, 0.3, 0.08), 1.0)


def fire():
    sec = 7
    x = lowpass(noise(sec + 1), 260) * 0.7 + band(noise(sec + 1), 600, 2500) * 0.08 * slow_lfo(sec + 1, 0.7, 0.5)
    for _ in range(int(sec * 9)):
        i = rng.integers(0, len(x) - 1200)
        d = 0.012 + rng.uniform(0, 0.03)
        pop = band(noise(d), 1200, 7000) * env(int(d * SR), 0.0003, d / 4)
        x[i:i + len(pop)] += pop * rng.uniform(0.4, 1.6)
    return loopify(x, 0.8)


# ---------------------------------------------------------------- one-shots

def whoosh(sec, lo, hi, peak_at=0.4):
    n = int(sec * SR)
    t = np.arange(n) / n
    e = np.where(t < peak_at, (t / peak_at) ** 2, np.exp(-(t - peak_at) * 9))
    x = noise(sec)
    # sweep: blend a low and a high band by the envelope
    return fade((band(x, lo, hi) * e + lowpass(x, lo) * e * 0.6))


def swing_light(): return whoosh(0.28, 700, 3200, 0.45)
def swing_heavy(): return whoosh(0.45, 300, 1800, 0.55)
def dodge(): return whoosh(0.4, 250, 1400, 0.35) + fade(band(noise(0.4), 2000, 5000) * env(int(0.4 * SR), 0.05, 0.08) * 0.3)


def hit_flesh():
    sec = 0.3
    n = int(sec * SR)
    thump = tone([(np.linspace(140, 60, n), 1.0)], sec) * env(n, 0.002, 0.06)
    smack = lowpass(noise(sec), 1800) * env(n, 0.0005, 0.02) * 0.8
    return fade(thump + smack)


def chop():
    sec = 0.35
    n = int(sec * SR)
    knock = tone([(310, 1.0), (520, 0.6), (870, 0.3)], sec) * env(n, 0.001, 0.05)
    click = band(noise(sec), 1500, 6000) * env(n, 0.0003, 0.008) * 1.2
    crunch = band(noise(sec), 400, 2500) * env(n, 0.002, 0.03) * 0.5
    return fade(knock + click + crunch)


def mine():
    sec = 0.5
    n = int(sec * SR)
    ring = tone([(1240, 0.6), (2930, 0.5), (4110, 0.3), (5370, 0.15)], sec) * env(n, 0.0005, 0.07)
    hit = band(noise(sec), 800, 7000) * env(n, 0.0003, 0.012) * 1.4
    grit = band(noise(sec), 2500, 8000) * env(n, 0.01, 0.06) * 0.25
    return fade(ring + hit + grit)


def rustle():
    sec = 0.35
    n = int(sec * SR)
    x = band(noise(sec), 1500, 6500)
    am = 0.5 + 0.5 * np.abs(np.sin(2 * np.pi * 26 * t_of(sec)))
    return fade(x * am * env(n, 0.02, 0.09))


def block():
    sec = 0.3
    n = int(sec * SR)
    return fade(tone([(np.linspace(220, 160, n), 1.0), (430, 0.3)], sec) * env(n, 0.001, 0.05)
                + lowpass(noise(sec), 1200) * env(n, 0.0005, 0.02))


def parry():
    sec = 1.1
    n = int(sec * SR)
    partials = [(812, 0.7), (1338, 0.5), (2207, 0.45), (3310, 0.3), (4480, 0.2)]
    ring = sum(tone([(f, a)], sec) * env(n, 0.0005, 0.35 / (1 + i * 0.5)) for i, (f, a) in enumerate(partials))
    hit = band(noise(sec), 2000, 8000) * env(n, 0.0003, 0.01)
    return fade(ring + hit)


def step():
    sec = 0.12
    n = int(sec * SR)
    return fade(lowpass(noise(sec), 700) * env(n, 0.004, 0.025) + band(noise(sec), 2000, 5000) * env(n, 0.01, 0.02) * 0.15)


def wolf_howl():
    sec = 2.6
    n = int(sec * SR)
    t = np.arange(n) / SR
    contour = np.interp(t, [0, 0.35, 1.1, 2.0, 2.6], [360, 610, 640, 560, 430])
    vib = 1 + 0.012 * np.sin(2 * np.pi * 5.5 * t) * np.clip(t / 0.8, 0, 1)
    f = contour * vib
    x = tone([(f, 1.0), (f * 2, 0.35), (f * 3, 0.12)], sec)
    breath = band(noise(sec), 500, 2500) * 0.12
    e = np.clip(t / 0.25, 0, 1) * np.clip((sec - t) / 0.6, 0, 1)
    return fade((x + breath) * e)


def wolf_snarl():
    sec = 0.55
    n = int(sec * SR)
    t = t_of(sec)
    am = 0.5 + 0.5 * np.sin(2 * np.pi * 32 * t) ** 2
    body = band(noise(sec), 180, 1100) * am
    tonal = tone([(np.linspace(170, 130, n), 0.5), (np.linspace(340, 260, n), 0.25)], sec) * am
    e = np.clip(t / 0.08, 0, 1) * np.clip((sec - t) / 0.15, 0, 1)
    return fade((body + tonal) * e)


def wolf_yelp():
    sec = 0.32
    n = int(sec * SR)
    f = np.linspace(980, 520, n)
    return fade((tone([(f, 1.0), (f * 2, 0.3)], sec) + band(noise(sec), 800, 3000) * 0.2) * env(n, 0.005, 0.1))


def eat():
    sec = 0.6
    x = np.zeros(int(sec * SR))
    for k in range(3):
        d = 0.07
        b = band(noise(d), 900, 5000) * env(int(d * SR), 0.002, 0.02)
        i = int((0.03 + k * 0.17 + rng.uniform(0, 0.03)) * SR)
        x[i:i + len(b)] += b
    return fade(x)


def pickup():
    sec = 0.25
    n = int(sec * SR)
    a = tone([(880, 1.0), (1760, 0.2)], sec) * env(n, 0.002, 0.05)
    b = np.zeros(n)
    m = int(0.07 * SR)
    b[m:] = (tone([(1318, 1.0), (2636, 0.2)], sec) * env(n, 0.002, 0.06))[: n - m]
    return fade(a * 0.6 + b * 0.6)


def build():
    sec = 0.6
    n = int(sec * SR)
    thunk = tone([(np.linspace(130, 80, n), 1.0), (260, 0.3)], sec) * env(n, 0.002, 0.08)
    rattle = np.zeros(n)
    for k in range(4):
        d = 0.04
        r = tone([(rng.uniform(400, 900), 1.0)], d) * env(int(d * SR), 0.001, 0.012)
        i = int((0.08 + k * 0.06 + rng.uniform(0, 0.02)) * SR)
        rattle[i:i + len(r)] += r * 0.4
    return fade(thunk + rattle + lowpass(noise(sec), 900) * env(n, 0.001, 0.03) * 0.7)


def craft():
    sec = 0.8
    x = np.zeros(int(sec * SR))
    for k in range(3):
        d = 0.18
        c = tone([(1650, 0.6), (2480, 0.4), (3900, 0.2)], d) * env(int(d * SR), 0.0005, 0.04)
        c += band(noise(d), 1500, 6000) * env(int(d * SR), 0.0003, 0.006)
        i = int(k * 0.22 * SR)
        x[i:i + len(c)] += c * (1 - k * 0.15)
    return fade(x)


def water():
    sec = 0.7
    n = int(sec * SR)
    splash = band(noise(sec), 400, 4000) * env(n, 0.01, 0.12)
    bubbles = np.zeros(n)
    for _ in range(9):
        d = 0.05
        f = rng.uniform(500, 1300)
        b = tone([(np.linspace(f, f * 1.6, int(d * SR)), 1.0)], d) * np.sin(np.pi * t_of(d) / d)
        i = rng.integers(int(0.05 * SR), n - len(b))
        bubbles[i:i + len(b)] += b * 0.25
    return fade(splash + bubbles)


def chest():
    sec = 0.7
    n = int(sec * SR)
    t = t_of(sec)
    creak = np.sign(np.sin(2 * np.pi * np.cumsum(np.full(n, 1.0) * (90 + 40 * np.sin(2 * np.pi * 3 * t))) / SR))
    creak = band(creak, 300, 2500) * np.clip(t / 0.05, 0, 1) * np.clip((0.4 - t) / 0.1, 0, 1) * 0.35
    knock = np.zeros(n)
    k = block()
    i = int(0.42 * SR)
    knock[i:i + len(k)] += k[: n - i]
    return fade(creak + knock)


def ui_click():
    sec = 0.06
    n = int(sec * SR)
    return fade(tone([(1400, 1.0)], sec) * env(n, 0.001, 0.012))


def lava():
    sec = 8
    x = lowpass(noise(sec + 1), 90) * 1.0 + lowpass(noise(sec + 1), 400) * 0.2
    for _ in range(14):
        i = rng.integers(0, len(x) - 3000)
        d = 0.12
        g = tone([(rng.uniform(60, 140), 1.0)], d) * np.sin(np.pi * t_of(d) / d)
        x[i:i + len(g)] += g * 0.4
    return loopify(x, 1.0)


# ---------------------------------------------------------------- class abilities

def pluck(f0, sec, decay=0.996):
    """Karplus-Strong plucked string."""
    n = int(sec * SR)
    period = int(SR / f0)
    buf = rng.uniform(-1, 1, period)
    out = np.empty(n)
    for i in range(n):
        out[i] = buf[i % period]
        buf[i % period] = decay * 0.5 * (buf[i % period] + buf[(i + 1) % period])
    return out


def bow():
    sec = 0.5
    n = int(sec * SR)
    string = pluck(98, sec, 0.994) * env(n, 0.001, 0.12)
    release = whoosh(sec, 900, 4000, 0.15) * 0.5
    return fade(string + release)


def arrow_hit():
    sec = 0.3
    n = int(sec * SR)
    thunk = tone([(np.linspace(260, 170, n), 1.0), (520, 0.25)], sec) * env(n, 0.001, 0.035)
    wobble = tone([(np.full(n, 140) * (1 + 0.05 * np.sin(2 * np.pi * 30 * t_of(sec))), 0.4)], sec) * env(n, 0.01, 0.08)
    return fade(thunk + wobble + band(noise(sec), 1500, 6000) * env(n, 0.0003, 0.006))


def firebolt():
    sec = 0.6
    n = int(sec * SR)
    t = t_of(sec)
    roar = band(noise(sec), 200, 1500) * np.clip(t / 0.08, 0, 1) * np.exp(-t / 0.25)
    crackle = np.zeros(n)
    for _ in range(18):
        i = rng.integers(0, n - 300)
        d = 0.01
        crackle[i:i + int(d * SR)] += band(noise(d), 2000, 7000) * env(int(d * SR), 0.0003, 0.003)
    rise = tone([(np.linspace(180, 420, n), 0.35)], sec) * np.exp(-t / 0.2)
    return fade(roar + crackle * 0.8 + rise)


def explode():
    sec = 1.2
    n = int(sec * SR)
    boom = lowpass(noise(sec), 260) * env(n, 0.004, 0.25) * 2.0
    crack = band(noise(sec), 800, 6000) * env(n, 0.001, 0.05)
    sizzle = band(noise(sec), 2500, 8000) * env(n, 0.05, 0.35) * 0.25
    return fade(boom + crack + sizzle)


def heal():
    sec = 1.4
    n = int(sec * SR)
    x = np.zeros(n)
    for k, f in enumerate([523.25, 659.25, 783.99, 1046.5]):
        i = int(k * 0.09 * SR)
        c = tone([(f, 1.0), (f * 2, 0.25), (f * 3.01, 0.1)], sec) * env(n, 0.005, 0.45)
        x[i:] += c[: n - i] * (1 - k * 0.12)
    shimmer = band(noise(sec), 4000, 9000) * env(n, 0.2, 0.4) * 0.08
    return fade(x + shimmer)


def vanish():
    sec = 0.7
    n = int(sec * SR)
    t = t_of(sec)
    puff = band(noise(sec), 300, 2500) * np.clip(t / 0.05, 0, 1) * np.exp(-t / 0.18)
    swirl = band(noise(sec), 1500, 5000) * (0.5 + 0.5 * np.sin(2 * np.pi * 9 * t)) * np.exp(-t / 0.3) * 0.4
    fall = tone([(np.linspace(900, 300, n), 0.25)], sec) * np.exp(-t / 0.2)
    return fade(puff + swirl + fall)


def whirlwind():
    sec = 0.75
    a = whoosh(0.4, 300, 2200, 0.5)
    b = whoosh(0.4, 400, 2600, 0.5)
    x = np.zeros(int(sec * SR))
    x[: len(a)] += a
    i = int(0.3 * SR)
    x[i:i + len(b)] += b[: len(x) - i] * 0.9
    return fade(x)


SOUNDS = {
    # loops
    "amb_wind": wind, "amb_rain": rain, "amb_birds": birds, "amb_crickets": crickets,
    "amb_waterfall": waterfall, "amb_fire": fire, "amb_lava": lava,
    # one-shots
    "swing_light": swing_light, "swing_heavy": swing_heavy, "dodge": dodge,
    "hit_flesh": hit_flesh, "chop": chop, "mine": mine, "rustle": rustle,
    "block": block, "parry": parry, "step": step,
    "wolf_howl": wolf_howl, "wolf_snarl": wolf_snarl, "wolf_yelp": wolf_yelp,
    "eat": eat, "pickup": pickup, "build": build, "craft": craft, "water": water,
    "chest": chest, "ui_click": ui_click,
    # class abilities
    "bow": bow, "arrow_hit": arrow_hit, "firebolt": firebolt, "explode": explode,
    "heal": heal, "vanish": vanish, "whirlwind": whirlwind,
}

if __name__ == "__main__":
    print("Writing", os.path.abspath(OUT))
    for name, fn in SOUNDS.items():
        save(name, fn(), 0.7 if name.startswith("amb_") else 0.85)
