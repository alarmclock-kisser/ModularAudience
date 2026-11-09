# TrackView effects

## Menu and dialog convention — also for future effects

The waveform PictureBox context menu contains:

```text
Apply Effect ...
  Pitch & Tuning       → Auto Tune ..., Pitch Shift ...
  Time & Speed         → Time Stretch ...
  Reverb & Space       → Room / Hall / Plate / Spring / Chamber / Shimmer reverb ...
  Delay & Echo         → Single / Slapback / Ping-Pong / Multi-Tap delay ...
  Dynamics             → Compressor ..., Limiter ..., Gate ..., Expander ..., De-Esser ...
  Distortion & Lo-Fi   → Bitcrusher ..., Sample Rate reducer ..., Overdrive ...,
                        Saturation ..., Tape saturation ...
  Modulation           → Chorus ..., Flanger ..., Phaser ..., Tremolo ...
  Stereo & Utility     → Gain ..., Normalize ..., Fade in ..., Fade out ...,
                        Stereo width ..., Convert to mono, Reverse
  Noise & Repair       → Hum removal ..., Noise reduction ...
```

New effect entries belong in this submenu. Use `EffectMenuFactory.Create`:
an effect that opens a parameter window must end with **` ...`** (space plus
three dots); an immediate effect without parameters must not. Parameter forms
may be named `...Dialog`, but open them with **`Show(owner)`**, not `ShowDialog`.
They must remain modeless and close/cancel their work when their TrackView closes.
The existing normalize/fade/trim shortcuts remain available as legacy commands.
`AudioEffectCatalog` and `EffectMenuFactory.CreateApplyMenu` define this hierarchy
once, including the distinction between parameterized and immediate actions.
MixTrackView's audio-selection/segment menu uses the same factory and dialogs;
its empty-space menu remains separate. See `Modules/MixTrackView.md` for lane-local
range/gap processing, non-destructive asset replacement and mix-level undo history.

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

Duration-changing effects **splice**, not overwrite: prefix + rendered region +
unchanged suffix. Following audio ripples by the exact frame delta. Selection,
navigation, playback position, loops and beat-grid positions are mapped piecewise
(before = fixed, inside = scaled, after = shifted), frame-aligned. History retains
samples, channel format, length/duration, selection, loop/navigation state, beat
grid and musical metadata. Whole-track time changes adapt known BPM tags; local
changes do not pretend that the entire track now has a different uniform tempo.

## Shared effect dialogs and DSP

All parameter effects use the same preset bar: selector (loads immediately),
**Load**, name field, **Save**, **Rename**, **Delete**, **Defaults**. Rename requires
a selected user preset and a new name, never overwrites another user preset, and
preserves unsaved sound edits. Factory entries cannot be renamed or deleted.
Dropdown refresh preserves unsaved parameters. Max threads and preview switches
are runtime controls, not preset sound parameters.

`EffectOptionsEditor` provides the shared cancellable render, cached RAM preview,
bypass/loop comparison and single-commit lifecycle. Each window is modeless and
owned by its editing window. `PitchShiftDialog` and the existing `TimeStretchDialog`
have effect-mode constructors using this editor; their older batch and configure-only
workflows remain available. Time Stretch also offers linked source/target BPM
helpers; only the duration factor, not live source metadata, belongs to its preset.

Pitch Shift explicitly selects the existing windowed-sinc resampler (duration
changes by `2^(-semitones/12)`) or its existing phase-locked FFT-PV compensation
(exact original frame count). Cancellation and a worker limit reach both paths.
The vocoder streams successive spectra rather than storing every analysis window,
so working memory does not grow with window count. Time Stretch offers that same
continuous phase-locked vocoder or isolated-per-channel legacy V1 chunk processing.
Legacy V1 cancellation is checked around its non-cancellable processing pass.

`AudioEffectProcessor` contains independent DSP routes:

- Six algorithmic reverbs: differently tuned orthogonal feedback-delay tanks,
  early reflections, diffusion/damping, plate modulation, dispersive spring
  all-passes, and real dual-read-head pitch-shift feedback for shimmer. These are
  algorithmic models, not measured impulse responses or hardware emulations.
- Filtered feedback delays, stereo-pair cross-feedback ping-pong and rhythmically
  spaced multi-taps. **Append tail (s)** explicitly sets the inserted tail length;
  zero crops to the original region. Wet zero bypasses without appending silence.
