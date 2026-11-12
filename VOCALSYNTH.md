# VocalSynth – Implementation & Requirements

This document captures the current implementation of the **Vocalsynth** feature
(Text-to-Speech / Vocaloid-style vocal synthesizer) so that further work
(e.g. by other Copilot/ChatGPT passes) can continue cleanly.

## 1. User-facing requirements (original idea)

- Entry point: **WindowMain → "+ Track" button right-click context menu →
  top-level, bottom-most item "Vocalsynth …"** opens the dialog.
- Highly interactive & intuitive but highly customizable.
- Selectable languages: English, German, French, Spanish, Italian, Chinese,
  Japanese, Turkish, Arabic, and Russian. The independent engine has a
  language-specific pronunciation path for each; quality varies by language.
- **Animal Crossing** is an additional playful, non-linguistic pronunciation
  mode. Its voice color is still selected independently through the voice preset
  or a custom profile.
- **Beatboxing** interprets a compact sequence of vocal percussion symbols.
- Interactive text input field; **line-by-line rich text editor** (multiline and editable).
- Defined speech punctuation markers that modulate speech:
  `-`, `~`, `+`, `...`, `#`, `*`, `_` and friends. Markers can be
  **interpolating / wrapping** (e.g. `*strongly pronounced*`,
  `~whispered~`) or **inline** (pauses, lengthening).
- Configurable in a deeper dialog, **JSON-persistent**, editable interactively:
  strongly pronounced vs. whispered, pauses, pitch up/down, lengthening.
- NumericUpDown for **Speed**.
- **9 hard-coded voice presets**: Child, Young Female, Adult Female, Adult Male,
  Deep Male, Robot, Whisper, Elderly, and Singing.
- Buttons: **Prehear / Stop** and **Create** (adds rendered audio to a new
  AudioCollectionView). Audio is rendered automatically after edits or on demand.
- **Ctrl+Z / Ctrl+Y** undo and redo speech-text edits from anywhere in the
  dialog. **Shift+Enter** toggles Prehear/Stop in the speech text box; elsewhere,
  **Enter** or **Shift+Enter** does so. Enter in the speech text box inserts a
  new line.
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
  VocalEmphasisMode.cs          Pause/Strong/Whisper/Lengthen/PitchUp/PitchDown/Muted
  VocalEmphasisRule.cs          marker rules and syntax collection
  VocalEmphasisSyntaxStore.cs   AppData JSON load/save
  VocalVoicePreset.cs           built-in profiles, including Singing
  VocalVoiceProfile.cs          user profile (JSON-serializable) + FromPreset/ToPreset
  VocalVoiceProfileStore.cs     AppData folder, one JSON per profile
  VocalPhoneme.cs               acoustic descriptor
  VocalPhonemeInventory.cs      shared acoustic phonemes and game sounds
  VocalPhonemizer.cs            pronunciation entry point and EN/DE rules
  VocalMultilingualPhonemizer.cs language dispatch and shared pronunciation metadata
  VocalIpa.cs                   IPA to acoustic inventory
  VocalLatinLanguages.cs        Italian and Turkish spelling rules
  VocalChinese.cs               Mandarin Hanzi and Pinyin, with lexical tones
  VocalJapanese.cs              Japanese dictionary readings, kana, and romaji
  VocalArabic.cs                Arabic script and Latin transliteration
  VocalRussian.cs               Russian Cyrillic and Latin transliteration
  VocalTextParser.cs            text + emphasis markers -> synthesis tokens
  VocalToken.cs                 pauses, phonemes, text and emphasis
  VocalSynthSettings.cs         settings (language, text, speed, voice, MIDI, emphasis)
  VocalSynth.cs                 local formant renderer -> AudioObj
  VocalMidiSinging.cs           MIDI track timing, chord grouping and syllable mapping
  VocalWindowsSpeech.cs         optional local Windows voice -> AudioObj
  VocalSinging.cs               generated melody, syllable grouping and Windows pitch processing

ModularAudience.Forms/Modules/Dialogs/
  VocalSynthDialog.cs                 main dialog (merged Prehear/Stop button,
                                      empty-text disables buttons)
  VocalSynthDialog.Designer.cs        Windows speech and MIDI/syllable controls
  VocalEmphasisSyntaxDialog.cs         emphasis marker editor
  VocalVoiceProfileDesignerDialog.cs   interactive voice color profile designer

ModularAudience.Forms/Modules/Partials/WindowMain/
  WindowMain.VocalSynth.partial.cs   CreateVocalSynthMenu()

