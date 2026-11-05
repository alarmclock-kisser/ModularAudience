# Waveform Modeller

Open via **+ Track → right-click → Model Waveform**. This is a sibling of Browse YT,
not an item nested inside the YouTube browser menu.

## Equations and drawing

The plot represents one periodic waveform: `x` (or `phase`) ranges from 0 to 1.
`Frequency Hz` controls how often that cycle repeats. Example equations:

```text
sin(tau*x)
0.7sin(2*pi*x) + 0.2sin(3*tau*x)
tanh(3*sin(tau*x))
2*x-1
sin(tau*x + 1.5*sin(3*tau*x))
```

Supported: `+ - * / % ^`, parentheses, scientific notation, unary signs, implicit
multiplication (`2pi`, `2(x+1)`, `2sin(...)`), `pi`, `tau`, `e`, and Unicode `π τ × · ÷ −`.
Functions: `sin cos tan tanh asin acos atan abs sqrt exp log ln log10 floor ceil round
sign min max pow clamp`. Arguments use commas; decimal numbers use a dot. The parser
only accepts this grammar and does not execute scripts or access application objects.
Power is right associative; `-2^2` means `-(2^2)`.

Draw with the left mouse button in the 2D plot. The edited cycle is approximated by
an editable Fourier equation, using **Draw harmonics** (1–128). This is a periodic
Fourier approximation, not an exact symbolic simplification of arbitrary strokes.
**Smooth drawing**, **Invert**, **Reverse**, and **Clear** update the equation as well.
If harmonic count is changed after drawing, the Fourier equation is regenerated.
The audio and the settled plot use the same normalized/DC-corrected equation.

## Live audio

**Prehear** plays continuously while the equation/shape/frequency/level/phase changes.
Expressions compile into a wavetable outside the audio callback. Drawing updates are
throttled, equation edits are debounced, frequency/gain/phase changes are smoothed,
and waveform changes crossfade over 10 ms without restarting the oscillator phase.
Invalid expressions leave the last valid sound active and display an error in the UI.
The live player uses 44.1 kHz stereo and does not use a device during ordinary editing.
High harmonics beyond the current frequency's Nyquist limit are removed by FFT filtering.

## Render and presets

**Render → AudioObj** uses the current model snapshot, configurable duration (0.01–3600 s),
sample rate (22.05/44.1/48/96 kHz), 1–8 duplicated channels, phase, gain, attack and release.
Results are added to **Modelled Waveforms** and can be exported through the existing
AudioCollectionView. **Cancel render** aborts generation; live editing can continue.
The selected channel count duplicates the designed mono waveform; it is not separate
per-channel waveform modelling. RAM audio limits still apply for very long/high-rate renders.

**Save waveform / Load waveform** store JSON equations, drawing and all numeric settings
under `%APPDATA%\ModularAudience\Waveforms` by default. No audio-device handles or
compiled delegates are serialized. Undo/Redo buttons (or Ctrl+Z/Y outside text fields)
restore complete valid model states; equation/name fields retain normal text editing.
Closing releases the live device, stops UI timers and cancels an outstanding render.
