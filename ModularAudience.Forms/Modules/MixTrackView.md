# Mix-TrackView / modular audio arrangement

## Joining windows

- AudioCollectionView → **Open as Multitrack View (N)** opens the selected tracks in list order,
  each on its own lane at time zero. The first track sets the mix BPM. For later tracks with
  differing known BPM, all TimeStretchDialog settings are collected sequentially before any
  of those tracks are stretched. Their target is the first track's mix BPM; bag audio stays
  untouched. Cancel leaves the first lane and discards the uncommitted remainder.
- Ordinary TrackView's **Sync Playback** is now labeled **Sync** (its existing behavior remains).
- **Join** copies the audio into a non-destructive arrangement and hides the original TrackView.
- Right-click **Join** to create a new mix, choose an existing mix, or open a project.
- The first audio track sets an empty mix's BPM from its known source BPM, without a tempo prompt.
  This applies to Join and audio drops. Unknown BPM retains the current grid tempo.
- Double-click the title bar to rename the mix/project; use the maximize button to maximize.
- Click a track name in the left header to edit it inline. New tracks use the next unused
  `Track #N` name; names are saved with the `.mamix` project. Renaming a mix updates its
  title and project metadata; saving under a different filename adopts that filename.
- When source and grid BPM differ, choose **Time-stretch**, **Varispeed**, or **Keep original tempo**.
Unknown source BPM keeps the original tempo; set it later through the clip's context menu.
- Mix context menu → **Remove Track** → select a track: the original TrackView returns.
  Removing an imported/project track opens a standalone TrackView for its first source.
- Undo/Redo also reconciles joined-window visibility. On **closing the mix**, a joined
  TrackView is restored only if its original AudioCollectionView still exists and
  still contains its source track. Orphaned hidden TrackViews are closed, not reopened.
  Explicitly detached/exported tracks remain independent windows.

## Arrangement editing

- Every lane contains independently bounded audio clips. Original sample arrays are never edited.
- Drop one/multiple AudioCollectionView items into lanes or the empty area to add clips/tracks.
- **+ Track** (or context menu **Add empty track**) creates an empty lane. Double-click empty
  lane space to select an existing source and insert it at that time; Shift / Snap applies.
- Drag a clip's **top title handle** to move it; drop on another lane to move between tracks.
- Clips can move **before time zero**, freely or with Shift/Snap on signed grid steps.
  The timeline can scroll left of zero and pans left while dragging across its edge.
  Full-mix playback/export starts at zero: material before zero is cropped without
  changing the clip's stored start/offset or restarting its fade. Explicit selection
  rendering/effects can still include the negative portion.
- Drag the left/right ends of that title handle to trim or reveal source audio without deleting the source.
- Drag **below the title handle** (waveform or empty lane space) to select a time range across
  any number of lanes. The selected range is highlighted, including intersecting segments.
  Right-click → **Set loop to selection**, **Delete selected audio range**, or
  **Render selected range → Collection**. Delete cuts only the selected audio region and keeps
  left/right remainders as independent non-destructive clips. Escape clears selection.
- A normal drag selects a rectangular time range, so copy/paste and delete can use partial
  clips. Hold **Ctrl** while dragging to keep the same rectangular gesture, but magnetically
  extend each affected track's left/right edge to the boundaries of clips intersected by the
  original rectangle. The outline only steps at track boundaries; it does not trace the
  pointer path or cascade onto neighboring clips. Every intersected clip is selected whole
  for copy/paste and delete, including when the rectangle touched only part of it.
- Drag a track name or the remaining header area vertically to reorder complete lanes; a
  click without dragging edits the name, and a line marks the reorder insertion point.
- Each track header has a **0–200% volume slider** beside Solo/Mute. Its thumb is
  repainted during the drag. Audio mixing is coalesced on background workers, never
  performed synchronously in each mouse event; superseded updates are cancelled and
  never partially overwrite the audible buffer. One completed gain gesture creates
  one Undo entry. Track reordering changes project metadata, not audio history.
  Gain above 200% remains available in properties.
- **Shift** snaps to the nearest grid boundary. **Snap** keeps snapping enabled permanently.
- Grid BPM: 10–400; divisions: 1/1 through 1/64 (whole-note denominator, 4/4 bars).
- Click the ruler to seek. Drag across it to set the loop range; enable **Loop** to repeat.
- Right-click clips for exact start, source offset/duration, gain, fades, and tempo mode.
- Right-click a track for gain, multiple-solo / mute, automatic overlap crossfades, reorder, and removal.
- Overlapping clips mix together. Output peaks above 0.98 are uniformly attenuated for headroom.
- Source channels stay intact inside the project; display shows up to eight channel bands.
  The shared preview/export is stereo: mono duplicates, stereo stays stereo, larger channel layouts
  fold to left/right by parity. It is not a surround-device routing editor.
