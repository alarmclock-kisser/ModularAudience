# Standalone MIDI Editor

Open the modeless piano roll from **+ Track → Midi Editor**. It owns its MIDI arrangement and can remain open alongside other application windows.

## Editing and navigation

- Left-drag paints notes; right-drag erases notes.
- **Shift + left-drag** selects a time/key rectangle. Remix and Generate use that selection; with no selection they use the whole arrangement.
- **Ctrl + drag** pans and **Ctrl + mouse wheel** zooms horizontally. The mouse wheel scrolls horizontally.
- The Min/Max Key controls update the visible range immediately and always leave at least one octave.
- Resolution is grid divisions per quarter note. BPM and A4 tuning affect preview and rendered audio.
- The instrument button chooses a built-in sound, a sample from an open collection, or an open sample collection. Its **Instrument Synth** submenu opens the same family-based Sample Synthesizer used elsewhere in the app. A configured synth is rendered individually for each MIDI note using that note's MIDI pitch, gate length and the editor BPM; it is not rendered once and then pitch/time-stretched as a static sample.

## Clipboard and history

- **Ctrl+C** copies notes in the selected rectangle; **Ctrl+X** copies and removes them.
- A quick **Ctrl+V** starts a ghost preview of the newest clip. Hold Ctrl+V for more than 250 ms to open the scrollable historical clipboard picker with note thumbnails; Up/Down navigates, Enter or click selects.
- Move the ghost with the pointer and click to add it. Esc or right-click cancels. Remix and Generate also create additive ghost previews; their notes are added to the clipboard history as soon as the preview is created, so cancelling a remix never loses it.
- **Ctrl+Z**, **Ctrl+Y**, and **Ctrl+Shift+Z** undo and redo arrangement edits.

## Preview and save

- Play/Stop previews the arrangement; **Prehear set note** auditions a newly drawn note.
- Click **Save** to render audio into a `CustomMidi` collection named for the BPM and instrument. A matching open collection is reused; changing BPM or instrument selects a different collection. Melody AudioObj names include instrument, subtype and BPM and receive a running suffix when needed to avoid duplicates. Right-click Save and choose **Save as MIDI file...** to export the arrangement as `.mid`.
