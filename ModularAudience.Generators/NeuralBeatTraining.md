# Neural Beat Engine training sessions

## Model and compatibility

The engine is a feedback-trained categorical rhythm model. It does **not** analyze sample audio or run a deep neural network. Each branch learns two sets of logits:

- **Step × sample slot**: which ordered sample slots are likely at each grid position.
- **Step × hit count**: rest (zero hits), one hit, or multiple simultaneous hits, up to `1 + Extra notes` and the slot count.

Generation samples hit count first, then sample slots without replacement. Positive feedback reinforces the played slot/count choices; negative feedback discourages them. Neutral feedback only applies configured decay. Weight bounds and numerically stable softmax are applied. This model learns position-specific preferences; it does not yet learn transitions between steps or generalize a pattern to a different grid.

The previous Generate handler recreated the engine and lost training. Generate now retains the active branch. A session fixes sample-slot count, bars, beats/bar, steps/beat, extra-note limit, and learning configuration. Sample audio is interchangeable: replacing slot 1 changes the sound associated with row 1, not the learned rhythm. Reordering slots intentionally changes that mapping. A mismatched sample count blocks generation and feedback without deleting the model.

New sessions begin on first Generate. Bars, beats/bar, steps/beat and extra-note limit lock because they define model dimensions. Reset clears the model and unlocks structure while retaining the sample kit. Load restores all saved settings and immediately locks only those structural controls. Temperature, learning rate, decay and weight bounds remain adjustable; the latest values are saved. Tightening bounds clamps existing weights across all branches. BPM can change when idle and applies to the next render; mutation affects the next remix. A different slot count requires Reset or a compatible model. The session iteration counter counts completed feedback updates (including neutral ratings); the active-branch counter counts its own updates. Total iterations survive trimming branches and save/load. Forks start with zero own iterations while inheriting weights.

## Samples and audition

Thread count remains adjustable during a session and its UI maximum is `Environment.ProcessorCount`. Loading on a smaller machine clamps the saved count to the available processors. It changes batch scheduling, not model dimensions or learned weights, and is included in saved settings. An already-running batch retains its captured concurrency limit; subsequent batches use the new value.

Drag samples from an Audio Collection into the sample list to **copy** them. Hold **Shift** while dropping onto a row to replace consecutive slots instead. The source collection is untouched. Remove and Move slot up/down are available from the sample context menu. `Play current pattern` re-renders the current rhythm with the current kit. Rating an already-rated stored pattern is disabled in the UI and rejected by the engine; generate a new candidate to continue feedback training.

## Training tree

`Training tree...` opens a branch tree and weight/pattern inspector. Branch entries contain generated pattern records with creation time, model revision, and feedback. Select a branch to edit sample logits or hit-count/rest logits. Select a pattern to inspect its complete step/slot matrix and audition it with the current kit.

- Fork/remix a **branch**: independently copy current weights and add bounded uniform mutation.
- Fork a **pattern**: seed a new branch from that exact rhythm. This does not claim to reconstruct historical weights.
- Rename a branch or trim a branch and its descendants. Trimming removes those branches; it does not undo feedback in surviving ancestors.
- `Use / audition` activates a branch and generates a new candidate, or plays the selected historical pattern.

The model stores current branch weights, every generated pattern, ratings and revision/edit counters. It does not store a separate full weight matrix for every past edit. Use zero-mutation forks as explicit weight checkpoints before experimentation.

## Save / load

`.nbtrain` is a versioned ZIP containing readable `model.json` and optionally `samples/NNN.f32` lossless little-endian float PCM. It includes all branch ancestry, current weights, generated patterns, feedback, active branch, grid/configuration, BPM, mutation setting and ordered slot descriptors.

Save and Load start in the user's Music folder under `ModularAudience/_NeuralBeatTraining`, which is created automatically. The archive also restores loop-playback/loop-feedback preferences and both iteration counters. Only structural parameters lock after loading; compatible learning/playback settings remain adjustable.

Without embedded samples, loading retains the current kit and waits for its count to match. With embedded samples, loading restores the saved kit. Audio is not needed to resume the learned model: any replacement kit with the same ordered slot count can be used.

Saving uses a temporary file then replacement. Loading validates the complete model and optional audio before replacing the current session. Invalid dimensions, non-finite/out-of-range weights, invalid ancestry/cycles, unsupported versions and excessive archive sizes are rejected. Cancellation of feedback does not commit partial weight changes.

Limits: 64 slots, 4096 steps, 131072 step/slot cells per model; 2048 branches; validated archives additionally bound aggregate matrix/history and embedded PCM size. Large grids or deeply populated history can consume substantial memory. The tree grid scrolls to expose all steps and slots. Audio rendering is limited to 512 MiB per beat.
