# Audio Decompile

Audio Decompile converts one selected `AudioObj` into separated stems, atomic slices, and either a Mix Track project or one Audio Collection per stem.

The feature is available from the Audio Collection right-click menu as **De-compile ...**. It is enabled only when exactly one item is selected.

## Source ownership

Opening the configuration window starts by synchronously cloning the selected `AudioObj` in the `DecompileConfigDialog` constructor. The dialog owns that private snapshot; it never retains the collection's original object.

The configuration and progress windows are modeless and unowned. They are shown with `Show()`, not `Show(owner)`. The source collection and its original audio can therefore be closed immediately after the configuration window opens. Results also own their own audio:

- Audio Collection output transfers atomics into newly owned collection windows.
- Project output copies stem data into `MixArrangement` project sources.
- Cancellation or failure publishes no partial output.

## Workflow

The operation executes in this order:

1. Resolve BPM.
2. Separate the private source snapshot into stems.
3. Atomize each stem or create beat/bar chunks.
4. Build and publish the selected output.

All stages report overall progress and a human-readable stage label. Closing the progress window, pressing **Cancel**, or closing the configuration window requests cancellation. Cancellation is checked at safe DSP/model boundaries; the output is not published after cancellation.

## BPM

Project tempo is resolved in this order:

1. `AudioObj.Bpm` when it is plausible.
2. `AudioObj.ScannedBpm`.
3. `BeatScanner.ScanBpmAsync`, which falls back to `BeatScanner.EstimateBpmAsync`.

If all three paths cannot produce a plausible tempo, the decompile run fails explicitly instead of silently using an arbitrary BPM.

## Separation tab

The separation selector defaults to **Demucs** and supports:

### Demucs

All stems exposed by the selected ONNX model are separated. No per-stem check list or interactive model-load button is part of this workflow; the model is loaded automatically when Decompile starts.

The tab provides:

- ONNX model selection and model-directory browsing.
- CPU or DirectML execution.
- Multiple DirectML devices for parallel stem assignment.
- CPU fallback and CPU-operator settings.
- DirectML graph fusion is always disabled for Demucs compatibility.
- Graph optimization and CPU execution mode.
- Intra/inter-op thread limits.
- ONNX profiling and optimized-model path.
- CPU FFT workers and chunk overlap.
- DirectML graph-fusion compatibility.
- CPU memory-pattern and arena options.
- Worker spinning and prepacking options.
- CUDA CuFFT detection and automatic CUDA 12/13 toolkit selection when available.
- Idle model retention.

DirectML adapter and CUDA toolkit discovery runs when the modeless configuration window is opened. Extra ONNX session entries, session logs, load buttons, and the stem selector from the standalone Demucs window are intentionally omitted.

Demucs models are acquired through the shared model lease/cache. Each lease serializes operations on its model and retains the native session only for the configured idle duration.

The DirectML Demucs session is initialized with graph fusion disabled. Enabling DirectML graph fusion for this ONNX export produced corruption even though FFT and interleaving were correct. The CUDA CuFFT inverse path retains the required `1/N` transform scaling. After the complete overlap-add and final ONNX stem reconstruction, every backend receives the same post-processing: per-channel DC removal and one stereo-preserving peak limit of `1.0` when needed.

### Analog Separation

Analog Separation exposes editable frequency bands, automatic band analysis, FFT window, overlap, sharpness, deterministic threading, timbre-aware peak handling, harmonic-aware handling, and subtract-from-original behavior.

### Best-practice deterministic

The deterministic panel exposes presets and all core STFT, median-filter, CQT, pYIN, ILRMA, ensemble, and component parameters. Instrument profiles can be selected explicitly, supplemented by automatic profiles, or detected fully automatically. The pipeline always separates every detected acoustic group plus the residual.

## Atomize tab

The atomizer selector supports:

### Transient Atomizer V4

Parameters exposed by the dialog:

- Sensitivity.
- Minimum slice and atomic duration.
- Tail padding.
- Single-atom fallback.
- Minimum RMS.
- Similarity threshold.
- Maximum variants per cluster.
- Optional collection deduplication.

V4 atomics preserve `Decompile.SourceStartFrame` and `Decompile.SourceFrameCount` metrics. These values are generated at the slice boundary and remain stable after deduplication or legacy conversion.

### Adaptive Legacy Atomizer V1

The legacy method uses its existing adaptive onset, merge, trim, fingerprint, and clustering pipeline. It exposes the existing similarity hint and an adaptive-threshold mode. Project output never relies on the processed/faded atomic sample; it uses only the atomic source position and references untouched stem data.

V4 is the recommended default. V1 remains available for compatibility and comparison.

The Decompile defaults use 60 ms minimum slice, 30 ms tail padding, 20 ms minimum atomic duration and a 0.001 RMS floor. These values retain more complete synth/noise bodies than the earlier drum-focused defaults.