ModularAudience.Forms/WindowMain.Designer.cs
  contextMenuStrip_newTrack.Items.Add(CreateVocalSynthMenu());  // bottom-most
```

Language phonemizers can set `SourceRepetitionLength` on pronunciation phones:
`0` means unknown, `2` means a double letter, and `3` means three or more.
Use `CappedRepetitionLength` when a grapheme consumes part of a longer run,
so expressive repetitions remain separate in the shared renderer.

## 3. Current speech engines and behavior

- **Natural speech (Windows)** is available but unchecked by default in the
  dialog for English and German. It uses an installed Windows desktop speech voice locally; no
  cloud service is used. If a matching voice is missing, the dialog reports an
  error rather than silently switching engines. Install a matching Windows voice
  or leave the option unchecked to use the independent formant synthesizer. The API's
  `UseWindowsSpeech` setting defaults to `false` for existing callers.
- Windows voice profiles are **approximations**, not separate recorded voices:
  the engine chooses an available voice, uses its supported pitch levels and
  applies additional pitch and color processing. Exact timbre, whispering, and
  the effect of individual emphasis parameters depend on the installed voice;
  whispered text in this mode is made quieter rather than truly whispered.
- The independent synthesizer uses English/German pronunciation rules and a
  small English exception list. It distinguishes moving diphthongs, English
  `ch` and German `ch`, voiced/unvoiced consonants, and German final devoicing.
  German uses a lightly fluttering, fricated uvular consonantal R, a short
  vocalic R glide after vowels, and a more open vocalic R in unstressed `-er`.
  Words such as `der` and `her` retain their full E before the glide.
  A distinct unrounded schwa is used for final
  `-e` and common unstressed `-en`/`-em` endings; monosyllabic `den`/`dem`
  and a small set of stressed endings retain a full E. The `be-` prefix before
  `sch` is unstressed, as in `Beschaffenheit`. Vowels before double
  consonants, `ck`, and `tz` are shortened. Long O and U are recognized in
  selected open syllables and before `h` or `ß`; `st` after `ver` begins with
  the German `sh` sound, and an initial `ver-` is left unstressed when a root
  vowel follows. Common `-egel` roots use a long E and reduced `-el`, and
  `mir`, `dir`, `wir`, and `ihr` use a long I.
  These spelling heuristics are not
  a complete German pronunciation dictionary and regional R variants differ.
  The exception for `intrigued` uses the stressed `ee` sound followed by `g d`;
  Windows speech receives an equivalent local SAPI pronunciation.
  Its output is still synthetic and its pronunciation rules are not a complete
  dictionary.
- The other languages use the same local formant renderer and voice profiles:
  - **French**: local rule-based French-to-IPA conversion, including accents,
    nasal vowels and a uvular R. Liaison across separate words is limited.
  - **Spanish**: Latin American rule-based IPA, including `ñ`, accent-driven
    stress, and distinct tapped and trilled R sounds.
  - **Italian**: spelling rules for soft/hard C and G, `gn`, `gli`, doubled
    consonants and written stress accents. Unmarked stress is approximate.
  - **Mandarin Chinese**: locally bundled Hanzi/phrase dictionaries select
    Pinyin readings and apply common tone sandhi within a continuous Hanzi
    segment. Pinyin may use tone marks or numbers, for example `nǐ hǎo` or
    `ni3hao3`. Tones 1–4 modulate pitch; tone 5 and unmarked Pinyin use a
    neutral contour, while explicitly marked neutral syllables stay separate.
    Tone sandhi is not inferred across separately typed Pinyin words.
  - **Japanese**: the bundled Kuromoji dictionary resolves common Kanji in
    context; kana and Hepburn-style romaji are also supported. Vowel length is
    approximated, and Japanese pitch accent is not modeled.
  - **Turkish**: distinct `i`/`ı`, `ö`/`ü`, `ç`, `ş` and lengthening `ğ`,
    with a general final-stress heuristic.
  - **Arabic**: Arabic script with common vowel marks and Latin/DMG-style
    transliteration. A small hand-written common-word list supplements the
    letter rules; it is not a full Arabic dictionary. Missing short vowels in
    ordinary unvowelled script cannot be recovered reliably, so pronunciation
    remains approximate.
  - **Russian**: Cyrillic and common Latin transliterations, including `ё`,
    basic palatalization, final devoicing, and approximate unstressed `о`
    reduction. An acute accent can explicitly mark stress; otherwise a small
    exception list and a general heuristic are used.
- French, Spanish and Mandarin use the Apache-2.0 DotNetG2P packages, and
  Japanese uses the Apache-2.0 Lucene.NET Kuromoji package (currently a beta
  release), whose bundled IPADIC dictionary has its own included notice.
  Their dictionaries are bundled locally; no cloud service or AI
  model is needed. Their license and attribution files are copied to
  `ThirdPartyLicenses` in build and publish output and are attached to releases
  as `VocalSynthLicenses.zip`. The Japanese package adds
  about 22 MB to the NuGet download, and Mandarin adds about 3.4 MB.
- The multilingual rules and small formant inventory are best-effort
  approximations, not complete pronunciation dictionaries or natural speech.
  IPA enclosed in `/slashes/` or `[brackets]`, and unambiguous IPA symbols, are
  rendered through the shared phone inventory regardless of the selected
  language. If a language rule or dictionary cannot pronounce part of mixed
  input, known scripts are attempted with their corresponding local rules;
  unsupported sounds are skipped by default, or rendered as a 100 Hz censor
  tone when **100 Hz beep for unsupported sounds** is enabled. Thus one
  incompatible fragment does not suppress the rest of the text. Unbroken
  segments are limited to 4096 characters; long Hanzi segments are processed
  in smaller chunks.
- The formant renderer maintains glottal phase between voiced sounds, blends
  neighboring formants, shapes stops and fricatives separately, and retains
  higher harmonics for lower-pitched voices. Voice presets, speed, and
  emphasis remain applicable to this mode.
- **Animal Crossing** uses short, character-derived pitched syllables instead
  of linguistic pronunciation. Both `...` and `…` produce three distinct
  percussive vocal pops with increasing gaps; this takes precedence over a
  configured ellipsis marker in this mode. Speed scales the pops and gaps.
  These sounds are generated locally and are not samples from a game.
- **Beatboxing** uses locally synthesized mouth sounds on 250 ms eighth-note
  steps (120 BPM at speed 1). `B`/`b` is a voiced kick, `K`/`k` a snare-like
  sound, `P`/`p` a lip burst, `T`/`t` a short hi-hat, `TS`/`ts` a longer
  hi-hat, `PF`/`pf` a lip snare, `TR` a short tongue roll, `TCH` a softer
  hi-hat, and `Z` a voiced buzz. `BZ` and `KZ` combine a kick or snare with a
  buzz; `S`, `F`, `H` are sustained mouth noises. Whitespace, apostrophes
  (`'` or `’`), and `|` separate symbols without adding time; `.` and `-` each
  insert one silent step. For example, `B t K t B ts pf .` is two bars of
  eighth notes. Speed changes the tempo,   uppercase symbols are accented, and
  voice profiles and wrapping emphasis markers still apply. A configured
  apostrophe emphasis marker takes precedence over its use as a separator.
  Unknown characters are approximated as the closest available mouth sound,
  while recognized notation retains its existing sound and timing.
