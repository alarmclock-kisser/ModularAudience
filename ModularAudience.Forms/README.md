# Application updates

For waveform editing effects, parameter-dialog conventions, Auto Tune presets,
and **mandatory Ctrl+Z / Ctrl+Y undo/redo**, see [TrackView effects](Effects/README.md).

The GitHub workflow publishes `ModularAudience.Forms.exe` with the existing
`release-<run number>` tag. The same number is embedded in the executable, so
the file name and existing shortcuts do not change.

Numbered, single-file releases check the latest GitHub release at startup.
When a newer, non-skipped release is available, the dialog offers **Update now**,
**Skip this**, **Not now**, and **Don't ask again at startup**. The update is
downloaded with progress and its size and SHA256 are verified before the
application closes. A separate
installer verifies the release number and files again, waits for the application
to exit, then replaces the executable at its existing path and automatically
starts that installed executable. No success/OK dialog or manual shortcut
restart is required. The existing temporary EXE helper handles the hand-off;
no additional batch script is installed. It first starts the application
directly, falling back to Windows shell activation if process creation fails.
Only installation/restart failures show a recovery dialog. Non-fatal settings
or backup-cleanup warnings are logged without interrupting a successful restart.

Update preferences are stored in
`%APPDATA%\ModularAudience\update-settings.json`:

```json
{
  "AskToUpdate": true,
  "CurrentReleaseNumber": 128,
  "SkipReleaseNumber": null
}
```

**Skip this** records the offered release as `SkipReleaseNumber`, closes the
dialog, and leaves `AskToUpdate` enabled. All releases **up to and including**
that number are suppressed on later startups; the GitHub metadata check still
runs so a strictly newer release can be offered. For example, skipping release
129 suppresses 129 and earlier releases, but release 130 is offered normally.
**Not now** only dismisses the current prompt and does not persist a skip.
Missing `SkipReleaseNumber` in older settings files is treated as `null`.

Checking **Don't ask again at startup** saves `AskToUpdate: false` even when
the dialog is closed without updating. Right-click the WindowMain background
and toggle **Check for Updates at Startup** to change the setting, or edit
`AskToUpdate` in the JSON file. The setting applies to subsequent startup
checks. Switching `AskToUpdate` off or on through the application resets
`SkipReleaseNumber` to `null`; turning checks off and back on therefore enables
offers for previously skipped releases again. The global opt-out checkbox and
**Skip this** are alternative choices: skipping is disabled while the opt-out
checkbox is checked or an update download is active.

`CurrentReleaseNumber` is informational: the running
executable's embedded number is authoritative and updates the JSON on startup.
The initial bottom-left label also uses that embedded number, e.g. **Build: 128**,
instead of the EXE's download/replacement timestamp. Unnumbered local builds
retain the timestamp fallback. Playlist activity can still replace this label
with the current queue information.
Local or non-single-file development builds do not check for releases or
replace themselves.

### Commit messages in the update dialog

The resizable update dialog includes a read-only text box with horizontal and
vertical scrolling. It lists the complete triggering commit message for each
published release **after the installed build and up to and including the
offered build**, ordered by build number from oldest to newest. Message bodies,
build numbers, commit identifiers, and available UTC timestamps are retained.
Previously skipped intermediate releases are included when updating to a later
release; the skip boundary controls prompting, not which changes are shown.
The history also includes up to the 24 most recent published releases before
the update range, including the installed build. The dialog initially scrolls
to the changes since the installed build; scroll up to read those older notes.
Use **Word wrap** to toggle line wrapping in the notes; wrapping hides the
horizontal scrollbar, while the default unwrapped view keeps it available.

History loads asynchronously when the dialog opens. Published `release-N` tags
are matched to `run_number: N` in `.github/workflows/build.yml`; missing workflow
metadata falls back to the corresponding release tag's commit, never the moving
`master` branch. New releases explicitly pin their tag to the workflow's source
SHA. Responses are size-limited and paginated, with a 20-second dialog timeout
and bounded fallback requests. Unavailable or incomplete history is indicated
inside the text box, without blocking **Update now**, **Skip this**, or dismissal.
Closing the dialog or starting installation cancels pending history requests.
These GitHub metadata requests are Internet traffic, separate from co-op LAN
synchronization.

