#!/usr/bin/env python3
"""Render MiiHab's original 16-bar menu loop and UI sounds (requires numpy)."""
from pathlib import Path
import math
import wave
import numpy as np

RATE = 44100
BPM = 112
BEAT = 60 / BPM
LENGTH = 64 * BEAT
OUTPUT = Path(__file__).resolve().parents[2] / 'unity/KinestheticUnity/Assets/Kinesthetic/Menu/Audio'
RNG = np.random.default_rng(182)
mix = np.zeros((round(LENGTH * RATE), 2), dtype=np.float64)


def note(start, beats, midi, gain, voice='keys', pan=0):
    t = np.arange(round((beats * BEAT + .22) * RATE)) / RATE
    f = 440 * 2 ** ((midi - 69) / 12)
    attack = 1 - np.exp(-t * 180)
    release = np.exp(-np.maximum(0, t - beats * BEAT) * 32)
    if voice == 'bass':
        sound = np.sin(2 * np.pi * f * t) + .2 * np.sin(4 * np.pi * f * t) * np.exp(-t * 8)
        envelope = attack * np.exp(-t * 2.5) * release
    elif voice == 'bell':
        sound = np.sin(2 * np.pi * f * t + 1.1 * np.sin(2 * np.pi * f * 3 * t) * np.exp(-t * 12))
        envelope = attack * np.exp(-t * 5.8) * release
    else:
        sound = np.sin(2 * np.pi * f * t + .72 * np.sin(2 * np.pi * 2 * f * t) * np.exp(-t * 4.5))
        sound += .13 * np.sin(2 * np.pi * f * 2.004 * t) * np.exp(-t * 3)
        envelope = attack * np.exp(-t * 2) * release
    mono = sound * envelope * gain
    stereo = mono[:, None] * np.array([math.sqrt((1-pan)/2), math.sqrt((1+pan)/2)])
    add(start * BEAT, stereo)
    if voice != 'bass':
        add(start * BEAT + .13, stereo[:, ::-1] * .12)
        add(start * BEAT + .27, stereo * .07)


def add(seconds, stereo):
    indices = (round(seconds * RATE) + np.arange(len(stereo))) % len(mix)
    np.add.at(mix, indices, stereo)


def drum(start, kind, gain):
    duration = .20 if kind == 'kick' else .11
    t = np.arange(round(duration * RATE)) / RATE
    noise = RNG.normal(0, 1, len(t))
    if kind == 'kick':
        phase = 2 * np.pi * (52*t + 36*.025*(1-np.exp(-t/.025)))
        sound = np.sin(phase) * np.exp(-t*24) * (1-np.exp(-t*800))
    elif kind == 'snare':
        high = np.concatenate(([0.], np.diff(noise))) * .22
        sound = (high + np.sin(2*np.pi*185*t)*.28) * np.exp(-t*42)
    else:
        sound = np.concatenate(([0.], np.diff(noise))) * .14 * np.exp(-t*78)
    pan = .18 if kind == 'hat' else -.08
    add(start*BEAT, sound[:, None]*gain*np.array([1-pan, 1+pan]))


# Fmaj9 / Dm9 / Gm9 / C13, with a contrasting bridge and an original pentatonic melody.
chords = [([57,60,64,67],41),([57,60,64,69],38),([58,62,65,69],43),([58,62,64,69],36)]
melodies = [
 [(0,76,.6),(.75,79,.3),(1.5,81,.5),(2.5,79,.3),(3.25,76,.4)],
 [(.5,77,.4),(1.25,76,.4),(2,72,.8),(3.25,69,.35)],
 [(0,74,.45),(1,77,.5),(2,81,.45),(2.75,79,.3),(3.5,77,.25)],
 [(.25,76,.4),(1.25,74,.45),(2,72,1.0)],
]
for bar in range(16):
    chord, bass = chords[bar % 4]
    if 8 <= bar < 12:
        chord = [n + (12 if n == chord[0] else 0) for n in chord]
    for beat, duration, volume in [(0,.65,.074),(1.5,.45,.061),(2.75,.55,.066)]:
        for j, midi in enumerate(chord):
            note(bar*4+beat+j*.012, duration, midi, volume, pan=-.22+j*.12)
    for beat, midi, duration in [(0,bass,.85),(1.5,bass+12,.32),(2.5,bass+7,.45),(3.5,bass+12,.32)]:
        note(bar*4+beat,duration,midi,.19,'bass')
    for beat, midi, duration in melodies[bar%4]:
        if 8 <= bar < 12:
            midi = midi - 12
        note(bar*4+beat, duration, midi, .13 if bar < 8 else .11, 'bell', .16)
    drum(bar*4, 'kick', .18)
    drum(bar*4+2, 'kick', .14)
    for beat in [1,3]: drum(bar*4+beat,'snare',.12)
    for beat in np.arange(0,4,.5): drum(bar*4+beat,'hat',.09 if beat%1 else .065)

# Circular echo and tails preserve the exact musical loop boundary.
mix = np.tanh(mix * 1.2)
mix *= .72 / max(.72, float(np.abs(mix).max()))


def write_wav(path, data):
    data = np.clip(data, -1, 1)
    with wave.open(str(path), 'wb') as out:
        out.setnchannels(2); out.setsampwidth(2); out.setframerate(RATE)
        out.writeframes((data * 32767).astype('<i2').tobytes())


def blip(notes, duration):
    t = np.arange(round(duration*RATE))/RATE
    result = np.zeros_like(t)
    for offset, midi, gain in notes:
        u=np.maximum(0,t-offset)
        result += (t>=offset)*np.sin(2*np.pi*(440*2**((midi-69)/12))*u)*(1-np.exp(-u*600))*np.exp(-u*24)*gain
    return np.column_stack((result,result))


OUTPUT.mkdir(parents=True,exist_ok=True)
write_wav(OUTPUT/'MorningPlay.wav',mix)
write_wav(OUTPUT/'Hover.wav',blip([(0,81,.12)],.14))
write_wav(OUTPUT/'Select.wav',blip([(0,76,.16),(.055,83,.14),(.11,88,.11)],.36))
write_wav(OUTPUT/'Back.wav',blip([(0,79,.12),(.065,72,.10)],.27))
print(f'Original menu loop: {LENGTH:.3f}s, {BPM} BPM, peak {np.abs(mix).max():.3f}; wrote {OUTPUT}')
