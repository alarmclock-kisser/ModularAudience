# Instrument Sample Synthesizer

Open **+ Track → Sample Synthesizer → Instrument family** directly below Model Waveform, or
choose the same family from **+ Instrument** in the Breakbeat Pattern Editor. Each family
opens a normal modeless window. Other application windows stay interactive.
The initial height fits that family's wrapped parameter controls without vertical scrolling,
up to the current screen's working-area height. The window remains resizable; reducing its
height or width enables parameter scrolling again, without automatically resizing it back.

## Timing and preview

- BPM and Key Duration specify the gate length of **one note**, not a sequence.
- Durations run from 1/64 through 4 Bars (4/4); Straight, Dotted and Triplet timing are supported.
- Other Sound FX opens with a 1/2-note gate (one second at 120 BPM) so timed signals such as Morse SOS are audible; choose a shorter duration for quick hits.
- Attack/Decay/Sustain control the held sound; Release adds a separate tail to the output sample.
- Key Note defaults to C4 and is a DomainUpDown with items ordered C8 through C0. Pitch is -16…+16 semitones in 0.25 steps.
  Up/wheel-up lowers the key; Down/wheel-down raises it. Key Duration items are ordered longest to shortest:
  Up/wheel-up lengthens the duration; Down/wheel-down shortens it.
- Prehear plays the generated sample; Loop repeats it. Sound-parameter changes start a cancellable
  background render immediately and refresh the waveform as soon as the latest render is ready.
  The prehear delay affects playback only; current-format preview switches to the new audio.
- Prehear on change (ms) defaults to 200. Changes restart the delay; playback starts once
  both the delay and current render are complete. At 0 the number is gray and automatic
  prehear is disabled. Manual Prehear remains available; opening the window does not autoplay.
  The delay is shared across all twelve families, synchronized between open windows and
  restored from `%APPDATA%\ModularAudience\SampleSynthesizer\settings.json`.
- Press Space outside the preset-name field to restart Prehear. Active prehears in other open
  synthesizer windows stop first; holding Space does not trigger repeated starts.
- Create adds a named AudioObj to this window's family collection without closing the editor.
  Names use `Instrument · Key · Key Duration [BPM]` (for example `Sub Anchor · G#2 · 1/4 [120]`);
  repeated names get a numeric suffix. Closed output collections are recreated on the next Create.
- OK renders the current settings, adds the sample to the family collection, then closes the
  dialog. In the Breakbeat Pattern Editor, OK instead stores the selected family, instrument/drum
  type and sound parameters in the current editor session, then adds a live instrument track. Melodic notes are synthesized at
  their requested pitch; each note uses the editor's BPM and note length as its synthesis tempo
  and key duration. The instrument's release remains an additional tail. Drum notes retain their
  configured drum and add the note pitch offset to that drum's tuning.
- Create Octave renders individual AudioObj notes into a new AudioCollectionView. Right-click
  the button to choose Offset (0/base, +8, +4, -4 or -8 semitones) and Step (2, 1, 0.5, 0.33
  [exactly 1/3], 0.25 or 0.125 semitones). The range includes both the selected starting
  transpose and the endpoint 12 semitones above it. Requests exceeding 512 MiB are rejected
  with guidance instead of silently lowering render quality.
- Seed fixes noise/strike variation; New variation changes it. Render formats include mono/stereo
  at 22.05, 44.1, 48 and 96 kHz. Changing format stops the old preview so it cannot play at the wrong rate.

## Families

| Family | Presets | Synthesis |
| --- | --- | --- |
| Keyboard | Concert Grand, Felt Upright, Bright Honky-Tonk, Tine Electric, Reed Electric, Glass FM Keys, Toy Piano, Dark Velvet | Decaying inharmonic partials, partial detuning, strike noise and electric-key FM components. |
| Drum Set | Nineteen kits below, each with fifteen drums | Swept membrane tones, noise snares/claps/shakers, inharmonic metal partials, transient and kit-specific colour. |
| Woodwinds | Concert Flute, Breathy Alto Flute, Clarinet, Oboe, Bassoon, Pan Pipes, Tenor Sax | Harmonic profiles, odd-harmonic emphasis, body formants, breath and delayed vibrato. |
| Brass | Trumpet, Muted Trumpet, French Horn, Trombone, Tuba, Brass Section | Dynamic harmonic attack, formant weighting, saturation, detuned section and vibrato. |
| Plucked Strings | Nylon Guitar, Steel Guitar, Harp, Koto, Muted Bass, Banjo | Karplus–Strong-style feedback strings, pluck position, damping and body resonance. |
| Bowed Strings | Violin, Viola, Cello, Contrabass, Chamber Ensemble, Glass Strings | Bow-position harmonic notches, body formants, bow noise, slow attack and vibrato/unison. |
| Mallets & Bells | Marimba, Vibraphone, Xylophone, Glockenspiel, Tubular Bells, Steel Pan | Instrument-specific modal ratios, damping and impact hardness. |
| Organ | Jazz Drawbar, Rock Tonewheel, Church Principal, Reed Organ, Flute Stops, Cathedral Mixture | Weighted harmonic stops, percussion, leakage noise, drive and rotary-style stereo movement. |
| Synth Bass & Lead | Sub Anchor, Acid Resonator, Reese Bass, Rubber Pluck, Silk Lead, Hard Sync Lead, FM Growl, Dream Unison, Deep House Bass, Offbeat Bass, Donk Bass, Wobble Bass, Acid 303 Slide, Neuro Bass | PolyBLEP saw/pulse components, sub/FM voices, pitch-scooped basses, resonant low-pass and filter LFO. |
| Synth Pads & Poly | Supersaw Stack, Wide Unison Saw, Warm Analog Pad, Dark Drone Pad, Hoover Rave Lead | Detuned multi-oscillator saw stacks, triangle/sub layers, sustained envelopes and resonant filtering. |
| Synth Keys & Plucks | Digital Pluck, FM Bell, House Techno Stab | Decaying digital partials, inharmonic FM modulation and short saw/pulse stabs. |
| Other Sound FX | 100 Hz Censor Tone, Buzzer, Synthetic Vocal Whoop, 8-Bit Coin, 8-Bit Laser, 8-Bit Power-Up, 8-Bit Game Over, DJ Scratch, Morse SOS, Sonar Ping, Boat Foghorn, Alarm Siren, Radio Static | Procedural one-shots made from oscillators, formant-weighted harmonics, pitch sweeps and seeded noise; no source recordings. |

