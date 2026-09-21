# Task for Qwen 3.8 28B: BeatClicker procedural slider and pattern grammar

## Role

You are working as the local implementation agent in the ModularAudience repository. Implement and validate the BeatClicker Classic generation upgrade described below. Work with the existing code and the existing uncommitted changes. Do not reset, revert, commit, or push anything.

The goal is authored-feeling procedural variety. Do not replace the current generator with a collection of 64 hardcoded coordinate templates. The 64 patterns are logical combinations generated from a small number of independent axes, with continuous random perturbation and controlled remixes.

## Primary outcome

Classic BeatClicker should generate sliders and short multi-object phrases that feel varied across runs:

- curved and wave-like slider paths;
- partial ellipses and S-curves;
- rare looping or curling paths, especially on Hard/Hell but never as the dominant shape;
- independent slider length and duration choices;
- fast/long, fast/short, slow/long, and slow/short sliders;
- rare rapid click motifs that are validated as a complete block before insertion;
- group numbers that reset at practical phrase boundaries rather than continuing to 50-90 objects;
- dense screens with several visible objects while preserving one interaction interval at a time;
- no simultaneous interactions required from the player;
- reproducible generation when a deterministic random seed is supplied.

The existing game timing, pause accounting, result dialog, rank policy, debug log, and AudioObj playback implementation are outside this task unless a compile error proves that a directly touched API requires a correction.

## Repository and owning code

Main implementation surface:

- `ModularAudience.Forms/Modules/BeatClickerGameForm.cs`
  - `GenerateBeatCatchClassicBeatmap`
  - `ConfigureSliderProfile`
  - `SetSliderEndpoint`
  - slider progress and hit-testing methods
  - `PaintSlider`
  - `BeatCatchHitObject`
- `ModularAudience.Forms/Modules/BeatClickerDifficulty.cs`
  - difficulty curves and profile values
- `ModularAudience.Forms/Modules/BeatClickerDebugLog.cs`
  - generation decisions and input diagnostics
- `ModularAudience.Audio.Tests/BeatClickerGenerationTests.cs`
  - generation invariants and regression coverage

Do not modify:

- `ModularAudience.Audio/AudioObj/AudioObj.Playback.cs`
- unrelated audio playback code;
- generated `bin` or `obj` files;
- unrelated UI or form designer files.

The UI strings and diagnostic messages that are added must be English, consistent with the repository instructions.

## Non-negotiable gameplay invariants

1. A slider occupies the complete interval `[Time, Time + Duration]`.
2. A spinner occupies the complete interval `[Time, Time + Duration]`.
3. A circle occupies its hit instant plus the configured minimum safety gap.
4. No two objects may require player interaction at overlapping times.
5. A visual overlap is allowed when the interaction intervals do not overlap. Several objects may be visible simultaneously.
6. Every object and every sampled point of a slider must stay inside the playable rectangle and its margin.
7. A slider must have a real path. Rendering, scheduled white-head movement, mouse distance, progress, and completion must all use the same path representation.
8. A path may self-intersect only for the deliberately rare loop family. Self-intersection must not cause the progress calculation to jump backward.
9. A failed candidate or failed motif validation must add nothing to `_hitObjects`.
10. Existing pause behavior must remain: the game clock excludes pause-menu time.
11. Do not display an `SS` rank. Preserve the existing rank policy.
12. Keep the results dialog large enough for `Best Streak` and all statistics.

## Pattern grammar: 64 logical patterns without 64 templates

Implement the pattern space as a product of three four-valued axes:

`PatternId = SpatialFamily * 16 + RhythmFamily * 4 + TransformFamily`

This yields 64 logical combinations with IDs 0 through 63. The ID is a classification and coverage key, not a lookup into 64 fixed point arrays.

### Axis A: SpatialFamily (4 values)

1. `Arc`
   - a monotonic start-to-end curve;
   - randomized bend side, bend amount, easing, and endpoint heading.
2. `PartialEllipse`
   - an ellipse segment or elliptical bow;
   - randomized arc span, eccentricity, phase, and direction;
   - endpoints remain exactly the generated slider endpoints.
3. `Wave`
   - one or more sinusoidal or damped sinusoidal deviations;
   - randomized frequency, phase, amplitude, damping, and skew;
   - the wave must be endpoint-corrected so it starts and ends on the endpoints.
4. `Curl`
   - a rare looping, hook, or partial-turn path;
   - randomized loop radius, turn fraction, entry/exit tangent, and direction;
   - it must remain playable and must not be selected on every Hard/Hell slider.

These are broad mathematical families, not visible templates. The generated path must contain at least 12-32 samples or an equivalent adaptive polyline. The exact count may vary with length and curvature.

### Axis B: RhythmFamily (4 values)

1. `EvenPulse`
   - objects are placed at regular subdivisions of the available beat interval.
