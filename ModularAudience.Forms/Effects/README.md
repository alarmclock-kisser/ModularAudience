# TrackView effects

## Menu and dialog convention — also for future effects

The waveform PictureBox context menu contains:

```text
Apply Effect ...
  Pitch & Tuning       → Auto Tune ..., Pitch Shift ...
  Time & Speed         → Time Stretch ..., Varispeed Shift ...
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

There are **37 effects** in the shared catalog. `EffectOptionsEditor` provides the shared cancellable render, cached RAM preview,
bypass/loop comparison and single-commit lifecycle. Each window is modeless and
owned by its editing window. `PitchShiftDialog` uses this editor in effect mode.
**TimeStretchEffectDialog is a new, separate Apply Effect window**; the existing
`TimeStretchDialog` and its older batch/configure-only workflows are unchanged.
`VarispeedShiftDialog` is the tape-style counterpart. Both offer linked source/target
BPM helpers; presets save their duration factor/speed, not live source metadata.

Pitch Shift explicitly selects the existing windowed-sinc resampler (duration
changes by `2^(-semitones/12)`) or its existing phase-locked FFT-PV compensation
(exact original frame count). Cancellation and a worker limit reach both paths.
The effect processor then matches the shifted output's peak to the source peak with
one shared gain, preserving the source level and stereo balance without letting the
output peak exceed the input peak.
The vocoder streams successive spectra rather than storing every analysis window,
so working memory does not grow with window count. Time Stretch offers that same
continuous phase-locked vocoder or isolated-per-channel legacy V1 chunk processing.
Legacy V1 cancellation is checked around its non-cancellable processing pass.

Varispeed Shift appears directly below Time Stretch in **Time & Speed**. It uses
anti-aliased windowed-sinc resampling: speed 2 means twice the BPM, one octave up,
and half the duration; speed 0.5 does the opposite. Unlike Time Stretch, it does
not compensate pitch. A speed curve and an independent output-gain curve are available.

## Independent parameter curves

**35 of the 37 effects** support automation, including typed Auto Tune. Every
supported continuous option has its own **Edit Curve ...** button, **Curve** toggle
and equation/checkpoint summary beside it. Several options can be automated together.
Opening or cancelling the child does not change the effect. **OK / Apply Curve**
installs its edited copy; the toggle disables it without discarding the shape/range.
Parameter/curve/timing changes cancel superseded work, stop prehear and invalidate
the RAM render cache. Prehear and Apply use exactly the same settings snapshot.

The modeless curve editor offers:

- An equation text box with live validation and a PictureBox plot; or an editable
  checkpoint table with **Add checkpoint**, row deletion, and Linear / Smooth /
  Hold interpolation. Use 2–512 strictly ordered points, with first x=0 and last x=1.
- **Start value**, **End value**, and **End follows current parameter**. The latter
  is on by default, so changing a knob/Target BPM also updates the curve's target.
  Positive parameter ranges can use logarithmic value interpolation instead of linear.
  These are the values at **y=0 / y=1**, not necessarily at the first/last time sample:
  oscillating or asymptotic shapes need not start at y=0 or finish at y=1.
- Normalized source time `x` (alias `t`), from 0 to 1. The finite shape `y` is clamped
  to 0–1, then mapped to the parameter range. This bounds oscillators/overshoot to
  the configured endpoints; reversed endpoints give falling sweeps.
- Constants `pi`, `e`; editable `f` (cycles), `p` (phase in cycles), and `a`, `b`,
  `c`, `d` (shape parameters). Operators `+ - * / % ^`, parentheses and a bounded
  arithmetic/function parser, **not executable scripting**. Decimal points are
  required in equations/checkpoints; numeric controls follow the system number format.
- `sin`, `cos`, `tan`, inverse/hyperbolic trig, `abs`, `sqrt`, `exp`, `log`/`ln`,
  `log10`, `floor`, `ceil`, `round`, `sign`, `min`, `max`, `pow`, `clamp`,
  `smoothstep(x)`, and `step(edge,x)`. Trig uses radians. Unknown code, excessive
  nesting/complexity, NaN/infinity, poles and invalid roots/logs are rejected before
  acceptance/saving; render-time evaluations are checked too.

The **17 immutable built-ins** include linear rise/fall, logarithmic, exponential,
power/ease in/out, smooth S-curve, sine/cosine, pole-free tangent, safe reciprocal
decay (`1/(1+a*x)`, not singular `1/x` at x=0), triangle, sawtooth, square,
decaying sine pulses, bell/swell and constant full amount. Oscillator examples:

```text
0.5 + 0.5*sin(2*pi*(f*x+p))
exp(-a*x) * (0.5 + 0.5*cos(2*pi*(f*x+p)))
```

**Effect amount (%)** is an additional original/effected crossfade for effects
whose sample timelines align. Individual parameter curves are actual DSP control,
not repeated short independent effect renders: filter/delay/dynamics/LFO states
are retained. Modulated LFO rates integrate phase instead of resetting it. Wet/dry
and output gain can have their own curves. Wet/dry or effect-amount curves that are
zero throughout the render bypass without inserting silent tails or redundant undo
steps. Output gain still follows the existing wet-zero gain behavior. Normalize
automates its global-peak-based gain, not a repeatedly re-analyzed local peak.
Custom fade gain replaces the built-in
fade shape/length and follows the selected curve clock.

### Pitch, Time Stretch and Varispeed curves

- Pitch starts at **0 semitones** and follows the configured semitone target by
  default. FFT-PV renders a real variable pitch shift at the original frame count;
  Resampling changes pitch and duration together. An optional output-gain curve
  follows the corresponding source-time map.
- Time Stretch starts at **1× source speed** and approaches Target BPM. Its curve
  interpolates **speed**, not duration factor: source 120 → target 180 BPM means
  speed 1→1.5; the factor at the midpoint is 1/1.25, not the midpoint of 1 and 2/3.
  A continuous, phase-locked variable-analysis-hop FFT-PV preserves pitch. Curve mode
  always uses that renderer, irrespective of the legacy constant-stretch Method.
  FFT size and requested window overlap remain quality controls; the effective overlap
  is increased when needed to keep analysis hops safe at the maximum configured speed.
- Varispeed also interpolates speed, but deliberately moves pitch with it. All
  channels use one integral time map. No crossfade between incompatible timelines
  is offered as a misleading substitute for pitch/speed modulation.
- Duration is the numerical integral of local duration factors in **source time**,
  rounded to complete frames. Reaching Target BPM at the end does not imply that
  the whole segment has that constant speed. For 1→1.5× linear speed, duration is
  approximately `originalDuration * log(1.5)/0.5`.
- Variable speed cannot be expressed as one uniform BPM multiplier. Such renders
  retain their base/grid tempo reference, rather than tagging the entire audio with
  the endpoint BPM. Constant Varispeed does provide its true uniform multiplier.
  The existing edit-session selection/ripple/navigation mapping is retained; this
  feature does not create a variable-tempo beat-grid map or a tempo-automation lane.

### Curve timing and multiple segments

**Curve timing** is shared by the enabled curves within one effect preset:

- **Restart curve per segment**, or **One curve across selection**.
- For a shared curve, optionally count the lengths of gaps between selected clips.
- Separately include/exclude sustained silence, with an adjustable dBFS threshold
  and minimum silence length. Single zero crossings do not pause the clock.
- TrackView's track/selection is one region. MixTrackView supplies the frozen selected
  clip intervals; overlapping clips on the same lane share a region, adjacent clips
  can restart separately, and an empty selected lane uses its selected time region.
  Simultaneous lanes share the **union of selected time**, not concatenated lane
  durations. Shared silence detection uses any audible selected lane, avoiding
  cancellation or one silent lane stopping the others' clock.
- Skipping gaps/silence changes **curve time only**; it does not delete samples,
  close gaps, reset DSP state, or silently change the captured editing scope. Appended
  reverb/delay tails hold the last curve value. DSP and the clock stay channel-linked.

### Shared shape presets and compatibility

The curve selector has **Load / Save / Rename / Delete / Defaults** and refreshes
other windows' changes without losing the current unsaved edit. Shape-only user
presets live at:

```text
%APPDATA%\ModularAudience\Effects\CurvePresets.json
```

**Only Save writes this library.** OK activates a curve without saving it. Selecting
a built-in clears the name field; saving a variant with a blank name allocates
`Curve#1`, `Curve#2`, etc. Built-ins are always code-defined, cannot be overwritten,
renamed or deleted, and remain usable if user JSON is malformed/locked. Same-named
user curves are separate. Writes are atomically replaced under a cross-process mutex;
invalid files are reported, never silently replaced. Rename never overwrites another
user curve. Range, target parameter, toggles and timing are **not** shape-preset fields,
so the same curve is usable for every compatible option/effect.