## Output tab

### Mix Track Project

Project and Collection output can be enabled independently, including both at the same time. Dual output shares the complete atomic pool: the project receives every occurrence, while Collection deduplication can still select representative samples without changing project placement.

Project output creates one vertical lane per separated stem. Each lane receives clips at the exact original timeline position.

Available project detail:

- **Individual atomics / transients**: detected event boundaries.
- **Beat-sized segments**: contiguous one-beat chunks from source frame zero.
- **Bar-sized segments**: contiguous four-beat chunks from source frame zero.

Overlapping segments can be handled in either of two ways:

- **Allow overlaps in the stem lane**: all clips for a stem remain on one lane and mix where their source ranges overlap.
- **Create additional lanes for overlaps**: deterministic interval allocation keeps clips on any lane non-overlapping and prefers lanes containing the same tonal/spectral family. Drum classifications use their sample tag; synth/noise atomics use their dominant spectral frequency and centroid. Extra lanes are named `Stem - overlap 2`, `Stem - overlap 3`, and so on.

The project **segment onset attack** is configurable from 1 to 100 ms and is stored as a clip fade-in. It avoids first-sample clicks without modifying the source stem or atomic waveform.

Beat and bar boundaries are derived from the resolved BPM. Project clips use full-frame precision from the untouched stem, so beat/bar output preserves the complete separated stem waveform. Transient output intentionally omits regions that the selected atomizer does not classify as events.

Project output:

- Always disables deduplication.
- Uses `MixOverlapMode.MixOverlap`.
- Leaves timeline snapping off.
- Sets the resolved BPM on every stem and source.
- Adds no source-reference lane.

For transient atomics, a clip's `Start` and `SourceOffset` are both the original start time. The project therefore aligns stem lanes without shifting, snapping, normalizing, or using a processed atomic sample.

If a stem has no detectable atomics in transient mode, the whole stem is retained as one clip and a warning is logged.

The configuration window closes immediately after a valid Decompile run is started. Ownership of its private source snapshot is transferred to the running operation and released after success, cancellation, or failure.

All configuration controls, including separation method, ONNX options, analog bands, deterministic DSP/profile settings, atomizer values, output selection, overlap handling, attack and worker count, are stored in `%APPDATA%/ModularAudience/decompile-settings.json` and restored on the next open.

Demucs model loading and ONNX separation occupy 5–85% of the operation progress. Atomization and collection preparation occupy the remaining 15%; ETA uses a smoothed observed progress rate instead of a direct elapsed/fraction jump.

### Audio Collections

Collection output creates one independently owned Audio Collection per stem. Optional deduplication keeps representative repeated atomics. The collection mode owns the atomics after the result is transferred to the newly opened windows.

## Mix Track View overlap merge

Right-clicking an internal boundary in `MixOverlap` or `AllowOverlapOverwrite` renders the complete connected overlap group into one segment. Any one start/end boundary selects the whole transitive group, so multiple boundaries do not have to be clicked separately. The operation is one undo step.

- `MixOverlap` sums all clips with their gain/fades.
- `AllowOverlapOverwrite` follows the same visual top-to-bottom order as the timeline; the visibly top clip replaces underlying sample regions.
- A special four-direction merge cursor and tooltip appear only on mergeable boundaries.

## Workers and memory

**Maximum workers** is limited to `Environment.ProcessorCount`. The value caps:

- Parallel stem atomization.
- V4 internal atomizer and deduplication workers.
- Analog separation threads.
- Deterministic separation threads.
- Demucs FFT and ONNX thread options.
- The number of selected DirectML devices used concurrently.

The pipeline performs a conservative managed-memory preflight before separation. It rejects configurations whose estimated stem/output working set exceeds the safe available-memory budget rather than silently reducing model or atomization quality.

## Error handling

Failures are logged with `LogManager` and shown in a copyable OK error dialog. Cancellation is reported as cancellation rather than as a processing failure. Deterministic analysis warnings and Demucs CPU-fallback warnings are added to the decompile result and written to the log.

## Limitations

Audio Decompile is a reconstructive production aid, not a recovery of the artist's original DAW session. It cannot reconstruct MIDI arrangements, plug-in chains, automation, side-chain routing, hidden edits, sample provenance, or exact pre-master balances.

Separation quality depends on the selected method and model. Transient atomization is event-oriented and may not represent sustained material; beat/bar chunk output is the appropriate choice when complete stem coverage is required.

## Tests

Focused tests cover:

- Independent constructor snapshot ownership after disposing the original.
- Exact beat/segment project placement and one lane per stem.
- Project frame timing from V4 atomics.
- Collection deduplication on/off without modifying the source.
- Cancellation before output publication.

The regression tests are CPU-only and do not use ONNX playback, GPU execution, or interactive audio playback.
