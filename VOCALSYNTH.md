# VocalSynth – Implementation & Requirements

This document captures the current implementation of the **Vocalsynth** feature
(Text-to-Speech / Vocaloid-style vocal synthesizer) so that further work
(e.g. by other Copilot/ChatGPT passes) can continue cleanly.

## 1. User-facing requirements (original idea)

- Entry point: **WindowMain → "+ Track" button right-click context menu →
  top-level, bottom-most item "Vocalsynth …"** opens the dialog.
- Highly interactive & intuitive but highly customizable.
- Selectable languages: English, German, French, Spanish, Italian, Chinese,
  Japanese, Turkish, Arabic, Russian, … (UI lists all; engine fully implements
  **English + German** first; others fall back to a generic Latin mapper).
- Interactive text input field; **line-by-line text editor** (multiline textbox).
- Defined speech punctuation markers that modulate speech:
  `-`, `~`, `+`, `...`, `#`, `*`, `_` and friends. Markers can be
  **interpolating / wrapping** (e.g. `*strongly pronounced*`,
  `~whispered~`) or **inline** (pauses, lengthening).
- Configurable in a deeper dialog, **JSON-persistent**, editable interactively:
  strongly pronounced vs. whispered, pauses, pitch up/down, lengthening.
- NumericUpDown for **Speed**.
- **8 hard-coded voice presets** (distinct color profiles): Child, Young Female,
  Adult Female, Adult Male, Deep Male, Robot, Whisper, Elderly.
- Buttons: **Render**, **Prehear / Stop**, **Create** (adds rendered audio to a
  new AudioCollectionView).
- User voice profiles: **savable / loadable / deletable / renamable** in AppData
  JSON, with an editor dialog.
- **All UI strings in English.**
- Language-specific pronunciation: vowels/umlauts differ by language; known
  clusters like German "sch" must sound like "sh", not "s-k-h".
- Only default UTF-8 charset for pronounceable characters + emphasis markers.
- Emphasis syntax configurable in a separate dialog (JSON persistent).
- Many meaningful **MSTest unit + integration/regression tests**.

## 2. Architecture

```
ModularAudience.Audio/Vocal/
  VocalLanguage.cs              enum + display names + IsFullyImplemented
  VocalEmphasisMode.cs          Pause/Strong/Whisper/Lengthen/PitchUp/PitchDown
  VocalEmphasisRule.cs          marker, mode, value, wrapping
  VocalEmphasisSyntax.cs        collection + Default() + clone
  VocalEmphasisSyntaxStore.cs   AppData JSON load/save
  VocalVoicePreset.cs           8 built-in profiles
  VocalVoiceProfile.cs          user profile (JSON-serializable) + FromPreset/ToPreset
  VocalVoiceProfileStore.cs     AppData folder, one JSON per profile
  VocalPhoneme.cs               acoustic descriptor
  VocalPhonemeInventory.cs      shared phoneme table (formants, voicing, noise)
  VocalPhonemizer.cs            language grapheme->phoneme (EN/DE + fallback)
  VocalTextParser.cs            text + emphasis markers -> VocalToken list
  VocalToken.cs                 VocalPauseToken / VocalPhonemeToken + VocalEmphasis
  VocalSynthSettings.cs         settings (language, text, speed, voice, format, emphasis)
  VocalSynth.cs                 renderer -> AudioObj

ModularAudience.Forms/Modules/Dialogs/
  VocalSynthDialog.cs                 main dialog (merged Prehear/Stop button,
                                      empty-text disables buttons)
  VocalEmphasisSyntaxDialog.cs         emphasis marker editor
  VocalVoiceProfileDesignerDialog.cs   interactive voice color profile designer

ModularAudience.Forms/Modules/Partials/WindowMain/
  WindowMain.VocalSynth.partial.cs   CreateVocalSynthMenu()

ModularAudience.Forms/WindowMain.Designer.cs
  contextMenuStrip_newTrack.Items.Add(CreateVocalSynthMenu());  // bottom-most
```

## 3. Key behaviors implemented

- **Whitespace → pauses**: a run of spaces produces a pause proportional to the
  number of spaces (`60ms * count` ... actually `70ms * count`, min 60ms).
  Newlines/paragraphs produce a ~350ms pause. Multiple consecutive spaces →
  longer pause.
- **Apostrophes** (e.g. "I'm") are skipped by the phonemizer.
- **Question marks** raise pitch on the word's phonemes; **exclamation marks**
  strengthen them.
- **Silent trailing 'e'** in English (e.g. "more", "name", "time") is
  skipped so it does not sound like an extra "ee".
- **Empty text** disables Render/Prehear/Create buttons; Prehear and Stop are
  merged into one toggling button.
- **Apostrophes** (e.g. "I'm") are skipped by the phonemizer, so "I'm" is
  spoken as "im" without being treated as an emphasis marker.
- **Question marks** raise pitch (`VocalEmphasis.PitchUp`) on the word's
  phonemes; **exclamation marks** strengthen them.
- **Emphasis markers**: wrapping markers apply their mode to inner text;
  inline markers insert pauses / set pending emphasis for the next word.
- **Speed** scales both phoneme durations and pause durations.
- **Rendering** uses additive harmonics with spectral tilt + formant boosts +
  gentle smoothing to reduce the harsh "square-wave" quality (still improvable).

## 4. Extension points / TODOs

- Add full phonemization for French, Spanish, Italian, Chinese, Japanese,
  Turkish, Arabic, Russian (currently fall back to English). Implement in
  `VocalPhonemizer` with language-specific digraphs/umlauts. Keep the
  `VocalLanguage` enum and `Phonemize` signature stable.
- Improve TTS quality: proper source-filter formant synthesis (Klatt-style),
  coarticulation, prosody (F0 contour, stress), better consonant shaping,
  breath noise, glottal pulse model.
- Add per-word/line emphasis in the UI (currently text markers only).
- Wire `SetCollection` to the active AudioCollectionView instead of creating a
  new one (mirror Sample Synthesizer if desired).
- Add more tests: all 8 presets render, all languages fall back without throwing,
  pause proportionality, question-mark pitch, profile rename/delete round trips.
- Consider exposing `VocalSynth` as an AudioObj generator for arrangement tracks.

## 5. Tests

`ModularAudience.Audio.Tests/VocalSynthTests.cs` (MSTest):
- Render produces non-empty audio (EN/DE)
- German "sch" → "sh" (not s-k-h)
- Speed affects duration
- Wrapping strong produces strong tokens
- Pause marker produces pause token
- Voice profile JSON round trip
- Emphasis syntax JSON round trip
- Whitespace/question behavior (add when extending)

Run:
```
dotnet test ModularAudience.Audio.Tests/ModularAudience.Audio.Tests.csproj --filter "FullyQualifiedName~Vocal"
```

## 6. Notes for future agents

- Keep **all UI strings in English**.
- Use `VocalVoiceProfileStore.Default()` / `VocalEmphasisSyntaxStore.Default()`
  for AppData paths; do not hard-code OS-specific paths elsewhere.
- `AudioCollectionView.AudioC` is internal; dialogs in the same assembly can
  access it.
- `ISampleProvider.Read` uses `Span<float>` in this NAudio version.
- Prefer adding tests alongside new behavior; run the filtered test suite before
  finishing.