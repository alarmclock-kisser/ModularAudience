# Application updates

The GitHub workflow publishes `ModularAudience.Forms.exe` with the existing
`release-<run number>` tag. The same number is embedded in the executable, so
the file name and existing shortcuts do not change.

Numbered, single-file releases check the latest GitHub release at startup.
When a newer release is available, the dialog offers **Update now**, **Not now**
and **Don't ask again at startup**. The update is downloaded with progress and
its size and SHA256 are verified before the application closes. A separate
installer verifies the release number and files again, waits for the application
to exit, then replaces the executable at its existing path. The installer
reports completion; start the application again using the same shortcut.

Update preferences are stored in
`%APPDATA%\ModularAudience\update-settings.json`:

```json
{
  "AskToUpdate": true,
  "CurrentReleaseNumber": 127
}
```

Checking **Don't ask again at startup** saves `AskToUpdate: false` even when
the dialog is closed without updating. Right-click the WindowMain background
and toggle **Check for Updates at Startup** to change the setting, or edit
`AskToUpdate` in the JSON file. The setting applies to subsequent startup
checks. `CurrentReleaseNumber` is informational: the running
executable's embedded number is authoritative and updates the JSON on startup.
Local or non-single-file development builds do not check for releases or
replace themselves.

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

Right-click **Import** to host or join a co-op session. Right-click a collection's
track list and check **Share Collection** to synchronize additions and edits
between the host and all connected clients, including clients joining later.
See [the co-op documentation](../ModularAudience.CoOp/README.md) for retention,
firewall/UAC behavior, networking limits, and security considerations.
