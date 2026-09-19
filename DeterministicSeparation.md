# Best-practice deterministic source separation

> Advanced-extension checkpoint: standalone CQT and pYIN cores and partial ILRMA helpers now exist, but are **not connected to this production workflow**. Advanced API switches are deliberately rejected until integration is tested. See [SOURCE_SEPARATION_HANDOFF.md](SOURCE_SEPARATION_HANDOFF.md) for verified status and small continuation tasks. The behavior described below remains the active baseline.

## Use

1. Select one loaded mono or stereo track in AudioCollectionView.
2. Right-click → **Source Separation → Best-practice deterministic** (third entry, after Demucs and Analog).
3. Choose settings and click **Detect sources**. Opening the modeless dialog does not start analysis.
4. Inspect the acoustic groups, evidence scores, pitch/pan hints, warnings and output-memory estimate. Select the groups to export.
5. Click **Separate & restore**. A new modeless audio collection contains the selected groups and **Residual**. Use the application's existing export/playback actions there.

Deselection routes material to Residual, not to deletion. With no groups selected, Residual is an exact copy of the analysis snapshot. Keep every selected stem **and Residual** at their original relative gains to reconstruct the mix. The source is not modified; stems are not individually normalized or clipped. Their channel count, sample rate, sample count and duration are preserved, and each receives a new identity.

**Cancel** stops the current operation without publishing partial stems. Closing during processing requests cancellation and waits for the worker to finish. A successful analysis owns a private sample snapshot: later source edits do not change its output. Do not modify source samples concurrently while the initial snapshot is being copied. Changing a DSP setting invalidates the analysis and requires another detection pass.

## What the implementation actually does

All processing runs locally on the CPU in `ModularAudience.Audio/Processors_V4`. It uses existing managed MathNet.Numerics FFT/SVD functionality, not a downloaded model, cloud service, GPU, native MKL package or added PDF dependency. NMF fits numerical factors to this recording; there is no pretrained inference model or training corpus.

1. **Centered STFT:** periodic Hann window, fixed 75% overlap, zero-padding outside the source. Power is averaged across channel spectra, never computed from a mono sum, so opposite-phase stereo survives. Complex channel spectra are retained only for the current synthesis block.
2. **Harmonic/percussive/residual evidence:** temporal and frequency medians of power, with a configurable separation margin. If their median powers are H and P, the harmonic and percussive memberships are `H/(H + margin² P)` and `P/(P + margin² H)`. Uncertain membership is retained as residual. Context frames prevent median filters from treating processing-block boundaries as audio boundaries.
3. **Representative dictionary training:** deterministic, distributed contiguous windows across the track, including its beginning/end. Each window has real neighboring frames for HPSS; distant sampled frames are never treated as adjacent by the median filter. The analysis-frame setting bounds the training matrix. Sampling is not a guarantee of capturing every rare/quiet event.
4. **Effective rank:** SVD of a small uncentered log-frequency feature second-moment matrix followed by a covariance MDL criterion. Its equal-noise-eigenvalue/independent-observation assumptions are approximate for overlapping music spectra. This selects a bounded *spectral model order*, **not the number of instruments**. Non-silent input has at least one modeled direction; silent analysis yields zero.
5. **IS-NMF:** deterministic diverse-spectrum initialization, nonnegative W/H factors, multiplicative Itakura–Saito MM updates with square-root exponent. W normalization is coupled to H so it does not change WH. Positive relative numerical floors stabilize the calculation. There is no guarantee of a global optimum or of exact physical sources.
6. **Conservative component grouping:** negligible fitted-energy components are pruned; compatible components are merged with complete-link criteria for log-band envelopes, activation correlation, HPR character, harmonic relationships and pan. QIFFT-style peak interpolation and harmonic template evidence supply a representative pitch hint, not a pitch transcription. Group descriptions such as bass-like, sustained/harmonic, transient/percussion-like and mixed are explicitly uncertain. Scores are uncalibrated acoustic evidence, **not class probabilities**; fitted energy shares are not exact original instrument loudness.
7. **Stable blockwise separation:** the learned dictionary is fixed across the recording. Each block fits its own activations, with halo/context frames and onset-sensitive initialization/mask smoothing. Grouped Wiener-style power responsibilities are attenuated by HPR/pan compatibility; model mismatch and ambiguity leave room for Residual. A convex floor includes the implicit residual, keeping the mask sum at most one. A nonzero floor reduces sharp holes but also permits bleed.
8. **Reconstruction:** real nonnegative masks apply equally to the original complex spectra of all channels. The original phase is retained; inverse FFT and window-square-normalized overlap-add reconstruct the original time axis, including very short inputs and both ends. Selected stems are accumulated in fixed order. Residual is finally calculated as original minus their sum, preserving ambiguous/unselected content and correcting reconstruction roundoff. The reported maximum mixture error measures this sum, **not separation quality**.

## Settings and tradeoffs

