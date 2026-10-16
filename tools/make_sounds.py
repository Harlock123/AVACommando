#!/usr/bin/env python3
"""Synthesises AVACommando's sound effects and tunes into ../src/AVACommando/Sounds as 16-bit mono WAV.

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
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVACommando", "Sounds")
RNG = random.Random(1985)


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




def drums(bpm, bars):
    """A marching snare: a roll on the first beat of each bar, single taps on the rest, and a bass drum under it."""
    beat = 60.0 / bpm
    out = []
    for _ in range(bars):
        for b in range(4):
            if b == 0:
                hit = cat(*[tone(beat / 4, 1800, 900, "noise", attack=0.001, release=1.5, gain=0.5) for _ in range(4)])
            else:
                hit = cat(tone(beat / 2, 2000, 700, "noise", attack=0.001, release=2.5, gain=0.6), silence(beat / 2))
            kick = cat(tone(beat / 2, 110, 45, "sine", attack=0.002, release=1.5, gain=0.9), silence(beat / 2)) if b % 2 == 0 else silence(beat)
            out.extend(mix(hit, kick)[: int(RATE * beat)])
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The player's rifle: a short crack. The enemy's: a duller pop.
    write("Fire.wav", mix(tone(0.08, 3000, 900, "noise", attack=0.001, release=1.2), tone(0.06, 400, 150, "square", release=1, gain=0.5)), 0.24)
    write("EnemyFire.wav", mix(tone(0.1, 1600, 500, "noise", attack=0.001, release=1.0), tone(0.08, 260, 120, "square", release=1, gain=0.4)), 0.22)
    # A grenade thrown: a whoosh; and going off.
    write("Throw.wav", tone(0.3, 500, 1500, "noise", attack=0.05, release=0.8), 0.18)
    write("Blast.wav", mix(tone(0.7, 900, 40, "noise", attack=0.002, release=0.35), tone(0.6, 120, 35, "sine", release=0.4, gain=0.9)), 0.5)
    write("EnemyDown.wav", cat(tone(0.05, 900, 600, "square", release=0.6), tone(0.12, 500, 200, "tri", release=0.6)), 0.26)
    write("BunkerDown.wav", cat(mix(tone(1.0, 700, 30, "noise", release=0.3), tone(0.9, 90, 30, "saw", release=0.3, gain=0.6)), tone(0.5, 300, 100, "noise", release=0.8, gain=0.4)), 0.55)
    write("Freed.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.3) for n in ["C5", "E5", "G5", "C6", "G5", "C6"]]), 0.34)
    write("Pickup.wav", cat(tone(0.06, hz("A5"), kind="pulse", release=0.3), tone(0.12, hz("E6"), kind="pulse", release=0.4)), 0.3)
    # The bugles: the call at the gate, a new area, and an area taken.
    write("Gate.wav", cat(*[tone(d, hz(n), kind="pulse", release=0.2) for n, d in [("G4", 0.12), ("G4", 0.12), ("G4", 0.12), ("C5", 0.4), ("G4", 0.12), ("C5", 0.5)]]), 0.36)
    write("AreaStart.wav", cat(*[tone(d, hz(n), kind="pulse", release=0.3) for n, d in [("C5", 0.1), ("E5", 0.1), ("G5", 0.1), ("C6", 0.3)]]), 0.3)
    write("AreaClear.wav", tune(150, [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 1), ("G5", 0.5), ("C6", 2)], [("C3", 1), ("G2", 1), ("C3", 1), ("G2", 0.5), ("C3", 1.5)], kind="pulse"), 0.42)
    write("Death.wav", cat(mix(tone(0.5, 1200, 60, "noise", release=0.4), tone(0.4, 500, 80, "saw", release=0.4, gain=0.5)), *[tone(0.14, hz(n), kind="tri", release=0.4) for n in ["E5", "C5", "A4", "E4"]]), 0.46)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["C6", "G6", "C6", "G6", "C7"]]), 0.38)

    # The march while the fighting's on: four bars of drums with a bugle-ish line over a bass (an original
    # composition), exactly a whole number of beats long, so it loops cleanly.
    lead = [("G4", 1), ("C5", 0.5), ("C5", 0.5), ("E5", 1), ("C5", 1),
            ("G4", 1), ("C5", 0.5), ("E5", 0.5), ("G5", 2),
            ("A5", 1), ("G5", 0.5), ("E5", 0.5), ("F5", 1), ("D5", 1),
            ("E5", 1), ("D5", 0.5), ("B4", 0.5), ("C5", 2)]
    bass = [("C3", 1), ("G2", 1)] * 4 + [("F2", 1), ("C3", 1)] * 2 + [("G2", 1), ("G2", 1), ("C3", 1), ("G2", 1)]
    melody = tune(120, lead, bass, kind="pulse")
    write("Music.wav", mix([v * 0.6 for v in melody], drums(120, 4)), 0.3)

    # The title: the same march, fuller, with a second strain.
    title_lead = lead + [("E5", 1), ("E5", 0.5), ("F5", 0.5), ("G5", 1), ("E5", 1), ("D5", 1), ("C5", 0.5), ("D5", 0.5), ("E5", 2),
                         ("C5", 1), ("E5", 0.5), ("G5", 0.5), ("C6", 1), ("B5", 1), ("A5", 0.5), ("G5", 0.5), ("F5", 0.5), ("D5", 0.5), ("C5", 2)]
    title_bass = bass + [("C3", 1), ("G2", 1)] * 2 + [("G2", 1), ("D3", 1)] * 2 + [("A2", 1), ("E2", 1), ("F2", 1), ("C3", 1), ("G2", 1), ("G2", 1), ("C3", 2)]
    write("TitleTune.wav", cat(mix(tune(126, title_lead, title_bass, kind="pulse"), drums(126, 8)), silence(1.0)), 0.42)
    write("StartTune.wav", tune(150, [("G4", 0.5), ("C5", 0.5), ("E5", 0.5), ("G5", 1), ("E5", 0.5), ("G5", 1)], [("C3", 1), ("G2", 1), ("C3", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(66, [("G4", 1), ("E4", 1), ("D4", 1), ("B3", 1), ("C4", 4)], [("C3", 2), ("A2", 2), ("G2", 2), ("C2", 2)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()