- **Singing** is a voice preset usable with every pronunciation mode. It
  generates a simple deterministic melody rather than interpreting musical
  notation. The formant engine sustains vowels on successive notes; Windows
  speech is slowed and locally pitch-tracked and retuned while unvoiced
  consonants remain mostly unprocessed. Saved profiles derived from Singing
  retain this behavior. Mandarin tones are softened while singing so the
  generated melody remains recognizable.
- **MIDI singing**: load a `.mid` or `.midi` file from the user's Music folder.
  Loading selects the Singing voice; the track selector shows the current
  page/name and the Edit button opens the MIDI inspector/editor on that page.
  Same-tick notes form a chord for one vocal syllable. MIDI onset, duration,
  tempo and gaps set the vocal timeline; explicit punctuation/emphasis pauses
  remain supported, but lyric line breaks do not add audible pauses. Text filled
  from MIDI keeps its commas and line breaks for display without shifting note
  timing. MIDI note velocity controls the rendered level, and overlapping notes are
  mixed as simultaneous voices. Extra text syllables reuse the last MIDI chord,
  while excess MIDI note groups are left silent and reported after rendering;
  they do not extend the rendered audio past the final sung syllable.
  The **Semitone offset** control transposes MIDI notes by -16…+16 semitones in
  0.25-semitone steps. **Speed** scales MIDI note durations and the resulting BPM;
  1.00 preserves the MIDI tempo (for example, 120 BPM stays 120 BPM). **Show
  syllables** underlines every second syllable in the same editable text field,
  whether or not a MIDI file is loaded. **Fill Syllables** is available for a
  selected Singing MIDI track; it appends only the missing syllables, counting
  same-tick chords once. Its language-appropriate vowel color follows the MIDI
  pitches, and repeatable vowels are lengthened up to eight characters based on
  note duration. Rest gaps and the MIDI time signature guide comma and line
  formatting without adding extra time in the aligned MIDI render. The first
  MIDI time signature is used when formatting measures.
  Word-internal boundaries use matching written vowel nuclei and keep grapheme
  clusters intact, with a deterministic fallback where orthography is ambiguous;
  they remain a display approximation, not linguistic syllabification. MIDI singing also works with Natural speech
  (Windows), where syllables are rendered locally, time-fitted to notes and pitch-retuned.
