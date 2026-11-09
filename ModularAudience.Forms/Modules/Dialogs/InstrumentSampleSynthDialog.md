# Instrument Sample Synthesizer

Open **+ Track → Sample Synthesizer → Instrument family** directly below Model Waveform.
Each family opens a normal modeless window. Other application windows stay interactive.
The initial height fits that family's wrapped parameter controls without vertical scrolling,
up to the current screen's working-area height. The window remains resizable; reducing its
height or width enables parameter scrolling again, without automatically resizing it back.

## Timing and preview

- BPM and Key Duration specify the gate length of **one note**, not a sequence.
- Durations run from 1/64 through 4 Bars (4/4); Straight, Dotted and Triplet timing are supported.
- Attack/Decay/Sustain control the held sound; Release adds a separate tail to the output sample.
- Key Note is a DomainUpDown from C0 through C8. Pitch is -16…+16 semitones in 0.25 steps.
- Prehear plays the generated sample; Loop repeats it. Parameter changes update the sample
  asynchronously and refresh the waveform; current-format preview switches to the new audio.
- Prehear on change (ms) defaults to 200. Changes restart the delay; playback starts once
  both the delay and current render are complete. At 0 the number is gray and automatic
  prehear is disabled. Manual Prehear remains available; opening the window does not autoplay.
  The delay is shared across all nine families, synchronized between open windows and
  restored from `%APPDATA%\ModularAudience\SampleSynthesizer\settings.json`.
- Create adds a named AudioObj to this window's family collection without closing the editor.
  Names use `Instrument · Key · Key Duration [BPM]` (for example `Sub Anchor · G#2 · 1/4 [120]`);
  repeated names get a numeric suffix. Closed output collections are recreated on the next Create.
- Seed fixes noise/strike variation; New variation changes it. Render formats include mono/stereo
  at 22.05, 44.1, 48 and 96 kHz. Changing format stops the old preview so it cannot play at the wrong rate.

## Families

| Family | Presets | Synthesis |
| --- | --- | --- |
| Keyboard | Concert Grand, Felt Upright, Bright Honky-Tonk, Tine Electric, Reed Electric, Glass FM Keys, Toy Piano, Dark Velvet | Decaying inharmonic partials, partial detuning, strike noise and electric-key FM components. |
| Drum Set | Eight kits below, each with fourteen drums | Swept membrane tones, noise snares/claps/shakers, inharmonic metal partials, transient and kit-specific colour. |
| Woodwinds | Concert Flute, Breathy Alto Flute, Clarinet, Oboe, Bassoon, Pan Pipes, Tenor Sax | Harmonic profiles, odd-harmonic emphasis, body formants, breath and delayed vibrato. |
| Brass | Trumpet, Muted Trumpet, French Horn, Trombone, Tuba, Brass Section | Dynamic harmonic attack, formant weighting, saturation, detuned section and vibrato. |
| Plucked Strings | Nylon Guitar, Steel Guitar, Harp, Koto, Muted Bass, Banjo | Karplus–Strong-style feedback strings, pluck position, damping and body resonance. |
| Bowed Strings | Violin, Viola, Cello, Contrabass, Chamber Ensemble, Glass Strings | Bow-position harmonic notches, body formants, bow noise, slow attack and vibrato/unison. |
| Mallets & Bells | Marimba, Vibraphone, Xylophone, Glockenspiel, Tubular Bells, Steel Pan | Instrument-specific modal ratios, damping and impact hardness. |
| Organ | Jazz Drawbar, Rock Tonewheel, Church Principal, Reed Organ, Flute Stops, Cathedral Mixture | Weighted harmonic stops, percussion, leakage noise, drive and rotary-style stereo movement. |
| Synth Bass & Lead | Sub Anchor, Acid Resonator, Reese Bass, Rubber Pluck, Silk Lead, Hard Sync Lead, FM Growl, Dream Unison | PolyBLEP saw/pulse components, sub/FM voices, resonant low-pass and filter envelope. |

These are procedural instrument-inspired sounds, not sampled acoustic recordings.
Brightness, body, noise, drive, cutoff/Q, filter envelope, room, width and output level
are editable. Family-specific controls include strike/bow position, percussion level and drum pitch sweep.

## Drum kits

- **808 Deep**: deep sine membranes, long bass decay and restrained transient.
- **909 Punch**: shorter, sharper sweep and pronounced attack/noise.
- **Beach Wood**: dry wooden resonances, short tails and softer metallic content.
- **Industrial Forge**: harsh noise, strong drive and metallic overtones.
- **Acoustic Studio**: balanced membrane/noise blend and moderate natural-style decay.
- **Lo-Fi Dust**: coarse quantization, dusty noise and softened metal.
- **Electro Glass**: high inharmonic metallic content and exaggerated pitch movement.
- **Tribal Earth**: deep resonant drums with extra body modes and longer tails.

Every kit offers Kick, BassDrum, Snare, Rimshot, Clap, ClosedHiHat, OpenHiHat,
LowTom, MidTom, HighTom, Ride, Crash, Cowbell and Shaker.
Created drums carry their matching DrumsetElement tag for Breakbeat Generator use.

## Saved parameter presets

Each family has its own Saved presets ComboBox and **Save preset**, **Load preset**,
**Rename preset** and **Delete preset** buttons. Enter a name to save a new configuration;
saving an existing name updates it. To rename, select a preset, edit the name field and
click Rename preset. Loading restores the instrument/keyboard type, drum set/type,
key note, duration/timing, render format, loop checkbox and every synthesis parameter.
Loading a sound preserves the shared prehear-delay preference.

Files live in `%APPDATA%\ModularAudience\SampleSynthesizer\Presets\`:
`Keyboard.json`, `DrumSet.json`, `Woodwinds.json`, `Brass.json`, `PluckedStrings.json`,
`BowedStrings.json`, `MalletsAndBells.json`, `Organ.json`, `SynthBassAndLead.json`.
Each document has a version, family-specific schema and family identifier. Foreign-family
files/configurations are rejected. Instrument names are saved alongside their indices
so catalog reordering does not silently select a different voice. Writes are atomic.