Effect presets **do** persist all their independent curve assignments/ranges and
timing. Old effect/Auto Tune JSON with no automation still loads unchanged; disabled
curves follow the original DSP paths. Existing numeric effect IDs are unchanged;
Varispeed Shift was appended as ID 36 and placed after Time Stretch only in menu order.
Mix bounce recipes retain curves and captured shared-clock geometry across `.mamix`
save/load and later tempo regeneration. Rendering/preview still write no audio files.

### Deliberately fixed options / excluded actions

- **Convert to mono** remains a discrete channel-layout conversion, not a fractional
  channel count; **Reverse** remains a discrete frame-order operation, not a continuous
  rate parameter. They remain immediate, undoable actions without curve dialogs.
- Algorithm/waveform/profile/scale choices and booleans are not interpolated.
  Structural values (tail length, FFT/window/overlap topology, voices/taps/harmonic
  counts, limiter look-ahead, noise-profile capture duration, built-in fade length)
  remain fixed for one render; they define buffers, analysis scope or topology rather
  than a smooth control signal. Bit depth is a useful exception and is rounded to
  actual quantizer steps. Other meaningful continuous controls are exposed per option.
- Auto Tune's detector frequency range, analysis hop/FFT topology, musical duration/
  tempo gate, and boundary-blend length are fixed analysis/boundary settings. Its
  intensity, correction limit, tracking/retune/release, tuning, confidence/silence gate,
  vibrato/formant and mix/output controls are individually automated.
