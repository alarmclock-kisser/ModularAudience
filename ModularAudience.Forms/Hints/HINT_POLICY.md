# Hint Policy — Feature vs. Hint

The global hint switch (`Enable ToolTips (hints)` in WindowMain's background menu)
controls **interaction hints only**: tooltips, hover windows, and similar
explanatory UI. It must **not** gate user features.

## Always-on features (not affected by the hint policy)

- **Waveform preview** in `AudioCollectionView`: the `Preview` checkbox is a
  user feature, not a hint. The preview stays available even when hints are
  disabled. `ShowPreview` is controlled solely by `checkBox_preview.Checked`.
- **Full-message hover tooltips** on WindowMain's log ListBox: always on,
  independent of the global flag.
- **Playlist button** current/next-track and queue summary: always available
  via a dedicated tooltip.
- **Parameter help** (`IsParameterHelp = true`): stays active with hints off.

## Hints (follow the global flag)

- All `AppToolTip` and `AppContextMenuStrip` components (routed via
  `HintAliases.cs`).
- Native ListView / TreeView / TabControl / DataGridView cell tooltips.
- YouTube mini-layout thumbnail previews.
- Any custom hover window registered via `HintPolicy.RegisterPopup`.

## Rule

Before adding a new popup or overlay, decide: is this a **hint**
(explains something) or a **feature** (does something the user asked for)?
Only hints follow `HintPolicy.Enabled`.
