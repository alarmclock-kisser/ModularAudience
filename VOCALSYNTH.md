# VocalSynth – Implementation & Requirements

This document captures the current implementation of the **Vocalsynth** feature
(Text-to-Speech / Vocaloid-style vocal synthesizer) so that further work
(e.g. by other Copilot/ChatGPT passes) can continue cleanly.

## 1. User-facing requirements (original idea)

- Entry point: **WindowMain → "+ Track" button right-click context menu →
  top-level, bottom-most item "Vocalsynth …"** opens the dialog.
- Highly interactive & intuitive but highly customizable.
- Selectable languages: English, German, French, Spanish, Italian, Chinese,
  Japanese, Turkish, Arabic, Russian, … (the independent engine has
  language-specific **English + German** rules; others use the English fallback).
- **Animal Crossing** is an additional playful, non-linguistic pronunciation
  mode. Its voice color is still selected independently through the voice preset
  or a custom profile.
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
  VocalEmphasisRule.cs          marker rules and syntax collection
  VocalEmphasisSyntaxStore.cs   AppData JSON load/save
  VocalVoicePreset.cs           8 built-in profiles
  VocalVoiceProfile.cs          user profile (JSON-serializable) + FromPreset/ToPreset
  VocalVoiceProfileStore.cs     AppData folder, one JSON per profile
  VocalPhoneme.cs               acoustic descriptor
  VocalPhonemeInventory.cs      phonemes, moving vowels and animal-style syllables
  VocalPhonemizer.cs            language grapheme->phoneme (EN/DE + fallback)
  VocalTextParser.cs            text + emphasis markers -> synthesis tokens
  VocalToken.cs                 pauses, phonemes, text and emphasis
  VocalSynthSettings.cs         settings (language, text, speed, voice, format, emphasis)
  VocalSynth.cs                 local formant renderer -> AudioObj
  VocalWindowsSpeech.cs         optional local Windows voice -> AudioObj

ModularAudience.Forms/Modules/Dialogs/
  VocalSynthDialog.cs                 main dialog (merged Prehear/Stop button,
                                      empty-text disables buttons)
  VocalSynthDialog.Designer.cs        Windows speech option
  VocalEmphasisSyntaxDialog.cs         emphasis marker editor
  VocalVoiceProfileDesignerDialog.cs   interactive voice color profile designer

ModularAudience.Forms/Modules/Partials/WindowMain/
  WindowMain.VocalSynth.partial.cs   CreateVocalSynthMenu()

ModularAudience.Forms/WindowMain.Designer.cs
  contextMenuStrip_newTrack.Items.Add(CreateVocalSynthMenu());  // bottom-most
```

## 3. Current speech engines and behavior

- **Natural speech (Windows)** is enabled by default in the dialog for English
  and German. It uses an installed Windows desktop speech voice locally; no
  cloud service is used. If a matching voice is missing, the dialog reports an
  error rather than silently switching engines. Install a matching Windows voice
  or uncheck the option to use the independent formant synthesizer. The API's
  `UseWindowsSpeech` setting defaults to `false` for existing callers.
- Windows voice profiles are **approximations**, not separate recorded voices:
  the engine chooses an available voice, uses its supported pitch levels and
  applies additional pitch and color processing. Exact timbre, whispering, and
  the effect of individual emphasis parameters depend on the installed voice;
  whispered text in this mode is made quieter rather than truly whispered.
- The independent synthesizer uses English/German pronunciation rules and a
  small English exception list. It distinguishes moving diphthongs, English
  `ch` and German `ch`, voiced/unvoiced consonants, and German final devoicing.
  Its output is still synthetic and its pronunciation rules are not a complete
  dictionary. Other spoken languages continue to use the English fallback.
- The formant renderer maintains glottal phase between voiced sounds, blends
  neighboring formants, shapes stops and fricatives separately, and retains
  higher harmonics for lower-pitched voices. Voice presets, speed, and
  emphasis remain applicable to this mode.
- **Animal Crossing** uses short, character-derived pitched syllables instead
  of linguistic pronunciation. Both `...` and `…` produce three distinct
  percussive vocal pops with increasing gaps; this takes precedence over a
  configured ellipsis marker in this mode. Speed scales the pops and gaps.
  These sounds are generated locally and are not samples from a game.
- **Whitespace and punctuation**: in the formant engine, spaces create
  proportional pauses, newlines create a 350 ms pause, and sentence
  punctuation adds pauses. Windows speech retains normal single spaces and
  punctuation for its own prosody; explicit markers and longer whitespace
  still create breaks.
- Apostrophes within words are retained for English pronunciation exceptions.
- Question and exclamation marks affect emphasis and, in Windows speech, are
  also passed through as punctuation.
- **Empty text** disables Render/Prehear/Create buttons; Prehear and Stop are
  merged into one toggling button.
- **Emphasis markers**: wrapping markers apply their mode to inner text;
  inline markers insert pauses / set pending emphasis for the next word. An
  unmatched wrapping marker is ignored instead of blocking the parser.
- Rendering is cancellable and rejects invalid settings or output exceeding
  30 million interleaved samples. Windows speech also limits text to 100,000
  characters and accepts speeds from 0.5 to 4.

## 4. Extension points / TODOs

- Add full phonemization for French, Spanish, Italian, Chinese, Japanese,
  Turkish, Arabic, Russian (currently fall back to English). Implement in
  `VocalPhonemizer` with language-specific digraphs/umlauts. Keep the
  `VocalLanguage` enum and `Phonemize` signature stable.
- Improve the independent synthesizer further with a larger pronunciation
  dictionary, richer stress/prosody and perceptual listening tests. Its
  rule-based output should not be presented as natural speech.
- Add per-word/line emphasis in the UI (currently text markers only).
- Wire `SetCollection` to the active AudioCollectionView instead of creating a
  new one (mirror Sample Synthesizer if desired).
- Add more tests: all 8 presets render, all languages fall back without throwing,
  pause proportionality, question-mark pitch, profile rename/delete round trips.
- Consider exposing `VocalSynth` as an AudioObj generator for arrangement tracks.

## 5. Tests

`ModularAudience.Audio.Tests/VocalSynthTests.cs` covers pronunciation,
formant/fricative spectra, voiced transitions, cancellation and size limits,
all animal voice presets, timed ellipsis pops, Windows SSML and actual Windows
voice output when an English voice is installed, plus the existing settings
and parser regression cases. No playback device or GPU is required.

Run:
```
dotnet test ModularAudience.Audio.Tests\ModularAudience.Audio.Tests.csproj --filter "FullyQualifiedName~VocalSynthTests"
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