| Setting | Meaning |
| --- | --- |
| FFT/window size | Larger windows distinguish nearby low frequencies better but localize attacks less precisely. Powers of two, 256–16384. Default 4096. |
| Max components | Upper bound for MDL/NMF model complexity, not a requested instrument count. Default 16; maximum 32. |
| Iterations | IS-NMF update count for training and block activation fitting. More iterations cost CPU and do not guarantee perceptually better stems. Default 80. |
| Analysis frames | Maximum number of representative training columns. Increasing it improves coverage but increases training memory and work. Default 1024; maximum 8192. |
| Block frames | Core STFT frames per render block, plus automatic context. Larger blocks require more temporary memory. Default 128. |
| Median frames / bins | Odd temporal/frequency HPR filter lengths, 3–65. Defaults 17. Their physical durations/bandwidths depend on sample rate and FFT size. |
| Separation margin | Stronger dominance required for H/P evidence; higher values leave more ambiguity in Residual. Default 2. |
| Mask floor | Soft-floor mixture strength, 0–0.05. Default 0.001. More floor can reduce mask holes while allowing more interference. |
| Transient preservation | How strongly positive spectral flux reduces mask/history smoothing at attacks. Default 75%. It does not synthesize missing attacks. |
| Threads | Maximum parallelism of this operation's explicit worker loops. Default half the logical processors, minimum one. It is not a process-wide cap; separate simultaneous dialogs have separate workers. |

Use spare CPU capacity if other tracks are playing. The operation does not alter playback-thread priorities or global MathNet settings. Results are repeatable for identical inputs/settings on the same runtime; reductions do not depend on worker scheduling. Different runtimes/hardware need not be bit-identical. Block-size changes can make small differences because activation initialization, numerical floors and smoothing use local context.

The UI estimates **output buffers only**: `interleaved sample count × 4 bytes × (selected groups + 1)`. The original, private snapshot, dictionary, training matrices and temporary spectra require additional RAM. For three-minute 44.1-kHz stereo, each output alone is about 60.6 MiB. AudioObj still stores complete output tracks; blockwise spectral processing does not make total output storage constant-size. A preallocation guard rejects clearly excessive output requests rather than silently reducing quality. Training work is roughly proportional to frames × bins × components × iterations; increasing all controls substantially is expensive.

## Scientific limits and deliberate omissions

- General blind separation of arbitrary overlapping mono/stereo instruments is underdetermined. Timbre resemblance, MDL rank and NMF atoms cannot prove instrument identity. Several instruments may remain together; one instrument may span several groups.
- Spatial evidence is an interchannel power/pan cue, not DUET delay clustering or a multichannel demixing matrix. Equal masks preserve each channel's original phase relationships but cannot unmix fully overlapping spatial sources.
- Residual is **not just noise**. It can contain useful music, interference, attacks, silence, unseen events and deliberately unselected sources. Do not discard it automatically.
- The implementation does **not** claim invertible CQT/NSGT/sliCQT, Bayesian ARD/SON, pYIN/Viterbi trajectories, McAulay–Quatieri partial tracking, spectral-eigenvector clustering, ILRMA or a phase-locked vocoder. Those are separate techniques from the supplied research overview, not synonyms for the routines above.
- A phase vocoder is unnecessary for this unchanged time axis and could introduce phase artifacts. Retaining mixture phase avoids artificial phase trajectories; it does not recover the unknown original phases of overlapping sources. Long-window masking may still smear isolated attacks.
- “Restore” here means transient-aware artifact reduction and mixture-consistent resynthesis, not de-clipping, generative inpainting, denoising guarantees or recovery of missing information. Higher complexity alone cannot guarantee better audible separation.
- Synthetic regression tests check engineering invariants and an identifiable mixture. Listening comparisons on representative recordings and reference-stem evaluations remain necessary; no Demucs-level quality claim is made.

## Validation and code map

- `DeterministicSeparationSettings.cs`: validated public settings, immutable analysis/result contract.
- `DeterministicSpectrogram.cs`, `DeterministicTrainingData.cs`: centered/channel-aware analysis and bounded sampling.
- `DeterministicRankEstimator.cs`, `DeterministicNmf*.cs`: model order and IS factorization.
- `DeterministicSource*.cs`, `DeterministicPitchFeatures.cs`: features, grouping and masks.
- `DeterministicSeparationProcessor.cs`, `DeterministicSynthesis.cs`: snapshot, output ownership, cancellation and resynthesis.
- `ModularAudience.Forms/Modules/Dialogs/DeterministicSeparationDialog*`: Designer controls and modeless workflow.
- `ModularAudience.Audio.Tests/DeterministicSeparationTests.cs`: direct unity-mask reconstruction, known-mixture separation, deterministic threading, snapshot/selection, invalid input and cancellation.
- `ModularAudience.Audio.Tests/DeterministicSeparationDialogTests.cs`: idle construction, settings invalidation and actual modeless context-menu invocation.

Background references: Math.NET Numerics documentation; Driedger, Müller and Disch, *Extending Harmonic-Percussive Separation of Audio* (ISMIR 2014); Févotte, Bertin and Durrieu, *Nonnegative Matrix Factorization with the Itakura–Saito Divergence* (2009). The supplied research overview motivated the design but its stronger reconstruction and instrument-identification guarantees are not adopted.
