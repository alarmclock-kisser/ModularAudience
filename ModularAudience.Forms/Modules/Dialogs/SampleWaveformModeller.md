# Sample Waveform Modeller

Open from a collection's single-selection context menu, directly below
**Time-Stretch Selected**. This is a modeless window tied to that sample.

## Workflow

1. Select a time range by dragging the waveform or entering Start/End percentages.
2. Select an effect and **Add to selection**. Edit its parameters in the property grid.
   Each parameter has help text. Effects run top to bottom; reorder, disable or remove them.
3. The waveform updates after a short debounce using cancellable background rendering.
   Prehear (Space) auditions the selection; Loop repeats it; Hear original provides A/B.
4. **Save** replaces the original AudioObj in place and closes the window.
   **Ctrl-click Save** (or Ctrl+S) adds a uniquely named Modelled variant to the same
   collection and leaves the window open. Closing without Save discards the session.
5. **Ctrl+Z / Ctrl+Y** undo/redo effect edits, reordering, resets, drawings and preset loads.

## Visual editing

- Mouse wheel zooms around the pointer; scrollbar pans; Fit resets the view.
- Y scale controls vertical magnification. Zoom far enough to edit individual samples.
- Draw waveform edits the selected channel; Draw envelope creates a 0–2 gain curve.
- Drawings are effects in the chain, so they can be undone, reordered and saved in presets.
- The logarithmic spectrum is measured from the middle of the sample. Click it to add
  a parametric EQ band: horizontal position selects frequency, vertical position gain.
- Use range assigns the selection to the selected effect. Trim crops to that range.

## Effects

Gain, normalization, DC removal, polarity inversion, reverse, fades, trim, silence;
low/high/band-pass and notch filters; parametric EQ and low/high shelves;
compressor, limiter, gate; soft/hard clipping, wavefolding, rectification, bitcrushing;
tremolo, ring modulation, auto-pan, width, pan, channel swap, mono;
delay, ping-pong delay, multi-tap reverb, chorus, flanger, phaser;
varispeed, granular pitch shift and granular time stretch;
mathematical transfer waveshaping (x = input amplitude), waveform drawing and envelope drawing.

The Mix control blends wet/dry, except Trim, which is a crop. Time effects resize the
selected region; wet/dry blends the dry region mapped to the new duration. Delay and
reverb stay within the existing duration. Granular pitch/time processing can add texture.
Peak level is shown in dBFS; preview clamps to full scale while saved floating-point audio
retains headroom up to the processor's ±16 bound. Normalize/Limiter can control final level.

## Presets

Enter a name and Save preset. On another sample, open its own Waveform Modeller,
choose the preset in the ComboBox and Load. Edit the chain then Update preset, or
Delete preset. The ComboBox selects presets only; it never switches samples.

Stored as JSON in `%APPDATA%\ModularAudience\SampleModellerPresets`.
Presets contain processing parameters, relative regions and drawing points, not source PCM.
