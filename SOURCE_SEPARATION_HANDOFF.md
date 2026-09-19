# Source separation — implementation handoff

## Start here (token-budget stop, not a completed feature release)

The user requested configurable genuine CQT analysis AND invertible CQT synthesis, pYIN, optional experimental stereo ILRMA, and manual instrument ensembles. Both guided modes are wanted: selected profiles + Residual; selected profiles + extra automatic groups + Residual. UI controls should be checkboxes and/or DomainUpDowns. Work stopped at the user's request because the Copilot quota reached 98.1%.

**Working now:** the original deterministic STFT/HPR/MDL/IS-NMF separator and modeless dialog, including progress bar, cancellation and source selection. Context-menu path: AudioCollectionView → Source Separation → Best-practice deterministic.

**New but standalone:** real invertible NSGT-CQT and probabilistic YIN/HMM/Viterbi cores. Their options are NOT wired into the production separation pipeline or UI. There is a catalog of 16 DSP instrument priors and validated options, but no guided grouping yet.

**Partial only:** ILRMA training-data, numerical, NMF and spatial-update helpers compile; the `DeterministicIlrma` orchestrator, source-image projection-back and production wiring are missing. No ILRMA separation-quality claim is justified.

`DeterministicSeparationProcessor.AnalyzeAsync` deliberately throws `NotSupportedException` for enabled advanced options or a non-Automatic ensemble. This avoids pretending unsupported options affect audio. Remove this guard separately for each completed, tested path, not all at once. Keep defaults working. The guard is covered by `UnconnectedAdvancedOptionsAreRejectedInsteadOfIgnored`; update only the supported-option cases as real implementations land.

## Verified checkpoint

- Full solution build successful.
- 55 selected test cases passed, zero failed: `AdvancedSeparationCoreTests`, `DeterministicSeparationTests`, `DeterministicSeparationDialogTests`, `AnalogSeparationTests`.
- New CQT tests cover 12/24/36 bins/octave, direct roundtrip including DC, Nyquist and edge impulses, and coefficient gain 0.5. Error threshold 1e-9, no residual correction.
- pYIN test covers a 220-Hz tone (under 20-cent error for interior voiced frames), silence, deterministic thread-count changes, and pre-cancellation. This is NOT broad pitch-quality validation.
- Existing known-mixture test requires two useful non-residual stems with gain >=0.20, correlation >=0.85 and SIR improvement >=6 dB.
- ILRMA helpers have NOT been exercised by runtime separation tests.
- No new dependencies, app restart, commit or push. Many files are still untracked; preserve them and inspect git status before editing. Do not discard previous/user changes.

## Constraints for every local-LLM task

Read `.github/copilot-instructions.md`. Work only on the named task, do not rewrite the whole separator. All application strings/logs are English. Controls and event wiring belong in the corresponding `.Designer.cs`; DSP stays in Audio/Processors_V4. Keep Show(), not ShowDialog(). Use existing MathNet, CPU only, no models/cloud/paid packages. Preserve stereo, lengths, gains, source immutability, cancellation, output ownership and Residual. Do not peak-normalize stems independently. Do not change global MathNet worker settings. Never weaken a failed quality assertion merely to get green tests.

Compile and run the targeted tests after each task. For a failure, record the exact test name and measured error. Work in small increments; the DSP review tasks marked **specialist review** are not routine checkbox-wiring jobs.

## Code/API map

All following DSP files are under `ModularAudience.Audio/Processors_V4/`.

