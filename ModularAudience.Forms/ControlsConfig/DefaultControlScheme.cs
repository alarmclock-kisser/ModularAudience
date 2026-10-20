using System.Collections.Generic;
using System.Windows.Forms;
using ModularAudience.Forms.ControlsConfig;

namespace ModularAudience.Forms.ControlsConfig
{
    public static class DefaultControlScheme
    {
        public static ControlScheme Create()
        {
            var scheme = new ControlScheme { Name = "Default" };

            // ===== NOTE EDITING =====
            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Note.Draw",
                Implemented = true,
                DisplayName = "Draw note",
                Description = "Empty cell: create a note. Drag horizontally to set its length.",
                Category = ActionCategory.NoteEditing,
                ValidScopes = BindingScope.Grid,
                PrimaryInput = InputType.MouseLeft,
                PrimaryMouseAction = MouseAction.Drag,
                SortOrder = 10
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Note.DrawShort",
                Implemented = true,
                DisplayName = "Short note (1 step)",
                Description = "Empty cell: create a note with a fixed length of 1 step (stretch type follows the track default).",
                Category = ActionCategory.NoteEditing,
                ValidScopes = BindingScope.Grid,
                PrimaryInput = InputType.MouseLeft,
                PrimaryModifiers = ModifierKey.Control,
                PrimaryMouseAction = MouseAction.Click,
                SortOrder = 11
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Note.DrawTimeStretch",
                Implemented = true,
                DisplayName = "Set note as TimeStretch",
                Description = "Empty cell: create a TimeStretch note (pitch stays the same).",
                Category = ActionCategory.NoteEditing,
                ValidScopes = BindingScope.Grid,
                PrimaryInput = InputType.None,
                PrimaryMouseAction = MouseAction.Click,
                SortOrder = 11
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Note.DrawVarispeed",
                Implemented = true,
                DisplayName = "Set note as Varispeed",
                Description = "Empty cell: create a Varispeed note (pitch changes).",
                Category = ActionCategory.NoteEditing,
                ValidScopes = BindingScope.Grid,
                PrimaryInput = InputType.None,
                PrimaryMouseAction = MouseAction.Click,
                SortOrder = 11
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Note.DrawTruncated",
                Implemented = true,
                DisplayName = "Set note as Truncated",
                Description = "Empty cell: create a truncated (hard-cut) note.",
                Category = ActionCategory.NoteEditing,
                ValidScopes = BindingScope.Grid,
                PrimaryInput = InputType.None,
                PrimaryMouseAction = MouseAction.Click,
                SortOrder = 11
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Note.Move",
                Implemented = true,
                DisplayName = "Move note",
                Description = "Grab the note in the middle and drag to move it (horizontally and vertically).",
                Category = ActionCategory.NoteEditing,
                ValidScopes = BindingScope.Note | BindingScope.NoteBody,
                PrimaryInput = InputType.MouseLeft,
                PrimaryMouseAction = MouseAction.Drag,
                SortOrder = 12
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Note.Resize",
                Implemented = true,
                DisplayName = "Resize note (Edge)",
                Description = "Drag the left or right third of a note to change its length.",
                Category = ActionCategory.NoteEditing,
                ValidScopes = BindingScope.Note | BindingScope.NoteEdge,
                PrimaryInput = InputType.MouseLeft,
                PrimaryMouseAction = MouseAction.Drag,
                SortOrder = 13
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Note.ResizeToggleMode",
                Implemented = true,
                DisplayName = "Resize + mode toggle",
                Description = "Hold Ctrl while resizing and release without moving to toggle between TimeStretch and Varispeed.",
                Category = ActionCategory.NoteEditing,
                ValidScopes = BindingScope.Note,
                PrimaryInput = InputType.MouseLeft,
                PrimaryModifiers = ModifierKey.Control,
                PrimaryMouseAction = MouseAction.Drag,
                IsHardcoded = true,
                AllowRemapping = true,
                SortOrder = 14
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Note.HardResize",
                Implemented = true,
                DisplayName = "Hard Resize (Sample Cut)",
                Description = "Cut the note to exactly 1 step and harden the sample.",
                Category = ActionCategory.NoteEditing,
                ValidScopes = BindingScope.Grid | BindingScope.Note,
                PrimaryInput = InputType.MouseLeft,
                PrimaryModifiers = ModifierKey.Control | ModifierKey.Shift,
                PrimaryMouseAction = MouseAction.Drag,
                SortOrder = 15
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Note.Delete",
                Implemented = true,
                DisplayName = "Delete note (single)",
                Description = "Right-click a note to delete it.",
                Category = ActionCategory.NoteEditing,
                ValidScopes = BindingScope.Note,
                PrimaryInput = InputType.MouseRight,
                PrimaryMouseAction = MouseAction.Click,
                SortOrder = 16
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Note.EraseDrag",
                Implemented = true,
                DisplayName = "Eraser Mode (Drag)",
                Description = "Hold and drag the right mouse button to delete every note you touch (custom eraser cursor).",
                Category = ActionCategory.NoteEditing,
                ValidScopes = BindingScope.Grid | BindingScope.Note,
                PrimaryInput = InputType.MouseRight,
                PrimaryMouseAction = MouseAction.Drag,
                IsHardcoded = true,
                AllowRemapping = true,
                SortOrder = 17
            });

