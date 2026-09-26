#!/usr/bin/env python3
"""Create an original, quiet 80 BPM golf loop with soft keys and plucked strings."""
from pathlib import Path
import wave
import numpy as np

RATE, BEAT = 44100, 60 / 80
LENGTH = 32 * BEAT
mix = np.zeros((round(LENGTH * RATE), 2))


def note(beat, midi, gain, decay=1.1, pan=0):
    t = np.arange(round(4 * RATE)) / RATE
    hz = 440 * 2 ** ((midi - 69) / 12)
    tone = np.sin(2 * np.pi * hz * t)
    tone += .23 * np.sin(2 * np.pi * hz * 2 * t) * np.exp(-t * 7)
    tone += .08 * np.sin(2 * np.pi * hz * 3 * t) * np.exp(-t * 12)
    tone *= (1 - np.exp(-t * 160)) * np.exp(-t / decay) * gain
    tone *= np.minimum(1, (4 - t) * 10)
    stereo = tone[:, None] * np.sqrt(np.array([(1-pan)/2, (1+pan)/2]))
    for delay, level in [(0, 1), (.19, .13), (.37, .07)]:
        indices = (round((beat * BEAT + delay) * RATE) + np.arange(len(t))) % len(mix)
        np.add.at(mix, indices, stereo * level)


chords = [(53,60,64,69), (50,57,60,64), (55,62,65,69), (48,55,62,64)] * 2
for bar, chord in enumerate(chords):
    note(bar*4, chord[0]-12, .14, .9, -.1)
    note(bar*4+2.5, chord[0]-12, .08, .7, -.1)
    for step, index in enumerate([1,2,3,2,1,3]):
        note(bar*4+step*.5, chord[index], .095 if step%2==0 else .065, .75, -.25)
    for offset, pitch in zip([.5,2,3], [chord[3]+12,chord[2]+12,chord[1]+12]):
        note(bar*4+offset, pitch, .055, 1.15, .3)

mix = np.tanh(mix)
p = Path(__file__).resolve().parents[2] / 'unity/KinestheticUnity/Assets/Kinesthetic/Golf/Resources/GolfAudio/QuietFairway.wav'
p.parent.mkdir(parents=True, exist_ok=True)
with wave.open(str(p), 'wb') as out:
    out.setnchannels(2); out.setsampwidth(2); out.setframerate(RATE)
    out.writeframes((mix * 32767).astype('<i2').tobytes())
print(f'{LENGTH:.0f}s original loop, peak {abs(mix).max():.3f}, seam {abs(mix[0]-mix[-1]).max():.6f}')