- Linked-channel compressor/gate/expander, offline look-ahead **sample-peak**
  limiter, and band-detected split-band/wideband de-esser. Not true-peak or LUFS tools.
- Four quantizers in Bitcrusher's combo (round, truncate, mu-law, noise-shaped),
  optional deterministic TPDF dither, sample/hold or linear rate reduction,
  oversampled clipping/saturation, and tape bias/memory/wow.
- Fractional-delay chorus/flanger, cascaded all-pass phaser and waveform-selectable
  amplitude tremolo. Stereo width processes pairs, leaving odd extra channels intact.
- Peak normalization (optionally per-channel / DC removal), gain, four fade curves,
  all-channel averaging and frame-wise reverse. Whole-track mono conversion yields
  one channel. A selected mono conversion duplicates the mono average on existing
  channels, because channel format cannot change in the middle of a track.
- Mains/harmonic notch filtering and linked STFT spectral noise subtraction.
  Noise profiles use quietest frames, a noise-only lead-in, or a fixed spectral
  floor. Stationary-noise reduction is not source separation; a contaminated noise
  profile can attenuate wanted sound. Frequency parameters clamp below Nyquist at
  render time, keeping presets portable between source rates.

No implicit clipping/normalization is applied to arbitrary effects: use the limiter
when a deliberate drive/feedback/gain combination needs a ceiling. Rendered arrays
are bounded to 512 MiB per effect; overly large results fail rather than truncate
the track. Processing and preview never export temporary audio files.

Each new parameterized effect has at least three distinct built-in profiles plus
persistent user presets at `%APPDATA%\ModularAudience\Effects\<EffectKind>Presets.json`.
They use complete schema-validated settings and stable factory IDs. Stores seed
missing built-ins without overwriting existing values, lock across processes,
write atomically, isolate effects and report malformed files without replacing them.
Auto Tune keeps its existing typed JSON format and factory catalog below.

## Import preferences

`%APPDATA%\ModularAudience\application-settings.json` persists `KeepStructure`
(default true) and `AllInOneBag` (default false) immediately on checkbox changes;
startup and cross-instance refresh restore them without writing them back.
Structured import collections remember a normalized **full source directory**,
independent of editable captions or later track file paths. Repeated imports append
to the matching live local collection (even if it was renamed or emptied); identical
folder names under different parents remain separate. Closed/remote collections
are not reused. Failed loads cannot shift later files into the wrong source folder.

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

Each element contains `Name` and the complete typed `Parameters` object. Default
entries additionally contain a stable `BuiltInId`, for example `"soft"` or
`"vocals-natural"`; user entries omit this property. Legacy arrays containing only
`Name` and `Parameters` remain compatible. Worker limits and the preview-only
loop/bypass switches are not sound parameters and are not written into the preset.

The selector always offers the catalog below, marked **`[Default]`** and ordered
before user presets. Missing default IDs are added to the existing JSON array
under the same cross-process mutex used for saving. Once all defaults are present,
reopening does not rewrite the file or duplicate entries. Seeding preserves
existing user parameters and even manually edited default parameters. A user
preset named `Soft` remains separate from `[Default] Soft`. If the file cannot
be read or seeded, the error is shown
and the code-defined defaults remain usable without replacing the original file.

Selecting an entry immediately loads its saved parameter values, then adapts
**Tempo BPM** to the current track metadata for both user and built-in presets:

1. `AudioObj.Bpm`
2. `AudioObj.ScannedBpm`
3. The selected preset's `TempoBpm`
4. 120 BPM

Each candidate must be finite and within the supported **20–400 BPM** range;
missing/invalid values fall through rather than being clamped. Metadata is read
again on every selection, even if it changed after the modeless window opened.
TrackView's editing clone retains `ScannedBpm` from its source AudioObj as well.
Only the parameter controls are adapted: preset JSON, catalog values, track
metadata, samples, undo/redo history, and Max threads are not modified by loading.
Tempo remains manually editable after loading; it is not forced again during
preview/apply or a dropdown refresh. Opening/resetting the dialog uses the same
tempo fallback with the dialog defaults as the preset starting point.

**Save** creates a user preset or updates an existing user name case-insensitively;
it never overwrites a default entry. Selecting a default clears the name field so
an edited variant does not accidentally overwrite a same-named user preset.
Enter a name to keep a variant, or leave it blank for the first unused `Preset#N`.
Generic names are allocated under the write mutex. Clear the name field to
create another generic preset.

