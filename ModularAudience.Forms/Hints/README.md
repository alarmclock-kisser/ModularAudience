# Hover hints and parameter explanations

`Enable ToolTips (hints)` in WindowMain's **background** context menu controls optional
interaction hints app-wide. Its `EnableToolTips` preference defaults to `true` and is
stored in `%AppData%/ModularAudience/application-settings.json`. Existing atomic,
field-specific saves and cross-instance monitoring apply to this option as well.
Full-message hover tooltips on WindowMain's log ListBox are explicitly always on,
so clipped diagnostic entries remain readable regardless of this preference.

## Coverage

- `HintAliases.cs` routes all unqualified `ToolTip` and `ContextMenuStrip` creation
  in this project, including designer code and dynamic menus, through `AppToolTip`
  and `AppContextMenuStrip`. Do not bypass these aliases for new UI hints.
- Automatic tooltips and **all programmatic Show overloads** follow the same flag.
  Disabling dismisses current hints without deleting their text.
- `HintPolicy` watches existing/new form controls, context menus and dynamic
  submenus, plus native ListView/TreeView/TabControl/DataGridView hints. Controls
  whose native tooltips were already disabled keep that state when hints return.
- Floating AudioCollectionView waveform previews and YouTube mini-layout thumbnail
  previews are also optional hints. Turning hints off hides them and prevents
  pending hover work from displaying them again. Inline previews and explicit
  editing/help windows are not hidden.
- `HintContent` documents interactions in WindowMain, TrackView, AudioCollectionView
  and the MIDI editors. Mix timeline, waveform modellers and YouTube have additional
  context-specific descriptions beside their UI construction.

## Always-available parameter help

Set `IsParameterHelp = true` on a tooltip component (or a parameter menu) when it
explains labelled settings rather than covering an editing surface. Such components
stay active with hints off. Numeric explanations include allowed range and step;
an immediately preceding caption label receives the same explanation.
`AlwaysAvailable = true` is the separate exception for full log-message tooltips;
it does not classify diagnostic text as parameter help or rewrite its contents.

Audited always-on contexts include effect/Auto Tune editors, instrument synthesis,
waveform/sample modeller parameters, deterministic separation, neural-beat options,
pattern generation/remix options, LLM generation settings, hotkey/control settings
and YouTube download/network options. Native settings-dialog cell explanations and
PropertyGrid's permanent description pane remain available. Interactive pattern,
MIDI, waveform and mix canvases do **not** acquire this exception just because their
window also contains settings.

Use `HintPolicy.RegisterPopup(owner, dismiss)` for future custom hover windows, and
check `HintPolicy.Enabled` both before starting and after awaiting hover work. The
policy keeps only weak references to live UI components.
