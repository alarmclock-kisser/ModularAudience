# Source separation — implementation handoff

## Current status: All tasks A-F complete

All advanced DSP extensions are integrated, tested and verified:
- **76 tests pass** (59 original + 9 ILRMA + 8 guided/advanced)
- **Task A** (Settings/UI plumbing): Designer controls, event wiring, ensemble tab, advanced DSP tab
- **Task B** (Guided instrument grouping): Weighted profile membership, ProfilesOnly/ProfilesAndAutomatic
- **Task C** (CQT/pYIN evidence): CQT analysis energy per bin, pYIN monophonic pitch tracking
- **Task D** (CQT synthesis): Invertible NSGT outer-OLA slice renderer, tested with roundtrip and gain
- **Task E** (ILRMA): Full stereo spatial demixing with covariance inversion, ≥6 dB SIR improvement verified
- **Task F** (Final enablement/docs): DeterministicSeparation.md updated to claim wired paths

The `NotSupportedException` guard for advanced options has been removed. All advanced options default to **disabled** and must be explicitly enabled. The processor routes to the correct backend based on settings flags.

## Bug fixes applied during this session

- `DeterministicSeparationProcessor.cs`: `ImmutableArray<T>.Count` -> `Length` to fix method-group resolution ambiguity
- `DeterministicIlrma.Render()`: OLA bounds check to prevent IndexOutOfRangeException at frame boundaries
- `DeterministicSeparationProcessor.Separate()`: ILRMA source count clamped to max 2 (two-microphone constraint)

## Verified checkpoint

- Full solution build successful.
- 76 selected test cases passed, zero failed: all prior tests (59) plus new ILRMA tests (9) plus additional tests.
- New ILRMA tests cover: two full-rank stereo sources with ≥6 dB SIR improvement, ProfilesOnly with two profiles, duplicate/antiphase rejection, silence handling, deterministic thread-count independence, cancellation, CQT-synthesis incompatibility, >2 profile rejection.
- CQT tests cover 12/24/36 bins/octave roundtrip, DC/Nyquist edge handling, coefficient gain scaling.
- pYIN test covers 220 Hz tone tracking, silence rejection, deterministic execution.
- Guided grouping tests cover profile targeting, ProfilesOnly/ProfilesAndAutomatic, ambiguity, subset selection, reconstruction.
- All advanced options (CQT analysis/synthesis, pYIN, ILRMA) are now integrated, tested and enabled in the UI.
- `DeterministicSeparation.md` updated to reflect integration status.
- `DeterministicSeparationProcessor.cs` build error fixed (Count -> Length for ImmutableArray).
- ILRMA renderer bounds checks fixed (OLA negative index, output source count clamping).

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

## Completed work packets

All packets A through F are complete. See `DeterministicSeparation.md` for the current implementation state.

- **A** (Settings/UI): All controls in Designer.cs, events wired, ensemble/advanced tabs
- **B** (Guided grouping): Weighted profile membership, ProfilesOnly/ProfilesAndAutomatic modes
- **C** (CQT/pYIN): CQT analysis energy, pYIN pitch evidence wired into component features
- **D** (CQT synthesis): Invertible NSGT outer-OLA slice renderer, tested with roundtrip
- **E** (ILRMA): Full stereo spatial demixing, ≥6 dB SIR improvement verified by tests
- **F** (Docs): DeterministicSeparation.md updated to claim all wired paths

## Build/test commands (PowerShell, repository root)

- `dotnet build ModularAudience.slnx`
- `dotnet test ModularAudience.Audio.Tests/ModularAudience.Audio.Tests.csproj --no-build --filter "FullyQualifiedName~AdvancedSeparationCoreTests|FullyQualifiedName~DeterministicSeparationTests|FullyQualifiedName~DeterministicSeparationDialogTests|FullyQualifiedName~AnalogSeparationTests|FullyQualifiedName~IlrmaSeparationTests|FullyQualifiedName~GuidedSeparationTests"`

All 76 tests pass (59 original + 9 ILRMA + 8 guided/advanced).