All instruments and effects are generated procedurally, not from recordings. The synthetic vocal whoop is an original oscillator/formant sound.
Brightness, body, noise, drive, cutoff/Q, filter envelope, room, width and output level
are editable. Family-specific controls include strike/bow position, percussion level and drum pitch sweep.
The Acid 303 Slide preset performs a short per-note glide into its target pitch; it does not depend on
the previous note. Chord stabs are likewise single-note timbres and can be arranged as chords in the editor.

## Drum kits

- **808 Deep**: deep sine membranes, long bass decay and restrained transient.
- **909 Punch**: shorter, sharper sweep and pronounced attack/noise.
- **Beach Wood**: dry wooden resonances, short tails and softer metallic content.
- **Industrial Forge**: harsh noise, strong drive and metallic overtones.
- **Acoustic Studio**: balanced membrane/noise blend and moderate natural-style decay.
- **Lo-Fi Dust**: coarse quantization, dusty noise and softened metal.
- **Electro Glass**: high inharmonic metallic content and exaggerated pitch movement.
- **Tribal Earth**: deep resonant drums with extra body modes and longer tails.
- **Minimal Techno**: short, dry drums with restrained noise and metallic content.
- **Electro Breakbeat**: punchy sweeps, sharper transients and a brighter noise/metal blend.
- **Hard Techno**: stronger drive, sharp attacks and brighter metallic tails.
- **Jungle Break**: punchy, snappy kit with a light break-style stutter.
- **Breakcore Crunch**: aggressive drive, bit-crushing and rapid retriggered hits.
- **Ragga Jungle**: warmer, weightier drums with a rougher snare/noise blend.
- **Glitch DnB**: metallic, heavily crushed and tightly stuttered hits.
- **Chiptune DnB**: reduced-bit percussion with short, pitched attacks.
- **Liquid DnB**: softer transients, restrained noise and smoother tails.
- **Neurofunk**: dense metallic content, sharper sweeps and controlled drive.
- **Jump-Up**: bright, punchy attacks with a bouncy, slightly crushed edge.

Every kit offers Kick, BassDrum, Snare, Rimshot, Clap, ClosedHiHat, OpenHiHat,
LowTom, MidTom, HighTom, Ride, Crash, Cowbell, Shaker and PedalHiHat.
PedalHiHat carries the closed-hi-hat DrumsetElement tag for Breakbeat Generator use.
Genre kits alter individual drum hits; they do not generate break loops or use sampled breaks.

## Saved parameter presets

Each family has its own Saved presets ComboBox and **Save preset**, **Load preset**,
**Rename preset** and **Delete preset** buttons. Enter a name to save a new configuration;
saving an existing name updates it. To rename, select a preset, edit the name field and
click Rename preset. Loading restores the exact instrument/keyboard subtype (for example,
Concert Grand), drum set/type,
key note, duration/timing, render format, loop checkbox and every synthesis parameter.
Loading a sound preserves the shared prehear-delay preference.

Files live in `%APPDATA%\ModularAudience\SampleSynthesizer\Presets\`:
`Keyboard.json`, `DrumSet.json`, `Woodwinds.json`, `Brass.json`, `PluckedStrings.json`,
`BowedStrings.json`, `MalletsAndBells.json`, `Organ.json`, `SynthBassAndLead.json`,
`SynthPadsAndPoly.json`, `SynthKeysAndPlucks.json`, `OtherSoundFx.json`.
Each document has a version, family-specific schema and family identifier. Foreign-family
files/configurations are rejected. Instrument names are saved alongside their indices
so catalog reordering does not silently select a different voice. Older presets without the
stable names fall back to their stored indices. Writes are atomic.
