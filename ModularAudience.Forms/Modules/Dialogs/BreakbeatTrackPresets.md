# Track Generate presets

Available in the Pattern Editor's Generate and per-track Auto-Reroll options.
`- None -` (default), or intensity 0%, runs the original generator unchanged.
Selecting a preset loads editable starting values; changing preset intensity never
overrides pitch permissions, note-kind permissions, density or value ranges.
Preset choice, intensity and phrase length travel with copied track options, undo
and Generate settings. Phrase length is a musical cycle inside the existing editor,
not a change to the editor's total bars. The last bar contains an answer/fill.

At 100% intensity the preset's rhythmic template, accents, duration shape and pitch
contour dominate; at lower intensity these blend toward random placement/dynamics.
Density 0% remains silent. Templates adapt to the grid: fine resolutions allow
extra retriggers in JungleRoller, RatchetPressure and GlitchShards. Longer source
samples can suppress adjacent hits when neither trimming nor stretching is allowed.
Pitch chance/range apply to explicit pitch; varispeed also changes pitch inherently.
The motif length/repeat options correlate placement decisions while retaining each
bar's template. Seed fixes a manual generation; Auto-Reroll advances variations.

| Preset | Default phrase | Intended sound |
| --- | --- | --- |
| AmenSnareLadder | 4 bars | Backbeats and soft pickups, with a rising minor-flavoured pitched snare ladder. Best on snare. |
| JungleRoller | 4 bars | Syncopated doubles, descending pitch rolls, dense fine-grid retriggers in the final bar. |
| FunkGhostPocket | 2 bars | Airy funk pocket: firm backbeats, quiet ghost notes, displaced answer. |
| DnBTwoStep | 4 bars | Dry, stable two-step anchors; delayed second kick-like hit and brief pickup. Pitch disabled. |
| HalfTimeTitan | 2 bars | Wide half-time holes, heavy middle slam, long low-pitched/varispeed replies. |
| RaggaOffbeat | 2 bars | Clipped offbeat skanks with high/low pitch alternation. |
| EuclidFive | 4 bars | Five attacks distributed across sixteen cells; the last bar rotates their phase. |
| TresilloChop | 2 bars | Repeated 3-3-2 rhythm with tight cuts and an answering flourish. |
| FootworkSwitch | 4 bars | Angular doubles and sudden gaps; alternating low/high pitch jumps. |
| RatchetPressure | 4 bars | Quarter-note skeleton tightens into shorter gates and rapid final-bar retriggers. |
| TapeDive | 4 bars | Increasingly long varispeed notes with a downward pitch staircase; tape-stop-like, not a continuous glide. |
| ElasticOrbit | 2 bars | Alternating long/short time-stretched syncopations without pitch shifts. |
| GlitchShards | 4 bars | Tiny cuts and irregular bursts, quarter-tone pitch jumps and alternating allowed note kinds. |
| DubEchoSteps | 2 bars | Loud lead hit followed by softer, progressively lower retriggers. Echo illusion, no delay effect. |
| TribalTomSpiral | 4 bars | Rotating three-step cycle with a pentatonic-flavoured up/down contour. Best on toms. |
| NeuroMachine | 4 bars | Mechanical accents and bursts, alternating stretch/varispeed; answer reverses pitch direction. |

The per-track options window is owned/modeless: only its Pattern Editor is disabled.
Other application windows remain interactive. OK/Apply for all commits, Cancel or
closing discards edits; closing the owner closes the options window with it.