2. `Acceleration`
   - subdivisions compress and then relax, or relax and then compress;
   - clamp all intervals against the current difficulty safety gap.
3. `Syncopated`
   - off-beat placements and rests with a bounded, deterministic subdivision;
   - never create an overlapping interaction interval.
4. `PhraseEcho`
   - a short motif is echoed with a changed position, direction, or scale;
   - the echo is a remix, not a byte-for-byte duplicate.

The rhythm family controls timing and object sequence, not just visual coordinates. A motif may fall back to a simpler rhythm when the track has insufficient time remaining.

### Axis C: TransformFamily (4 values)

1. `Mirror`
   - reflect the base geometry across its local axis.
2. `Rotate`
   - rotate the local geometry by a continuous random angle.
3. `ScaleAndSkew`
   - change length, transverse amplitude, and local skew within screen-safe limits.
4. `PhaseRemix`
   - change wave/ellipse phase, curvature tension, and traversal direction.

Transforms are continuous. Do not use a small finite table of angles or amplitudes. Quantize only the diagnostic fingerprint, never the actual generated geometry.

### Selection and fuzzy remixes

Use a seeded `Random` or the generator's existing RNG. Select an axis combination using difficulty- and energy-aware weights. Then perturb its continuous parameters:

- endpoint heading;
- length factor;
- duration in beats;
- transverse amplitude;
- phase;
- frequency/cycle count;
- easing exponent;
- loop radius and turn fraction;
- mirror/rotation direction;
- local skew;
- sample count.

The same logical ID may produce many remixes. That is expected. A remix must not be considered a duplicate merely because it uses the same broad family.

Maintain a small recent history of logical IDs and quantized geometry fingerprints. Prefer not to repeat the exact recent ID/fingerprint pair. Do not ban a family globally: a curved wave may follow another wave if its phase, direction, length, timing, or amplitude is materially different. Allow older IDs to reappear after a cooldown so the generator does not become artificially constrained.

A useful fingerprint can include:

- `PatternId`;
- quantized length ratio;
- quantized amplitude ratio;
- quantized phase bucket;
- quantized direction;
- quantized rhythm family;
- quantized duration bucket.

The fingerprint is for variety diagnostics only. It must never alter the fairness checks.

## Slider representation and geometry

Extend `BeatCatchHitObject` with a path representation. The simplest compatible representation is:

- `float[]? PathX`;
- `float[]? PathY`;
- optional `float[]? CumulativePathLength` or normalized cumulative progress.

Keep `X`, `Y`, `EndX`, and `EndY` synchronized with the first and last path points for compatibility with existing logging and placement code. Circles and spinners keep null paths.

Create one shared path pipeline:

1. choose the profile and logical pattern;
2. choose an intended straight heading and nominal length;
3. generate local path samples;
4. rotate and translate them from the slider start point;
5. correct or clamp the path to the playable rectangle;
6. force the first sample to exactly `(X, Y)` and the last sample to exactly `(EndX, EndY)`;
7. compute cumulative segment lengths;
8. reject degenerate paths with too little total length or too many zero-length segments;
9. validate placement against existing objects;
10. commit only after all validation succeeds.

Do not generate a curve and then later move only `X` or `Y`. If placement needs a correction, translate the complete path and recompute its bounds and cumulative lengths.

For constant physical slider speed, define:

- `GetSliderPointAtProgress(obj, progress)` using cumulative arc length;
- `GetSliderProgress(obj, mouseX, mouseY)` by projecting the mouse point onto every path segment and returning the closest point's normalized cumulative distance;
- `IsSliderPointOnPath` using distance to the closest path segment and the current difficulty tolerance.

A loop may have multiple nearby segments. Choose the closest point, but keep progress tied to that segment's cumulative arc length. Do not use a straight start/end projection for curved sliders.

The scheduled white slider head must call `GetSliderPointAtProgress` and therefore move at approximately constant speed along the actual curve. `PaintSlider`, mouse move, mouse up, and completion logic must agree on the same path.

For drawing, use `Graphics.DrawLines` or equivalent segment drawing. Keep the current visual style and fade behavior unless a small change is required to make the path legible.

## Slider speed and duration profiles

Length and duration must be independent random decisions. At least these four regimes must be reachable on each appropriate difficulty:

- fast/long;
- fast/short;
- slow/long;
- slow/short.

Use difficulty-aware bounds. A long slider must not become impossible because its duration is too short, and a short slider must not always receive a long duration. The hard levels may have shorter maximum durations, but the generated path must still be physically traversable.

Log the selected profile, logical pattern ID, length, duration, path family, point count, and any fallback reason. Keep logs concise enough to inspect a complete run.

## Rapid click motifs

Rare high-energy motifs may create four circles at half-beat or another difficulty-safe subdivisions. They must be validated atomically:

