#!/usr/bin/env python3
"""Synthesises AVARampage's sound effects and tunes into ../src/AVARampage/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVARampage", "Sounds")
RNG = random.Random(1986)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # Fists, glass and masonry.
    write("Punch.wav", mix(tone(0.09, 180, 70, "square", release=1.0), tone(0.07, 1200, 300, "noise", release=1.2, gain=0.6)), 0.4)
    write("Smash.wav", mix(tone(0.3, 900, 120, "noise", release=0.7), tone(0.2, 140, 60, "square", release=0.8, gain=0.6)), 0.5)
    write("Glass.wav", mix(tone(0.35, 5200, 2600, "noise", release=0.9), *[tone(0.3, f, kind="tri", release=1.2, gain=0.3) for f in [2900, 3700, 4400]]), 0.35)
    write("Crumble.wav", cat(*[mix(tone(0.12, 260 - k * 15, 60, "noise", release=0.6), tone(0.1, 90 - k * 4, 40, "square", release=0.8, gain=0.4)) for k in range(14)],
                             tone(0.9, 200, 25, "noise", release=0.5)), 0.7)
    # Eating.
    write("Eat.wav", cat(*[mix(tone(0.07, 400, 150, "noise", release=0.8), tone(0.06, 220, 120, "square", release=1.0, gain=0.4)) + silence(0.04) for _ in range(3)]), 0.45)
    write("Burp.wav", tone(0.55, 95, 70, "saw", attack=0.02, release=0.6, vibrato=0.12, vib_rate=28), 0.5)
    write("Roar.wav", mix(tone(1.0, 160, 90, "saw", attack=0.05, release=0.5, vibrato=0.08, vib_rate=18), tone(1.0, 700, 300, "noise", attack=0.05, release=0.5, gain=0.5)), 0.6)
    # The army.
    write("Shot.wav", mix(tone(0.06, 1800, 400, "noise", release=1.2), tone(0.04, 300, 150, "square", release=1.0, gain=0.3)), 0.2)
    write("Cannon.wav", mix(tone(0.5, 600, 60, "noise", release=0.6), tone(0.35, 110, 40, "square", release=0.7, gain=0.6)), 0.55)
    write("Explode.wav", mix(tone(0.9, 300, 30, "noise", release=0.5), tone(0.7, 110, 30, "square", release=0.6, gain=0.6)), 0.6)
    write("Hurt.wav", tone(0.25, 520, 200, "square", release=0.6, vibrato=0.1, vib_rate=30), 0.35)
    write("Shock.wav", cat(*[mix(tone(0.05, 3000, 2000, "noise", release=0.3), tone(0.05, 60, 60, "square", release=0.2, gain=0.5)) for _ in range(10)]), 0.4)
    write("Coin.wav", cat(tone(0.07, hz("B5"), kind="square", release=0.5), tone(0.25, hz("E6"), kind="square", release=0.8)), 0.35)
    write("Jump.wav", tone(0.2, 180, 520, "square", release=0.5), 0.3)
    write("Land.wav", mix(tone(0.15, 120, 50, "square", release=0.9), tone(0.15, 400, 120, "noise", release=0.9, gain=0.6)), 0.4)
    write("Shrink.wav", cat(tone(0.7, 900, 150, "square", release=0.4, vibrato=0.06, vib_rate=14), tone(0.2, 150, 120, "tri", release=0.8)), 0.4)
    write("Copter.wav", cat(*[mix(tone(0.07, 120, 90, "noise", release=0.9), tone(0.07, 80, 70, "square", release=1.0, gain=0.4)) for _ in range(12)]), 0.35)
    write("Siren.wav", cat(*[tone(0.35, a, b, "tri", release=0.05) for a, b in [(700, 1000), (1000, 700)] * 2]), 0.3)
    write("Select.wav", tone(0.08, hz("A5"), kind="pulse", release=0.6), 0.3)
    write("DayClear.wav", cat(*[tone(0.12, hz(n), kind="pulse", release=0.4) for n in ["C5", "E5", "G5", "C6"]], tone(0.6, hz("C6"), kind="pulse", release=0.6, vibrato=0.01)), 0.45)

    # Rampaging: a stomping boogie in A over a walking bass, with tom drums (new). Loops.
    walk = [("A2", 0.5), ("C#3", 0.5), ("E3", 0.5), ("F#3", 0.5), ("G3", 0.5), ("F#3", 0.5), ("E3", 0.5), ("C#3", 0.5)]
    walk_d = [(n.replace("A2", "D3").replace("C#3", "F#3").replace("E3", "A3").replace("F#3", "B3").replace("G3", "C4"), d) for n, d in walk]
    walk_e = [(n.replace("A2", "E3").replace("C#3", "G#3").replace("E3", "B3").replace("F#3", "C#4").replace("G3", "D4"), d) for n, d in walk]
    bars = walk * 2 + walk_d * 2 + walk * 2 + walk_e + walk_d + walk * 2
    lead = [("-", 4), ("E5", 0.5), ("E5", 0.5), ("C5", 0.5), ("A4", 1.5), ("-", 1), ("G4", 0.5), ("A4", 0.5), ("C5", 1), ("E5", 1), ("D5", 2), ("-", 2),
            ("A5", 0.5), ("A5", 0.5), ("F#5", 0.5), ("D5", 1.5), ("-", 1), ("C5", 0.5), ("D5", 0.5), ("F#5", 1), ("A5", 1), ("G5", 2), ("-", 2),
            ("E5", 0.5), ("E5", 0.5), ("C5", 0.5), ("A4", 1.5), ("-", 2), ("B4", 1), ("D5", 1), ("E5", 2), ("D5", 1), ("C5", 1), ("A4", 4)]
    bass = seq(150, bars, kind="square", gain=0.35, release=0.5)
    top = seq(150, lead, kind="pulse", gain=0.25, release=0.5)
    beat = 60.0 / 150
    drums = cat(*[mix(tone(0.12, 140, 50, "sine", release=0.8, gain=0.9), silence(beat)) for _ in range(int(len(bass) / RATE / beat))])
    write("Music.wav", mix(bass, top, drums), 0.42)

    write("TitleTune.wav", tune(132,
                                [("A4", 0.5), ("C5", 0.5), ("E5", 1), ("D5", 0.5), ("C5", 0.5), ("A4", 1), ("G4", 0.5), ("A4", 0.5), ("C5", 1), ("E5", 2),
                                 ("F5", 0.5), ("E5", 0.5), ("D5", 1), ("C5", 0.5), ("D5", 0.5), ("E5", 1), ("A4", 4)],
                                [("A2", 2), ("A2", 2), ("F2", 2), ("G2", 2), ("D2", 2), ("E2", 2), ("A2", 4)], kind="pulse"), 0.45)
    write("StartTune.wav", tune(160, [("E4", 0.5), ("A4", 0.5), ("C5", 0.5), ("E5", 1.5), ("D5", 0.5), ("E5", 2)], [("A2", 2), ("E2", 1), ("A2", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(84, [("E5", 1), ("D5", 1), ("C5", 1), ("B4", 1), ("A4", 3)], [("A3", 2), ("F3", 2), ("E3", 1), ("A2", 3)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()