- `DeterministicSeparationSettings.cs`: UseCqtAnalysis, UseCqtSynthesis, UsePyin, UseIlrma (default false); CqtBinsPerOctave 12/24/36; CqtMinimumHz; PyinMinimumHz/PyinMaximumHz; IlrmaIterations/IlrmaComponents; EnsembleMode; immutable InstrumentProfiles. `IsEquivalentTo` compares profile contents instead of array identity.
- `InstrumentProfileCatalog.cs`: Automatic / ProfilesOnly / ProfilesAndAutomatic; catalog includes Synth Bass, Bass Guitar, Drums, Kick, Snare, Hi-hat/Cymbals, Vocals, Synth Lead, Synth Pad, Piano, Guitar, Strings, Brass, Woodwinds, Organ, Mallets. These are heuristics, not trained classifiers. Profiles expose pitch bounds, brightness, harmonic/percussive/noise priors and SpectralWeight(frequency).
- `ConstantQTransform.cs` (+ ConstantQBand/FilterBank/Fourier): `Create(sampleRate,binsPerOctave,minimumHz[,token])`; Length, SampleRate, Bands; `Forward(double[Length],token)` -> Complex[band][time]; `Inverse(coefficients,token)` -> double[Length]; `Power(coefficients,band,samplePosition)`. Bands have CenterHz, BandwidthHz, CoefficientCount. Positive band b has negative partner Bands.Count-b. Same real masks at the same coefficient index preserve conjugacy. Coefficient time is j*Length/CoefficientCount. This is a periodic slice NSGT, not a full sliCQT streaming protocol. Canonical dual windows cover DC/Nyquist and all bins. The caller must implement outer overlap-add. Maximum slice length is 2^20; never silently lower resolution.
- `PyinPitchTracker.cs` (+ PyinSettings/Yin/Candidates/Observations/Transitions/Viterbi): `Track(float[] mono,int sampleRate,int hopSize,double minHz,double maxHz,int threads,CancellationToken)` -> PyinPitchFrame[]. Centered frame count = samples.Length/hop+1. Each frame: FrequencyHz (zero unvoiced), VoicedProbability (pre-HMM acoustic mass). FFT YIN difference, Beta(2,18) thresholds, Boltzmann trough prior, voiced/unvoiced pitch-state Viterbi. Monophonic, not a polyphonic detector.
- `IlrmaTrainingData.Read(snapshot,settings,progress,token)` -> distributed complex stereo STFT observations; IsSilent/Left/Right/FrameIndices. IMPORTANT: the missing caller must validate exactly two channels and aligned/nonempty audio BEFORE calling this helper, which indexes samples as stereo.
- `IlrmaSpatialModel.Train(data,sampleRate,settings,progress,token)` -> IlrmaMatrix[] demixing matrices per positive frequency. Uses independent source IS-NMF variance models and sequential iterative projection. `IlrmaMatrix.cs` has inversion and Apply/Element operations; verify conventions directly before projection-back.
- Existing `DeterministicSourceModel.Train` owns dictionary/grouping. `DeterministicTrainingData` samples contiguous windows with real HPSS context. `DeterministicSourceFeatures`, `DeterministicSourceGrouping`, `DeterministicSourceMasks` describe/group components and build masks. `DeterministicSynthesis` performs correct STFT OLA.
- `DeterministicSeparationAnalysis` currently owns a concrete `DeterministicSourceModel`; alternative renderer/model support is still needed.

## Small work packets for a local coding model

Execute one packet per session. Report changed files, build/test results and remaining caveats. A smaller model can handle A, UI portions of F, and focused tests/docs. B–E need careful DSP review.

### A — Settings/UI plumbing without claiming unfinished algorithms work

Files: Forms/Modules/Dialogs/DeterministicSeparationDialog.cs and .Designer.cs; dialog tests.

1. Replace `current.Settings != settings` with the provided semantic `IsEquivalentTo` comparison when reading repeated profile selections.
2. Add an advanced/ensemble section (prefer tabs to avoid an excessively tall window): four checkboxes, BPO DomainUpDown, pitch/frequency numeric controls, ILRMA iteration/rank controls, ensemble-mode DomainUpDown, CheckedListBox of catalog profiles. Construct/wire ALL controls in Designer; adding data items at runtime is allowed.
3. Keep unfinished controls disabled and explicitly labeled pending until their DSP packet is complete. Populate settings via immutable profile selection, not a mutable shared list. Invalidate analysis on every option/profile change, including committed ItemCheck state. Disable inputs during jobs.
4. Preserve progress/status/cancel/close handling and output-memory estimate. Tests must verify defaults, invalidation and settings roundtrip. Do not expose enabled no-op checkboxes.

### B — Guided instrument grouping (independent of advanced transforms)

Files: SourceModel, SourceGrouping, SourceMasks, optional new focused GuidedSourceGrouping helper.

1. Use profile priors to assign weighted component membership using harmonic/percussive evidence, pitch, spectral envelope/brightness and noise evidence. Avoid assigning by name only or crude hard frequency cuts.
2. Existing groups have integer Components; extend with per-component weights. Sum memberships across all output groups must be <=1 per component. Masks must actually use those weights. Describe uncertainty; energy/evidence values must reflect fitted weights.
3. ProfilesOnly emits requested profile targets plus Residual; ProfilesAndAutomatic preserves unmatched fractions in extra stable automatic groups without double-counting. More than one instrument may still be indistinguishable. Do not invent confident detection for an unsupported profile.
4. Use at least as many candidate NMF components as requested profiles (respect MaxComponents; validation already requires this). Preserve the old Automatic path byte/tolerance-equivalent with flags off.
5. Tests: known low tonal + broadband transient mixture with Synth Bass/Drums, guided output names AND measured interference suppression, unchanged original, reconstruction, subset selection, ambiguous profile warning. Then lift ONLY the ensemble guard, enable ensemble UI.

### C — Real CQT and pYIN evidence integration (specialist review)

Split into C1 CQT evidence and C2 pYIN evidence; validate separately.