- Initial lane height is 100 px. Window resizing scales lanes together for the current
  view; this navigation gesture is not saved or undoable. Adding lanes preserves their
  current height by growing the window when screen space permits; when **Auto-adjust
  track height** is on and the screen is full, lanes are fitted without showing a vertical
  scrollbar. A track height explicitly accepted in **View Settings** is saved and restored.
- The mouse wheel scrolls the time viewport horizontally; Ctrl+wheel zooms. Vertical track
  scrolling is only available from the right scrollbar when **Auto-adjust track height** is off.
  That scrollbar is hidden while auto-adjust is on. The bottom scrollbar also moves time.
- The blue dashed audio-drop outline is a temporary placement preview, not a stored clip or
  historical selection; it clears when the drop completes or the pointer leaves the view.
- The three-state checkbox cycles **No Scroll → Auto-Scroll → Auto-Skip**. Auto-Scroll follows
  playback with the caret near the left quarter of the viewport. Auto-Skip keeps a page stationary
  until the caret reaches its right edge, then advances a page so the caret reappears at the left.
  Both handle loop wrap; manual editing gestures temporarily suspend automatic viewport movement.
  Scroll mode and current viewport are runtime navigation only: no history or autosave writes.
- Context menu **Place source audio here** reuses an existing source on an empty track position.
- **Fit arrangement horizontally** shows the whole arrangement.

## Audio effect context menu

There are now two distinct timeline context menus:

- **No selection + empty space/track header**: the general mix/track menu (view
  settings, gain/solo/mute, track removal/reordering, adding/placing sources, paste,
  loop/fit/rename and undo/redo).
- **Existing selection or an individually right-clicked segment**: the audio menu,
  with **Apply Effect ...** and exactly the same nine groups / 37 effects as ordinary
  TrackView. Existing range/whole-clip multiselections are not collapsed by a right
  click, including a right click in empty space. Clear selection or Escape returns
  to the general menu. Without a selection, the clicked clip becomes the target.

The modeless parameter dialogs, typed Auto Tune editor, source/target BPM helpers,
factory/user presets, Save/Load/Rename/Delete controls, RAM prehear/bypass/loop,
worker budget and cancellation are shared with TrackView. Presets therefore work
in both editors without duplicate stores. Convert to mono and Reverse are direct
actions without dialogs.

Time & Speed contains **Time Stretch ...** followed by **Varispeed Shift ...**.
Time Stretch's Apply Effect window is the new `TimeStretchEffectDialog`, separate
from the unchanged legacy/batch dialog. Varispeed deliberately couples pitch and
speed. Both support independent parameter curves and Source/Target BPM helpers.

All meaningful continuous effect options (including typed Auto Tune) have independent
**Edit Curve ...** controls/toggles. The shared English curve editor offers equations,
checkpoints, live plotting, oscillators and global shape-only JSON presets. Save is
explicit; OK only activates the shape. Timing can restart per segment or use one
selection-wide source-time curve, optionally skipping gap lengths and/or sustained
silence. Simultaneous lanes share the union of selected time; overlapping clips are
not counted twice. Empty selected lanes use the selected interval as their region.
Skipping silence changes the curve clock, not the captured audio interval. Shared
clock analysis is cancellable, off the UI thread and cached for Apply. Bounce recipes
persist curve assignments/clock geometry for project and tempo round trips.
Nested curve windows join this mix's existing window group (blocking, minimize/restore,
ownership and close), without disabling unrelated windows. See `../Effects/README.md`
for shape syntax, preset storage and intentionally fixed/unsupported options.

## Mix-local dialog scope

Effects, stretch/tempo questions, properties, view settings, prompts and clipboard
history use **mix-local modality**, not WinForms' UI-thread-wide `ShowDialog`.
An active dialog blocks the parent mix's client controls and lower dialogs in that
same group. Other TrackViews, collections, WindowMain and other mixes stay usable.
The parent mix itself remains enabled, so its title bar **Minimize** button still
works. Minimizing/restoring it hides/restores all its open configuration windows,
including nested/native dialogs, with their parameters intact. Child dialogs have
no independent taskbar/minimize trap. Closing the mix cancels/closes its group.

Native Open/Save/Color dialogs run on a separate STA with a mix-owned proxy HWND;
their native modal loop cannot disable every window on the application's UI thread.
Rendered AudioCollections and explicitly detached TrackViews are independent editing
results, not blocking configuration dialogs.