**Delete** is disabled for defaults. For a selected user preset it removes only
that entry, clears the name/selection, and resets sound controls to the dialog's
opening defaults with current track-tempo fallback, but leaves Max
threads unchanged. Preset operations never modify audio or undo/redo stacks.
Opening the dropdown refreshes entries saved by another window or instance.

### Built-in starting points

| Preset | Intensity | Retune | Distinguishing parameters / use |
| --- | ---: | ---: | --- |
| Soft | 35% | 150 ms | 80% vibrato preservation, 8-cent dead zone, 85% wet; subtle stabilization. |
| Medium | 65% | 70 ms | 45% vibrato preservation, 4-cent dead zone; balanced correction. |
| Strong | 100% | 12 ms | 5% vibrato preservation, no dead zone, `1/128` minimum notes; firm correction. |
| Vocals - Natural Lead | 70% | 85 ms | 85% vibrato and 80% formant preservation, 4096 FFT; expressive solo vocals. |
| Vocals - Pop Tight | 100% | 22 ms | 30% vibrato and 85% formant preservation; tight modern lead vocals. |
| Vocals - Hard Robotic | 100% | 0 ms | No vibrato preservation/smoothing, `1/128` minimum notes, 1024 FFT; deliberate hard snapping. |
| Vocals - Low Voice | 80% | 65 ms | 50–500 Hz detection, 4096 FFT, 90% formant preservation. |
| Vocals - High Voice | 75% | 65 ms | 140–2200 Hz detection, 70% vibrato and 80% formant preservation. |
| Instrument - Bass | 90% | 50 ms | 30–400 Hz detection, 8192 FFT, 0.82 confidence gate; sustained monophonic bass. |
| Instrument - Guitar Lead | 65% | 65 ms | 70% vibrato preservation, 70–1500 Hz detection, gentle attack crossfade; single-note leads. |
| Instrument - Bowed / Wind | 60% | 110 ms | 95% vibrato preservation, `1/16` minimum notes, 4096 FFT; expressive solo instruments. |
| Ensemble - Unison Only | 25% | 200 ms | 45% wet, 0.5-semitone correction limit, 0.9 confidence gate; very cautious shared correction. |
| Creative - C Minor Snap | 100% | 0 ms | C natural minor, no vibrato or formant preservation, 1024 FFT; obvious scale snapping. |

These are starting points, not instrument recognition. All use chromatic tuning
except **Creative - C Minor Snap**. All store A4 = 440 Hz, 120 BPM, linked
Strongest-channel analysis, 0 dB output gain, and enabled peak protection. Loading
uses the track-tempo fallback above; key/scale and detection range are not
automatically adapted. Check these values for the actual recording, especially
when no usable track tempo is known. Descriptions appear in the status area and
selector tooltip. Max threads remains an independent runtime choice.

**Ensemble - Unison Only does not tune individual choir voices or chords.** It
only attempts a conservative shared curve for predominantly unison material;
compare against dry audio and bypass it when the detected pitch is unreliable.
Very low bass detection and larger FFTs cost more CPU/memory. Formant preservation
is approximate, and no preset guarantees artifact-free results for every source.

Writes use a same-directory temporary JSON file and atomic replacement. Invalid
files are reported instead of silently overwritten. Preset JSON is the only
effect metadata written to disk; sample processing and preview stay in RAM.

## Validation

```powershell
dotnet test ModularAudience.Audio.Tests\ModularAudience.Audio.Tests.csproj --filter "FullyQualifiedName~AutoTune|FullyQualifiedName~AudioEffect|FullyQualifiedName~EffectPresetAndDialog|FullyQualifiedName~MixEffect|FullyQualifiedName~StructuredImport|FullyQualifiedName~ApplicationPreferences"
```

Tests cover actual pitch correction and intensity, duration and linked stereo,
bypass/gating, edge blending, peak/formant processing, settings validation,
selection isolation, stale-source rejection, undo/redo including TrackView's
Ctrl+Z / Ctrl+Y dispatch, modeless UI/preview/cancellation, parameter round trips,
and atomic preset persistence with concurrent generic-name allocation. Catalog
tests cover distinct/valid profiles, legacy migration, idempotent/concurrent
default seeding, same-name user/default isolation, restoration of missing defaults,
preservation of existing values and unsaved controls on dropdown refresh, protected
UI defaults, malformed/locked-file fallback, and live track/preset BPM priority
including invalid-value fallbacks and unchanged stored parameters.
Automated UI tests use a fake audio device; subjective sound/device checks remain
manual.