- C1: analyze actual, contiguous source samples with ConstantQTransform. Aggregate positive-band energy over the sampled training windows; retain stereo energy without anti-phase mono cancellation. Relate component activations to these constant-Q envelopes and use them in actual grouping/profile compatibility. Do not merely relabel STFT log bands CQT. Account for each band's coefficient time rate and power normalization.
- C2: apply pYIN preferably to provisional tonal candidate waveforms, not claim all voices in a polyphonic mix are tracked. If using a dominant-channel mono trajectory, label that limitation. Use voiced evidence/harmonic compatibility to affect grouping/profile assignment. Do not concatenate distant analysis windows and pretend they are one continuous pitch trajectory.
- Both: bounded windows with context, cancellation, progress stages; disabled flags leave baseline behavior unchanged. Tests must show real calls and observable meaningful evidence/assignment, not simply nonempty output. Add glide, unvoiced gap and harmonic-rich tone pYIN tests. Enable/lift each guard independently.

### D — CQT synthesis (specialist review; keep separate from ILRMA)

1. Create a slice renderer with centered outer periodic Hann window, slice hop Length/4, padding and normalization by accumulated window squares. Do not rely on Residual to fix a broken inverse.
2. For each slice/channel, run true CQT Forward. Interpolate learned per-source STFT masks in global time and frequency onto each CQT coefficient (j*Length/M); use abs(CenterHz) for positive/negative partners and identical partner masks. Handle DC/Nyquist. Reuse the global learned dictionary; do not relearn group identities per slice.
3. Inverse each selected source, outer-window and overlap-add. Use original level/channel phase; Residual remains final mixture difference. Temporary CQT memory and source outputs need explicit accounting/cancellation.
4. Test unity outer-OLA on short input and slice boundaries WITHOUT residual, coefficient mask interpolation alignment, stereo antiphase, finite transients, actual known-source suppression. Core half-gain tests alone do not prove this renderer. Then enable UseCqtSynthesis; analysis and synthesis switches must be independent.

### E — Finish ILRMA orchestrator/projection-back (specialist review)

Create DeterministicIlrma plus focused helpers. Proposed API: Train(snapshot,settings,progress,token); Sources/EstimatedRank; Spectrum(Complex[][] inputChannels,int sourceIndex,int outputChannel,token) returning a full conjugate-symmetric FFT.

1. Validate stereo, snapshot/sample format, settings and rank; handle silence BEFORE training matrices. Training helpers are partial and unvalidated at runtime: review complex conjugation, scale normalization, loaded covariance inversion and objective behavior.
2. Let W be learned demixing, A=inverse(W). Output channel c/source n must be A[c,n]*(W*X)[n]. Sum the two stereo source images back to X directly in complex domain; no residual workaround. Real DC/Nyquist and negative-frequency mirroring matter. Keep W and source ordering fixed across all render blocks.
3. Profile priors already affect source NMF initialization. Complete global source/profile assignment and optional source previews for CQT/pYIN evidence; those flags must not become ignored in ILRMA mode. Never reassign source labels independently per block.
4. Automatic => two spatial groups; ProfilesOnly with one profile => matched source + Residual; one profile with extra-auto => both; two profiles => at most two sources. Source count is not arbitrarily extendible beyond two microphones.
5. Integrate analysis model dispatch (concrete Model type currently assumes NMF), synthesis of stereo projected images and fresh AudioObj ownership. Reject mono/rank-deficient stereo, >2 profiles and CQT-synthesis+ILRMA clearly; CQT/pYIN analysis may coexist once wired.
6. Tests BEFORE UI enablement: two full-rank, overlapping synthetic stereo sources, both audible in both input channels; actual >=6-dB interference improvement, global permutation stability, direct two-image reconstruction, duplicate/antiphase input rejection, silence, deterministic execution and cancellation. Do not call a finite output a separation-quality proof.

### F — Final enablement/documentation

After each path is integrated and tested, enable its controls, remove only its unsupported guard, update capability tests and describe real compatibility/costs. All flags default off. Document large CQT slice/FFT memory, pYIN tracking limits, ILRMA two-source assumptions and heuristic profile ambiguity. Keep progress visible in every long phase; never start expensive work simply by opening the form. Update DeterministicSeparation.md only to claim paths that are actually wired.

## Build/test commands (PowerShell, repository root)

- `dotnet build ModularAudience.slnx`
- `dotnet test ModularAudience.Audio.Tests/ModularAudience.Audio.Tests.csproj --no-build --filter "FullyQualifiedName~AdvancedSeparationCoreTests|FullyQualifiedName~DeterministicSeparationTests|FullyQualifiedName~DeterministicSeparationDialogTests|FullyQualifiedName~AnalogSeparationTests"`

Visual Studio build/Test Explorer were used for the verified checkpoint above. These CLI commands are the equivalent suggested continuation, not a claim they were executed here. Do not kill/restart the running application if build files are locked; report the blocker.