Effects capture selected audio when opened, not when accepted. A rectangle selects
its **exact entire time interval on every selected lane**, including leading,
interior and trailing silence and empty selected lanes. Whole-clip multiselection
uses the common first-start/last-end time window, retaining silence between the
selected clips. Unselected overlapping/intervening clips are not fed to the effect
and remain separate. Each selected lane is processed independently with the same
parameters; it is not flattened together with other tracks. Mono and multichannel
layouts are retained in the lane buffers; the audition bus is stereo, at the mix's
44.1 kHz clock, and includes lane gain while ignoring mute/solo exclusion so the
selected material can actually be heard. Audition headroom scaling does not change
the committed per-lane samples.

Applying creates new immutable Float32 assets and a rendered replacement clip per
selected lane. Clip gain, fades, tempo mode and overlap envelopes are baked into
the captured audio once; lane gain/solo/mute stay editable. Partial boundary clips
keep independently bounded left/right remainder clips. Their original full-clip
envelopes/phase context are rendered before cropping, so an effect in the middle
of a long fade does not restart or shorten the fade outside the selection. For
auto-crossfading lanes, existing clip envelopes in that lane are frozen as well;
`HasRenderedEnvelope` prevents a second automatic crossfade on already-bounced
audio. The lane's Auto Crossfade setting remains enabled for newly placed clips.

Duration changes **ripple following clips only on affected lanes**, preserving
untouched lanes. The longest processed lane defines the common inserted time
window; shorter processed lanes leave silence up to its end. Shared loop/caret
positions map around that window. Same-length effects do not move following clips.
The original source assets are never edited or discarded and remain available to
the existing **Ctrl+Z / Ctrl+Y** history. A batch across several lanes creates
**one** history step; preview, presets, cancellation and bit-identical bypasses
create none. New assets and rendered-envelope metadata are persisted in `.mamix`.

Modeless sessions reject stale mix/clip/source edits rather than overwriting work
performed while a dialog was open. Changing a selection programmatically does not
retarget that frozen window; normal mix interaction is blocked by its active dialog.
Closing the effect or mix cancels rendering and
stops preview. Capture/render buffers have explicit memory bounds; a very large
clip may need to be split/trimmed before applying a small region effect because
its full phase/envelope context is retained for correct boundary audio.

## Mix View Settings

**View Settings** in the toolbar/context menu opens the mix-specific equivalent of TrackView's
view settings. It previews changes live; Cancel restores them, Apply saves the accepted
settings without adding audio-history entries (an accepted audio tempo change remains undoable).
Options include uniform lane height, horizontal zoom, grid BPM/division, snap, grid/waveform
visibility, per-track hue coloring and a hue shift. Color properties (background, headers, grid,
clips/selection, waveform, text, playhead and loop selection) have HEX input, preview swatches and
Windows color pickers. These explicit options are embedded in `.mamix`. Undoing an
audio edit leaves the current view settings intact. Toolbar/wheel zoom and scrolling
do not overwrite the explicitly saved View Settings zoom.
**Hue cycle** animates clip colors at **Hue speed (degrees / sec)**, independently of playback.
Negative speed reverses the direction; zero freezes it. The static Hue shift remains the base
offset. Animation does not change audio or create history entries; the animation timer is released
when the mix window closes.

## Shortcuts and history

| Input | Action |
|---|---|
| Ctrl+Z / Ctrl+Y | Undo / Redo |
| Ctrl+C / Ctrl+V | Copy clip / paste at playhead |
| Ctrl+D | Duplicate immediately after selected clip |
| S | Split selected clip at playhead |
| Delete | Remove selected clip |
| Space | Play / pause-stop at current position |
| Ctrl+S / Ctrl+O | Save / open project |
| Escape while dragging | Cancel the current gesture |

History includes audio placement/content edits, gain/mute/solo/fades, audio tempo
changes, joins and removal of audio. A gain/clip drag gesture is one entry. Audio
history stores field-level deltas, not entire UI snapshots: undo/redo preserves later
view settings, snap preferences, track order, labels and navigation. Empty-track
creation, track ordering, names, snap/grid preferences and view changes do not add
audio-history entries; meaningful project metadata can still be saved. Project open
starts a new audio history. Play/stop, caret/loop/scroll states, manual navigation and
interactive zoom add no history, do not invalidate redo and do not enqueue autosave.

## Audio and projects

All lanes are rendered into one shared audio stream, so there is no independent-player drift.
Transforms run on bounded background workers. Replaying an unchanged arrangement reuses its
render; edits immediately schedule background preparation, including while stopped. Preparation
is serialized and superseded requests are cancelled, reusing unchanged pre-rendered unity-gain
lanes. Play consumes prepared audio (or waits for preparation already in progress); it does not
start a fresh effect pass on every press. Gain/mute/solo use these lane buffers directly; dragging
volume requests a background buffer update without restarting transport or repeating DSP. An expensive
edit still needs to finish computing before its new sound is available.
Time-stretch is implemented by resampling to target duration and compensating pitch using the
application's pitch-preserving phase-vocoder path. Varispeed changes duration and pitch together.
For **Time-stretch on Join/import**, the existing **TimeStretchDialog** opens in configuration
mode with source BPM and mix target BPM prefilled. Chunk size, overlap, worker count and V1/V2
choices are applied immediately during import. The imported adjusted source needs no further
tempo transformation at Play. The clip tempo mode menu remains available for later changes.