The executable's directory must be writable; no elevation is requested. Close
other instances if Windows reports that the executable is in use. A failed
download or verification does not replace the original; installation errors
are shown and the original or its backup is retained. The SHA256 is supplied by
GitHub and checks download integrity, not a publisher signature. Temporary
installer copies are cleaned on a later startup after they are at least one
hour old.

Releases published before the updater was added cannot update themselves.
Install the first release containing the updater manually; later releases can
then be installed through the dialog.

## Co-op collections

Right-click **Import** to host or join a co-op session. There is one manual
**Connect to address...** action; selecting a discovered host in the combo box
connects directly, without a second menu click. Right-click a collection's
track list and check **Share Collection** to synchronize additions and edits
between the host and all connected clients, including clients joining later.
See [the co-op documentation](../ModularAudience.CoOp/README.md) for retention,
firewall/UAC behavior, networking limits, and security considerations.

## Audio storage and repeated collection exports

The default workspace is the Windows **Music** known folder followed by
`ModularAudience`. Local and received co-op collections both use:

```text
Music\ModularAudience\_Exports
Music\ModularAudience\_Records
```

Windows folder redirection is respected. If the Music directory has not been
created yet, its known-folder path is still used; if Windows has no Music path,
the profile's `Music` directory is used. There is no AppData, Temp, or current
working-directory fallback. Custom workspaces or explicitly chosen destinations
remain supported. Existing files in old locations are not moved or removed.

Exporting multiple samples or using **Shift + Export** reuses the named
collection folder under `_Exports`, instead of creating `Collection_2`,
`Collection_3`, etc. New WAV/MP3 files whose names already exist receive suffixes
such as `Kick (1).wav`, `Kick (2).wav`. File names are claimed atomically so
parallel exports cannot overwrite each other. Explicit single-file saves retain
the existing overwrite-confirmation behavior, and recording finalization can
still replace its explicitly selected recording file.

## Interactive scan targets

**Scan BPM**, **Scan Time**, and **Scan Key** follow the last interacted-with
TrackView or the single selected audio across the collection views. A collection
selection does not require opening a TrackView and takes precedence over an old
TrackView focus. Refocusing that TrackView switches the scan target back to it.
An empty or ambiguous collection selection disables the scan buttons rather
than silently choosing the first item.

The result fields show the current audio's `ScannedBpm`, `ScannedTiming`, and
`ScannedKey` immediately, including cached values. Opening a TrackView preserves
these scan values in its audio copy. A scan that finishes after a focus change
updates the audio it started on, without replacing the newly focused audio's
displayed results.

## Main-window preferences

Export format/bit depth, the main audio directory, and the log list's zoom are
saved immediately to `%APPDATA%\ModularAudience\application-settings.json`:

```json
{
  "ExportFormat": ".wav",
  "ExportBits": 16,
  "WorkingDirectory": null,
  "LogFontSize": 8
}
```

`WorkingDirectory: null` uses the normal Music\ModularAudience default. Selecting
a different directory stores its absolute path exactly as selected; `_Exports`
and `_Records` are created inside it. A normal click on **[...]** opens the
folder picker at the current directory; **Ctrl+click** opens that directory in
Explorer instead. Existing files are not moved. Existing and future collections
that follow the application's default directory, including co-op mirrors, use
the new directory for future exports and recordings. Explicit custom workspaces
are left unchanged, and an already-running recording keeps its existing file.

Changing export format saves a coherent format/bit pair, with WAV defaulting to
16 bit and MP3 to 192 kbit/s when switching formats. A saved valid bit selection
is restored at startup. **Ctrl+mouse wheel** over the log list saves its font size
in points (6–20), without resizing the list itself.

Atomic, mutex-protected field updates avoid losing unrelated preferences when
multiple instances save simultaneously. Open instances monitor this JSON file
and apply changes without feedback-save loops. Unknown JSON fields are retained;
invalid JSON is reported in the log and is not silently overwritten. These
preferences are separate from `update-settings.json`, so changing them does not
reset the updater's opt-out or skipped-release boundary.