- build all candidates in a temporary list;
- validate each candidate against a copy of the occupied objects plus earlier candidates;
- validate all interaction intervals;
- commit all four only when every candidate passes;
- otherwise commit none and continue normal generation.

A rapid motif must not make simultaneous input necessary. Do not use an unconditional four-click insertion that ignores a slider or spinner interval.

## Spinners and groups

Keep spinner isolation based on interaction end time, not visual fade end time. Hard and Hell may use shorter spinners, but only when the track has enough room and the difficulty's minimum duration is respected. Do not force a spinner into an interval that would collide with another object.

Group targets should be randomized within a difficulty-specific range. The target is a phrase boundary, not a lifetime counter. For the high difficulties, keep the upper bound around 20-30 objects unless a current product decision explicitly says otherwise. Log `GROUP START`, group number, and target size. Group numbering must reset at the next generated phrase.

## Required implementation sequence

1. Read the current diff before editing. Preserve user changes.
2. Build the Forms project to expose errors from the existing generator patch.
3. Add the path data and pure path helpers.
4. Connect generation to the path builder and the 64-combination pattern grammar.
5. Replace every straight-line slider assumption in progress, input, hit-test, rendering, and white-head movement.
6. Add or update focused generation tests.
7. Build and run the focused tests.
8. Run `git diff --check` and verify that `AudioObj.Playback.cs` is not changed.

Do not perform a broad refactor while implementing this. If a helper is becoming too large, split it into focused private methods. Keep the public APIs stable.

## Test requirements

Add focused tests in `ModularAudience.Audio.Tests/BeatClickerGenerationTests.cs` or the closest existing test file. Prefer deterministic seeds and pure helper tests where possible.

At minimum cover:

1. `PathStartsAndEndsAtSliderEndpoints`
   - first path point equals `X/Y`;
   - last path point equals `EndX/EndY`.
2. `PathPointsStayInsidePlayableBounds`
   - all points respect the configured margin.
3. `CumulativePathLengthIsMonotonic`
   - cumulative distances never decrease;
   - total path length is positive.
4. `ScheduledSliderHeadUsesArcLength`
   - equal progress steps have approximately equal physical distance on a curved path.
5. `ClosestPointReturnsMonotonicPathProgress`
   - projected points on a path produce sensible normalized progress;
   - a loop does not cause progress to jump backward for sequential points.
6. `LogicalPatternIdCovers64Combinations`
   - all four values of each axis combine to IDs 0-63 without collision.
7. `RecentDuplicateSuppressionAllowsRemixes`
   - an exact recent fingerprint is suppressed;
   - a changed phase/direction/length remix is allowed.
8. `RapidPatternCommitsAtomically`
   - failed validation leaves the object list unchanged;
   - successful validation adds all four objects.
9. `NoInteractionIntervalsOverlap`
   - circles, sliders, spinners, and rapid motifs preserve the safety invariant.
10. `SliderProfileProducesSpeedAndLengthVariety`
    - a deterministic sample run reaches more than one length bucket and more than one duration bucket;
    - the four speed/length regimes are representable.
11. `HardAndHellSpinnersCanBeShorterButRemainValid`
    - no stale assertion requires every spinner to last three seconds;
    - minimum durations and interval safety still hold.
12. `GroupsUseBoundedTargets`
    - group targets are within the configured difficulty bounds;
    - high difficulty targets do not silently grow to 50-90 objects.

If the existing production code keeps generation private inside the WinForms form, extract only small deterministic helpers or test through the existing generation seam. Do not weaken tests just to make the implementation pass.

## Acceptance criteria

The task is complete only when all of these are true:

- Forms project builds successfully.
- Focused BeatClicker generation tests pass.
- `git diff --check` passes.
- `AudioObj.Playback.cs` is absent from the diff.
- No hardcoded list of 64 point templates exists.
- The 64 logical patterns are derivable from the 4x4x4 grammar and observable in debug logs/tests.
- At least four spatial families are visibly distinct, with continuous parameter variation.
- Loop/curl paths are rare and valid rather than dominant or impossible.
- Straight-line assumptions are gone from slider rendering, scheduled movement, progress, and hit-testing.
- The same sampled path is used for drawing and interaction.
- No two interaction intervals overlap, even when several objects are simultaneously visible.
- Fast/long, fast/short, slow/long, and slow/short slider combinations can be generated.
- Hard/Hell spinners can be shorter without reintroducing zero-spinner or overlap regressions.
- Group numbering and target sizes remain readable in verbose logs.
- Existing result presentation and rank behavior remain intact.

## Expected final report from Qwen

Return a concise report containing:

- files changed;
- the path representation and the 4x4x4 pattern grammar used;
- how arc-length progress is shared by rendering and input;
- how duplicate suppression and remixes work;
- tests and commands run;
- any remaining warning or runtime limitation.

Do not claim runtime validation that was not actually performed.