### Grid tempo changes and pristine origins

Every tempo-matched Join/drop retains an immutable **pre-import PCM asset and its
original sample rate, channels and BPM**. Prepared sources reference that asset with
`TempoOriginId` and a coordinate scale. Changing the grid with re-stretch accepted
resolves these origins and renders from them, never from the preceding stretched
version. Source offsets/durations of trimmed/split clips map through original
coordinates; arrangement positions and fades follow the old-grid/new-grid ratio.
Unknown source tempo uses the initial fallback once and remembers it thereafter.
Returning to a raw source's original tempo reuses its exact original samples.

An effect/envelope bounce also retains a **rendering recipe** and its source
dependencies. New grid renders replay those operations from pristine upstream audio,
including frozen full-clip envelopes, rather than stretching an already-stretched
effect bounce. Recipe data and transitive original assets are embedded in `.mamix`,
so this remains true after saving/reopening and does not depend on original disk files.
Old projects without origins/recipes cannot reconstruct missing pre-import audio;
their earliest available asset becomes a stable baseline, avoiding further cumulative
generations. Reimport the actual original to recover that older project's pristine base.

The BPM entered in the mix's **TimeStretchDialog** is authoritative: changing its
prefilled target changes both the rendering target and the resulting grid BPM.
That configuration dialog uses the grid's supported 10–400 BPM bounds. Cancellation
keeps the previous tempo/audio. Configured scalar V1/V2 routines receive isolated
channels, with frame-aligned target lengths and linked peak matching, not scrambled
interleaved stereo. A grid-only change on original-mode clips does not repeat DSP.

**Save project** writes `.mamix`, the single-file journal described below, with JSON
metadata and embedded Float32 PCM assets. Save/Open start in
`<configured ModularAudience working directory>\_Projects`, created if needed.
WindowMain's **Import** right-click menu also offers **Open Project (.mamix)** above
**Host co-op**, separated from the co-op actions. Its file picker filters `.mamix`
and opens the selected project in a new MixTrackView; cancellation leaves no empty mix.
Projects are independent of original file paths/TrackViews. **Render → Collection** creates an AudioObj
which can use the existing collection export workflow. The project keeps all sources used by undo
history for the lifetime of the window; closing releases its owned buffers and waveform cache.
# Clipboard, selection and rendering

- Ctrl+C copies the current audio-range or whole-clip multiselection. History holds 50
  deduplicated entries; recopying moves an entry to the top.
- Tap Ctrl+V for a ghost preview at the pointer. Left-click places it, Esc/right-click cancels.
  Hold Ctrl+V for 250 ms for clipboard history with waveform thumbnails. Up/Down selects;
  Enter or click accepts and moves the entry to the top. Track/time offsets are retained.
- Shift rectangle selection snaps to the grid. Ctrl rectangle selection selects entire
  touched clips and adds to the existing selection. Ctrl-click adds a whole clip.
- Render ... left-click renders the mix. Right-click offers Mix (one track), Separate
  (per track), Render Track # and Selection (N). Selected ranges are cropped; whole
  selected clips retain their complete audio. Explicit track renders ignore mute/solo
  exclusion while retaining track gain and clip processing.
- Backspace jumps to the last manually placed ruler caret; Ctrl+Backspace resets that
  anchor and playback position to zero.

# Project journal and Auto-save

New .mamix projects use the MAMIXJ02 single-file append-only journal. Existing old ZIP
projects are not compatible. Float32 audio assets are written once per immutable source
ID. Further edits append only arrangement metadata snapshots; new audio sources append
their PCM once. No repeated encoding/compression or full PCM scan occurs for existing
assets. Metadata writes are coalesced with a 300 ms debounce and serialized off the UI
thread. Undo/redo also schedules a save. The journal keeps prior records, so metadata
history grows over time. Interrupted trailing records are ignored and truncated on the
next save; the last complete snapshot remains loadable.

Auto-save is unavailable until Save/Open establishes a project path. Its checked state
is part of the project. Changing the checkbox saves that preference to the current file.
Closing flushes pending saves. Snap and explicitly accepted view options (including
saved track height/zoom) are stored. Viewport positions, scroll mode, transport loop,
playback/caret and transient zoom/resize states are omitted, including when a legacy
live snapshot is passed to the writer. Identical project saves append no redundant
metadata record. Adding tracks grows the viewport
when screen space allows; all lanes always share the same configured height, with a
vertical scrollbar for overflow.