- **Expressive repetition**: three or more consecutive copies of a sustainable
  letter, such as `Klenkerrrrrr`, `hissssss` or `coooool`, sustain its sound.
  Ordinary double letters retain their normal pronunciation. The formant
  engine sustains a double vowel continuously instead of restarting the
  sound; three or more remain expressive repetitions. Compatible adjacent
  vowel sounds from other language phonemizers also share this behavior.
  Separate Mandarin tones and the independently articulated Animal Crossing
  syllables remain distinct. The independent
  engine gives repeated `r` a trill; Windows speech uses repeated SAPI phones
  in the selected voice. In Windows speech the deliberately extended portion
  is a separate phonetic fragment, so neighboring pronunciation may change.
- **CAPS emphasis**: words with at least two uppercase letters are louder, even
  when followed by a question mark; a single uppercase `I` or `A` is not
  treated as a shout. Explicit whisper markers remain quieter than normal
  speech, including for uppercase words. Beatbox symbols may be single-letter
  uppercase accents.
- **Whitespace and punctuation**: in the formant engine, spaces create
  proportional pauses, newlines create a 350 ms pause, and sentence
  punctuation adds pauses. Windows speech retains normal single spaces and
  punctuation for its own prosody; explicit markers and longer whitespace
  still create breaks.
- Apostrophes within words are retained for pronunciation, including French
  contractions and Pinyin or romaji syllable separators. Configured emphasis
  markers still take precedence over punctuation inside words.
- Question and exclamation marks affect emphasis and, in Windows speech, are
  also passed through as punctuation.
- **Empty text** disables Create and Prehear; the merged Prehear/Stop
  button remains enabled to stop any current playback.
- **Emphasis markers**: wrapping markers apply their mode to inner text;
  inline markers insert pauses / set pending emphasis for the next word.
  `Muted` wrapping markers silence their enclosed text; inline `Muted` markers
  silence through the end of the current line. Markers must contain printable
  Unicode punctuation or symbols (not letters, digits or whitespace), and
  multi-character markers match as exact substrings. An unmatched wrapping
  marker is ignored instead of blocking the parser.
- Rendering is cancellable and rejects invalid settings or output exceeding
  30 million interleaved samples. Windows speech also limits text to 100,000
  characters and accepts speeds from 0.5 to 4.

## 4. Extension points / TODOs

- Improve the independent synthesizer further with larger pronunciation
  dictionaries, richer stress/prosody and perceptual listening tests. Its
  rule-based output should not be presented as natural speech.
- Add per-word/line emphasis in the UI (currently text markers only).
- Wire `SetCollection` to the active AudioCollectionView instead of creating a
  new one (mirror Sample Synthesizer if desired).
- Add more tests for pause proportionality, question-mark pitch and profile
  rename/delete round trips when extending those behaviors.
- Consider exposing `VocalSynth` as an AudioObj generator for arrangement tracks.

## 5. Tests

`ModularAudience.Audio.Tests/VocalSynthTests.cs` covers pronunciation,
multilingual scripts, transliteration, Mandarin tones, formant/fricative spectra,
voiced transitions, cancellation and size limits,
animal voice presets, timed ellipsis pops, expressive letter repetition, CAPS,
language-independent IPA, mixed-script best-effort fallback, beatboxing,
generated and MIDI singing (including chords, text pauses, lyric line breaks,
syllable filling, note-count mismatch, and Windows speech), muted emphasis markers, multi-character marker parsing,
Windows SSML and actual Windows voice output when matching voices are
installed. The fixed-timing singing tests specifically use Microsoft Zira
Desktop and Hedda Desktop; other Windows voice tests accept any matching
installed voice. No playback device or GPU is required.

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