            // ===== SELECTION =====
            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Select.Rectangle",
                Implemented = true,
                DisplayName = "Rectangle Selection",
                Description = "Shift + drag on an empty cell: select notes with a rectangle.",
                Category = ActionCategory.Selection,
                ValidScopes = BindingScope.Grid,
                PrimaryInput = InputType.MouseLeft,
                PrimaryModifiers = ModifierKey.Shift,
                PrimaryMouseAction = MouseAction.Drag,
                SortOrder = 20
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Select.GroupResize",
                Implemented = true,
                DisplayName = "Group Resize",
                Description = "Shift + drag a note edge: resize all selected notes by the same amount.",
                Category = ActionCategory.Selection,
                ValidScopes = BindingScope.Note,
                PrimaryInput = InputType.MouseLeft,
                PrimaryModifiers = ModifierKey.Shift,
                PrimaryMouseAction = MouseAction.Drag,
                SortOrder = 21
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Select.CloneDrag",
                Implemented = true,
                DisplayName = "Clone selection (drag)",
                Description = "Alt + drag a selected note: create a copy of the selection.",
                Category = ActionCategory.Selection,
                ValidScopes = BindingScope.Note,
                PrimaryInput = InputType.MouseLeft,
                PrimaryModifiers = ModifierKey.Alt,
                PrimaryMouseAction = MouseAction.Drag,
                SortOrder = 22
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Select.Delete",
                DisplayName = "Delete selection",
                Description = "Delete / Backspace: delete all selected notes.",
                Category = ActionCategory.Selection,
                ValidScopes = BindingScope.Global,
                PrimaryInput = InputType.Key,
                PrimaryKey = Keys.Delete,
                SortOrder = 23
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Select.DeleteAlt",
                DisplayName = "Delete selection (Backspace)",
                Description = "Backspace: alternative key for deleting the selection.",
                Category = ActionCategory.Selection,
                ValidScopes = BindingScope.Global,
                PrimaryInput = InputType.Key,
                PrimaryKey = Keys.Back,
                SortOrder = 24
            });

            // ===== CLIPBOARD =====
            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Clipboard.Copy",
                DisplayName = "Copy",
                Description = "Copy selected notes to clipboard.",
                Category = ActionCategory.Clipboard,
                ValidScopes = BindingScope.Global,
                PrimaryInput = InputType.KeyCombo,
                PrimaryKey = Keys.C,
                PrimaryModifiers = ModifierKey.Control,
                SortOrder = 30
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Clipboard.PastePreview",
                DisplayName = "Paste (preview)",
                Description = "Start paste preview: the copied notes follow the mouse. LMB places them, RMB/Esc cancels.",
                Category = ActionCategory.Clipboard,
                ValidScopes = BindingScope.Global,
                PrimaryInput = InputType.KeyCombo,
                PrimaryKey = Keys.V,
                PrimaryModifiers = ModifierKey.Control,
                SortOrder = 31
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Clipboard.CancelPaste",
                DisplayName = "Cancel Paste Preview",
                Description = "Cancel active paste preview.",
                Category = ActionCategory.Clipboard,
                ValidScopes = BindingScope.PastePreview,
                PrimaryInput = InputType.Key,
                PrimaryKey = Keys.Escape,
                SecondaryInput = InputType.MouseRight,
                SecondaryMouseAction = MouseAction.Click,
                SortOrder = 32
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Clipboard.PlacePaste",
                DisplayName = "Place Paste Preview",
                Description = "Place the paste preview at the current mouse position (only notes starting on the grid).",
                Category = ActionCategory.Clipboard,
                ValidScopes = BindingScope.PastePreview,
                PrimaryInput = InputType.MouseLeft,
                PrimaryMouseAction = MouseAction.Click,
                SortOrder = 33
            });

            // ===== NAVIGATION / VIEW =====
            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "View.Zoom",
                Implemented = true,
                DisplayName = "Zoom (horizontal)",
                Description = "Ctrl + mouse wheel: zoom around the mouse pointer (1×–512×).",
                Category = ActionCategory.View,
                ValidScopes = BindingScope.Global,
                PrimaryInput = InputType.MouseWheel,
                PrimaryModifiers = ModifierKey.Control,
                PrimaryMouseAction = MouseAction.WheelUp,
                IsHardcoded = true,
                AllowRemapping = true,
                SortOrder = 40
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "View.ScrollHorizontal",
                Implemented = true,
                DisplayName = "Horizontal Scroll",
                Description = "Mouse wheel (when zoomed in): scroll horizontally.",
                Category = ActionCategory.View,
                ValidScopes = BindingScope.Global,
                PrimaryInput = InputType.MouseWheel,
                PrimaryMouseAction = MouseAction.WheelUp,
                IsHardcoded = true,
                AllowRemapping = true,
                SortOrder = 41
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "View.ScrollBar",
                Implemented = true,
                DisplayName = "Scroll bar",
                Description = "Drag the bottom scroll bar to scroll horizontally.",
                Category = ActionCategory.View,
                ValidScopes = BindingScope.Global,
                IsHardcoded = true,
                AllowRemapping = true,
                SortOrder = 42
            });

            // ===== PLAYBACK =====
            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Playback.HearToggle",
                DisplayName = "Hear start/stop",
                Description = "Start or stop loop playback of the marked quarters.",
                Category = ActionCategory.Playback,
                ValidScopes = BindingScope.Global,
                PrimaryInput = InputType.Key,
                PrimaryKey = Keys.Space,
                SortOrder = 50
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Playback.PrehearNote",
                Implemented = true,
                DisplayName = "Prehear note (click)",
                Description = "Click a note to play it immediately (regardless of the Pre-hear setting).",
                Category = ActionCategory.Playback,
                ValidScopes = BindingScope.Note,
                PrimaryInput = InputType.MouseLeft,
                PrimaryMouseAction = MouseAction.Click,
                IsHardcoded = true,
                AllowRemapping = true,
                SortOrder = 51
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Playback.PrehearToggle",
                Implemented = true,
                DisplayName = "Auto prehear toggle",
                Description = "Pre-hear checkbox: automatically preview a note after placing or changing it.",
                Category = ActionCategory.Playback,
                ValidScopes = BindingScope.Global,
                IsHardcoded = true,
                AllowRemapping = true,
                SortOrder = 52
            });

            // ===== LOOP CONTROL =====
            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Loop.ToggleBar",
                DisplayName = "Toggle whole bar",
                Description = "LMB on a bar header: select/deselect all four quarters of the bar.",
                Category = ActionCategory.LoopControl,
                ValidScopes = BindingScope.BarHeader,
                PrimaryInput = InputType.MouseLeft,
                PrimaryMouseAction = MouseAction.Click,
                SortOrder = 60
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Loop.ToggleQuarter",
                DisplayName = "Toggle single quarter",
                Description = "RMB on a bar header: select/deselect a single quarter.",
                Category = ActionCategory.LoopControl,
                ValidScopes = BindingScope.BarHeader,
                PrimaryInput = InputType.MouseRight,
                PrimaryMouseAction = MouseAction.Click,
                SortOrder = 61
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Loop.AddBar",
                DisplayName = "Add bar",
                Description = "Plus button or + key: append a bar (max 64).",
                Category = ActionCategory.LoopControl,
                ValidScopes = BindingScope.Global,
                PrimaryInput = InputType.Key,
                PrimaryKey = Keys.Add,
                SortOrder = 62
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Loop.RemoveBar",
                DisplayName = "Remove last bar",
                Description = "Minus button or - key: remove the last bar.",
                Category = ActionCategory.LoopControl,
                ValidScopes = BindingScope.Global,
                PrimaryInput = InputType.Key,
                PrimaryKey = Keys.Subtract,
                SortOrder = 63
            });

            // ===== TRACK MANAGEMENT =====
            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Track.Reorder",
                DisplayName = "Move track (reorder)",
                Description = "Drag a track name on the left with LMB: a ghost row follows the mouse and an insertion line shows the target.",
                Category = ActionCategory.TrackManagement,
                ValidScopes = BindingScope.TrackHeader,
                PrimaryInput = InputType.MouseLeft,
                PrimaryMouseAction = MouseAction.Drag,
                SortOrder = 70
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Track.Settings",
                DisplayName = "Track settings",
                Description = "Double-click a track name or right-click → context menu: set defaults for volume, pitch and resize mode.",
                Category = ActionCategory.TrackManagement,
                ValidScopes = BindingScope.TrackHeader,
                PrimaryInput = InputType.MouseLeft,
                PrimaryMouseAction = MouseAction.DoubleClick,
                SecondaryInput = InputType.MouseRight,
                SecondaryMouseAction = MouseAction.Click,
                SortOrder = 71
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Track.AddExternal",
                Implemented = true,
                DisplayName = "Add external track (drop)",
                Description = "Drag samples from Explorer or another source onto the grid: a track is inserted at the mouse position.",
                Category = ActionCategory.TrackManagement,
                ValidScopes = BindingScope.Global,
                PrimaryInput = InputType.MouseLeft,
                PrimaryMouseAction = MouseAction.Drag,
                IsHardcoded = true,
                AllowRemapping = true,
                SortOrder = 72
            });

            // ===== UNDO/REDO =====
            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Edit.Undo",
                DisplayName = "Undo",
                Description = "Undo the last action.",
                Category = ActionCategory.NoteEditing,
                ValidScopes = BindingScope.Global,
                PrimaryInput = InputType.KeyCombo,
                PrimaryKey = Keys.Z,
                PrimaryModifiers = ModifierKey.Control,
                SortOrder = 80
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Edit.Redo",
                DisplayName = "Redo",
                Description = "Redo the last undone action.",
                Category = ActionCategory.NoteEditing,
                ValidScopes = BindingScope.Global,
                PrimaryInput = InputType.KeyCombo,
                PrimaryKey = Keys.Y,
                PrimaryModifiers = ModifierKey.Control,
                SortOrder = 81
            });

            // ===== PARAMETER ADJUSTMENT (Wheel on Note) =====
            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Param.PitchWheel",
                Implemented = true,
                DisplayName = "Pitch by mouse wheel",
                Description = "Shift + mouse wheel over note/selection: Pitch ±¼ semitone (up to ±24).",
                Category = ActionCategory.NoteEditing,
                ValidScopes = BindingScope.Note | BindingScope.Global,
                PrimaryInput = InputType.MouseWheel,
                PrimaryModifiers = ModifierKey.Shift,
                PrimaryMouseAction = MouseAction.WheelUp,
                IsHardcoded = true,
                AllowRemapping = true,
                SortOrder = 90
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Param.VolumeWheel",
                Implemented = true,
                DisplayName = "Volume by mouse wheel",
                Description = "Alt + mouse wheel over note/selection: volume ±5% (0–250%).",
                Category = ActionCategory.NoteEditing,
                ValidScopes = BindingScope.Note | BindingScope.Global,
                PrimaryInput = InputType.MouseWheel,
                PrimaryModifiers = ModifierKey.Alt,
                PrimaryMouseAction = MouseAction.WheelUp,
                IsHardcoded = true,
                AllowRemapping = true,
                SortOrder = 91
            });

            // ===== SETTINGS =====
            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Settings.OpenConfig",
                Implemented = true,
                DisplayName = "Open Control Configuration",
                Description = "Click gear icon: Opens this configuration menu.",
                Category = ActionCategory.Settings,
                ValidScopes = BindingScope.Global,
                PrimaryInput = InputType.MouseLeft,
                PrimaryMouseAction = MouseAction.Click,
                IsHardcoded = true,
                AllowRemapping = true,
                SortOrder = 100
            });

            scheme.RebuildIndex();
            return scheme;
        }
    }
}