- Automating hum fundamental requires **Custom** mains frequency; automating the
  spectral noise-floor level requires **Fixed spectral floor**. Incompatible modes
  report a validation error rather than accepting an inaudible, ignored curve.
- Abrupt equations/Hold/square shapes intentionally retain their discontinuities.
  Extreme or fast modulation can produce clicks/spectral artifacts; use smooth shapes,
  sensible ranges and Prehear. This is not a guarantee of artifact-free resynthesis.

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
dotnet test ModularAudience.Audio.Tests\ModularAudience.Audio.Tests.csproj --filter "FullyQualifiedName~AutoTune|FullyQualifiedName~AudioEffect|FullyQualifiedName~EffectCurve|FullyQualifiedName~CurvePresetAndDialog|FullyQualifiedName~EffectPresetAndDialog|FullyQualifiedName~MixEffect|FullyQualifiedName~StructuredImport|FullyQualifiedName~ApplicationPreferences"
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

Curve tests additionally cover every advertised scalar option's constant-curve DSP
equivalence (including typed Auto Tune), simultaneous periodic curves for every
supported effect, true sliding frequency/speed/duration, parser domains/precedence/
limits, checkpoint interpolation, source-time clocks/gaps/silence/overlaps/empty lanes,
shared-lane geometry, immutable factories/concurrent persistence, real preview painting,
curve toggles/cache invalidation, the separate Time Stretch / Varispeed windows,
TrackView/MixTrackView selection isolation and single-step undo/redo, and recipe/project/
tempo round trips. These are deterministic signal/UI tests, not subjective listening tests.
