# TrackView effects and Auto Tune

## Menu and dialog convention — also for future effects

The waveform PictureBox context menu contains:

```text
Apply Effects ...
  Auto Tune ...
```

New effect entries belong in this submenu. Use `EffectMenuFactory.Create`:
an effect that opens a parameter window must end with **` ...`** (space plus
three dots); an immediate effect without parameters must not. Parameter forms
may be named `...Dialog`, but open them with **`Show(owner)`**, not `ShowDialog`.
They must remain modeless and close/cancel their work when their TrackView closes.
The existing normalize/fade/trim commands are legacy commands and are not moved
or redesigned as part of this first effect.

Keep DSP, parameter/preset persistence, preview playback, and TrackView wiring
separate. Reuse the edit-session and menu infrastructure rather than placing
processing loops or file-store code in the TrackView event handler.

## Undo and redo are mandatory

**Applying an effect must be undoable and redoable with Ctrl+Z / Ctrl+Y.**

- Capture input with `AudioEffectEditSession` when opening the modeless window.
  With an active selection, only complete interleaved frames in that range are
  processed; without a selection, the entire track is processed.
- The scope and audio snapshot are frozen when the window opens and shown in
  the dialog. Changing the owner's selection does not silently retarget an
  already-open effect window. Reopen it to choose another scope.
- Preview, rendering, preset selection, parameter changes, cancellation, and
  closing without accepting must **not** mutate audio or create undo entries.
- On acceptance, stop the TrackView's playback while preserving its manual
  playback-rate setting. Revalidate the source and commit through the session.
- `AudioEffectEditSession.Apply` checks source-array identity, format, a full
  content hash, and the captured region. It rejects stale/in-place edits instead
  of overwriting newer work while a modeless window was open.
- A successful non-no-op commit creates **exactly one** existing AudioObj undo
  snapshot, invalidates redo, and replaces the samples once. The existing
  TrackView Ctrl+Z / Ctrl+Y commands restore/reapply the complete edit. A no-op
  does not add a redundant history step.
- Same-length effects must keep duration, sample format, metadata, and samples
  outside the selected range unchanged. Refresh the waveform after committing.
- TrackView remains an editing clone: the normal **Apply / Ctrl+S** operation
  commits it back to its source collection (and therefore to co-op if shared).

For future duration-changing effects, define selection/loop/position mapping
explicitly and test undo/redo of both samples and metadata before adding them.

## Auto Tune parameters

Auto Tune is an offline **monophonic** effect intended for a voice or a single
melodic instrument. It is not chord correction or source separation. All output
channels use the same tuning curve; Strongest-channel analysis avoids accidental
cancellation of antiphase stereo. Average/Left/Right analysis can also be chosen.

The tabs expose:

- Chromatic, major, natural/harmonic/melodic minor, pentatonic and blues scales,
  root key, or a custom set of absolute C–B pitch classes.
- Intensity, maximum correction in semitones, retune/release time, note-tracking
  smoothing, note-switch hysteresis, in-tune dead zone, vibrato preservation and
  its averaging window, concert A4 reference, and target fine tuning.
- Minimum note duration (`1/128`, `1/64`, `1/32`, ..., `1/2`, `1`, `2`, `3`, `4`)
  and tempo. **1 means one whole note / four beats**, so `1/4` is 500 ms at
  120 BPM. Shorter detected notes are left dry; their duration is not altered.
- Detection frequency bounds, voiced-confidence gate, RMS silence threshold,
  analysis step, synthesis FFT size and overlap.
- Wet/dry, output gain, voiced/unvoiced crossfade, selection-edge blending,
  optional spectral-envelope formant compensation and its bandwidth, and peak
  protection. Formant compensation is an approximation, not a vocal model.

Retune time 0 gives a harder, robotic response; longer times are gentler.
Corrections exceeding the maximum adjustment are bypassed instead of being
forced to an unrelated note. Intensity 0, wet 0, and maximum correction 0 bypass
pitch correction. Output gain is still honored when explicitly changed.

The implementation reuses the managed deterministic pYIN tracker for pitch and
voicing, with local period interpolation to refine its discrete pitch grid
without changing the decoded note/octave decision. It builds a gated, hysteretic
target-note curve and renders it with a
variable-ratio phase vocoder using normalized window overlap-add. Duration is
preserved. Above-Nyquist synthesis bins are discarded instead of folded back.
Large detection ranges/long tracks can hit pYIN's explicit memory limits; use a
selection or narrower frequency range rather than silently reducing quality.

## Prehear and cancellation

**Prehear** renders the captured region in memory and plays it through a float
sample provider. Original/bypass and looping are available for comparison.
Preview uses the source sample rate; TrackView's varispeed slider is not baked
into the effect. No temporary WAV, audio cache, export, or source-file rewrite is
created. Changing parameters stops preview, invalidates cached output, and
cancels a superseded render. Accept reuses output only when its parameters match.

**Max threads** is a separate per-run setting, not a sound/preset parameter. Each
new window defaults to `max(1, Environment.ProcessorCount / 2)` and limits the
NumericUpDown to `Environment.ProcessorCount`. Loading, saving, deleting, or
resetting a sound preset does not change it. The budget limits parallel pitch
analysis and independent synthesis channels; a single channel's phase history
is processed sequentially. Other application/UI/GC threads are not controlled
by this worker budget. No global MathNet or process-wide thread setting is changed.

CPU work is cancellable and off the UI thread; progress stays visible. Closing
either window cancels processing and stops/disposes the preview device. Hardware
preview failures are shown in the status area and do not modify audio/history.

## Presets

Presets are a UTF-8 JSON array at:

```text
%APPDATA%\ModularAudience\Effects\AutoTunePresets.json
```

Each element contains `Name` and the complete typed `Parameters` object. Selecting
a name in the combo box immediately loads its parameter controls. **Save**
creates a new named preset or updates that name case-insensitively. With a blank
name it chooses the first unused `Preset#N`, checking under the same cross-process
mutex as the write. Clear the name field to create another generic preset.
Execution worker limits and the preview-only loop/bypass switches are not sound
parameters and are not written into the preset.

**Delete** removes only the selected entry, clears the name/selection, and resets
all controls to the dialog's defaults (including the opening track's BPM when
available). It never modifies the audio or undo/redo stacks. Opening the preset
dropdown refreshes entries saved by another window or instance.

Writes use a same-directory temporary JSON file and atomic replacement. Invalid
files are reported instead of silently overwritten. Preset JSON is the only
effect metadata written to disk; sample processing and preview stay in RAM.

## Validation

```powershell
dotnet test ModularAudience.Audio.Tests\ModularAudience.Audio.Tests.csproj --filter "FullyQualifiedName~AutoTune|FullyQualifiedName~AudioEffectEditSession"
```

Tests cover actual pitch correction and intensity, duration and linked stereo,
bypass/gating, edge blending, peak/formant processing, settings validation,
selection isolation, stale-source rejection, undo/redo including TrackView's
Ctrl+Z / Ctrl+Y dispatch, modeless UI/preview/cancellation, parameter round trips,
and atomic preset persistence with concurrent generic-name allocation. Automated
UI tests use a fake audio device; subjective sound/device checks remain manual.
