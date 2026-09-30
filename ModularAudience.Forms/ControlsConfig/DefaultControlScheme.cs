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
                DisplayName = "Note zeichnen",
                Description = "Leere Zelle: Note setzen. Drag horizontal = Länge bestimmen.",
                Category = ActionCategory.NoteEditing,
                ValidScopes = BindingScope.Grid,
                PrimaryInput = InputType.MouseLeft,
                PrimaryMouseAction = MouseAction.Drag,
                SortOrder = 10
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Note.DrawShort",
                DisplayName = "Kurze Note (1 Step)",
                Description = "Leere Zelle: Note mit fixer Länge von 1 Step setzen.",
                Category = ActionCategory.NoteEditing,
                ValidScopes = BindingScope.Grid,
                PrimaryInput = InputType.MouseLeft,
                PrimaryModifiers = ModifierKey.Control,
                PrimaryMouseAction = MouseAction.Click,
                SortOrder = 11
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Note.Move",
                DisplayName = "Note verschieben",
                Description = "Bestehende Note an der Mitte packen und ziehen (horizontal + vertikal).",
                Category = ActionCategory.NoteEditing,
                ValidScopes = BindingScope.Note,
                PrimaryInput = InputType.MouseLeft,
                PrimaryMouseAction = MouseAction.Drag,
                SortOrder = 12
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Note.Resize",
                DisplayName = "Note resizen (Kante)",
                Description = "Note am linken/rechten Drittel packen und ziehen zum Verändern der Länge.",
                Category = ActionCategory.NoteEditing,
                ValidScopes = BindingScope.Note,
                PrimaryInput = InputType.MouseLeft,
                PrimaryMouseAction = MouseAction.Drag,
                SortOrder = 13
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Note.ResizeToggleMode",
                DisplayName = "Resizen + Mode Toggle",
                Description = "Beim Resizen mit Ctrl: Beim Loslassen ohne Bewegung TimeStretch ↔ Varispeed toggeln.",
                Category = ActionCategory.NoteEditing,
                ValidScopes = BindingScope.Note,
                PrimaryInput = InputType.MouseLeft,
                PrimaryModifiers = ModifierKey.Control,
                PrimaryMouseAction = MouseAction.Drag,
                IsHardcoded = true,
                AllowRemapping = false,
                SortOrder = 14
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Note.HardResize",
                DisplayName = "Hard Resize (Sample Cut)",
                Description = "Note auf exakt 1 Step schneiden (Sample wird gehardcut).",
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
                DisplayName = "Note löschen (Einzeln)",
                Description = "Rechtsklick auf Note löscht diese einzelne Note.",
                Category = ActionCategory.NoteEditing,
                ValidScopes = BindingScope.Note,
                PrimaryInput = InputType.MouseRight,
                PrimaryMouseAction = MouseAction.Click,
                SortOrder = 16
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Note.EraseDrag",
                DisplayName = "Radierer-Modus (Ziehen)",
                Description = "RMB gedrückt halten + ziehen: Alle berührten Noten werden gelöscht (Custom Radiergummi-Cursor).",
                Category = ActionCategory.NoteEditing,
                ValidScopes = BindingScope.Grid | BindingScope.Note,
                PrimaryInput = InputType.MouseRight,
                PrimaryMouseAction = MouseAction.Drag,
                IsHardcoded = true,
                AllowRemapping = false,
                SortOrder = 17
            });

            // ===== SELECTION =====
            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Select.Rectangle",
                DisplayName = "Rechteckselektion",
                Description = "Shift + LMB auf leerer Zelle + ziehen: Mehrfachauswahl per Rechteck.",
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
                DisplayName = "Gruppen-Resize",
                Description = "Shift + Kante ziehen: Alle markierten Noten ändern dieselbe Kante um denselben Delta.",
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
                DisplayName = "Selektion klonen (Drag)",
                Description = "Alt + LMB auf markierter Note + ziehen: Kopie der Selektion erstellen.",
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
                DisplayName = "Selektion löschen",
                Description = "Entf / Backspace: Alle markierten Noten löschen.",
                Category = ActionCategory.Selection,
                ValidScopes = BindingScope.Global,
                PrimaryInput = InputType.Key,
                PrimaryKey = Keys.Delete,
                SortOrder = 23
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Select.DeleteAlt",
                DisplayName = "Selektion löschen (Backspace)",
                Description = "Backspace: Alternative zum Löschen der Selektion.",
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
                DisplayName = "Kopieren",
                Description = "Markierte Noten in Zwischenablage kopieren.",
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
                DisplayName = "Einfügen (Preview)",
                Description = "Paste-Preview starten: Ghost-Struktur folgt Maus. LMB = platzieren, RMB/Esc = abbrechen.",
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
                DisplayName = "Paste-Preview abbrechen",
                Description = "Aktive Paste-Preview abbrechen.",
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
                DisplayName = "Paste-Preview platzieren",
                Description = "Ghost-Struktur an aktueller Mausposition platzieren (nur Noten mit Start auf Grid).",
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
                DisplayName = "Horizontal zoomen",
                Description = "Strg + Mausrad: Zoom am Mauszeiger (1× … 512×).",
                Category = ActionCategory.View,
                ValidScopes = BindingScope.Global,
                PrimaryInput = InputType.MouseWheel,
                PrimaryModifiers = ModifierKey.Control,
                PrimaryMouseAction = MouseAction.WheelUp,
                IsHardcoded = true,
                AllowRemapping = false,
                SortOrder = 40
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "View.ScrollHorizontal",
                DisplayName = "Horizontal scrollen",
                Description = "Mausrad (bei Zoom > 1×): Horizontal scrollen.",
                Category = ActionCategory.View,
                ValidScopes = BindingScope.Global,
                PrimaryInput = InputType.MouseWheel,
                PrimaryMouseAction = MouseAction.WheelUp,
                IsHardcoded = true,
                AllowRemapping = false,
                SortOrder = 41
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "View.ScrollBar",
                DisplayName = "Scrollbalken",
                Description = "Untere Scrollbar ziehen für horizontale Navigation.",
                Category = ActionCategory.View,
                ValidScopes = BindingScope.Global,
                IsHardcoded = true,
                AllowRemapping = false,
                SortOrder = 42
            });

            // ===== PLAYBACK =====
            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Playback.HearToggle",
                DisplayName = "Hear Wiedergabe Start/Stop",
                Description = "Loop-Wiedergabe der markierten Quarters starten/stoppen.",
                Category = ActionCategory.Playback,
                ValidScopes = BindingScope.Global,
                PrimaryInput = InputType.Key,
                PrimaryKey = Keys.Space,
                SortOrder = 50
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Playback.PrehearNote",
                DisplayName = "Note vorhören (Click)",
                Description = "Klick auf Note spielt diese sofort (unabhängig von Pre-Hear Setting).",
                Category = ActionCategory.Playback,
                ValidScopes = BindingScope.Note,
                PrimaryInput = InputType.MouseLeft,
                PrimaryMouseAction = MouseAction.Click,
                IsHardcoded = true,
                AllowRemapping = false,
                SortOrder = 51
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Playback.PrehearToggle",
                DisplayName = "Auto-Prehear toggeln",
                Description = "Checkbox 'Pre-Hear': Nach Platzieren/Ändern automatisch Note vorhören.",
                Category = ActionCategory.Playback,
                ValidScopes = BindingScope.Global,
                IsHardcoded = true,
                AllowRemapping = false,
                SortOrder = 52
            });

            // ===== LOOP CONTROL =====
            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Loop.ToggleBar",
                DisplayName = "Ganzen Takt toggeln",
                Description = "LMB auf Takt-Header: Alle 4 Quarters des Takts an/abwählen.",
                Category = ActionCategory.LoopControl,
                ValidScopes = BindingScope.BarHeader,
                PrimaryInput = InputType.MouseLeft,
                PrimaryMouseAction = MouseAction.Click,
                SortOrder = 60
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Loop.ToggleQuarter",
                DisplayName = "Einzelnes Viertel toggeln",
                Description = "RMB auf Takt-Header: Einzelnes Quarter an/abwählen.",
                Category = ActionCategory.LoopControl,
                ValidScopes = BindingScope.BarHeader,
                PrimaryInput = InputType.MouseRight,
                PrimaryMouseAction = MouseAction.Click,
                SortOrder = 61
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Loop.AddBar",
                DisplayName = "Takt hinzufügen",
                Description = "Plus-Taste: Einen Takt anhängen (max 64).",
                Category = ActionCategory.LoopControl,
                ValidScopes = BindingScope.Global,
                PrimaryInput = InputType.Key,
                PrimaryKey = Keys.Add,
                SortOrder = 62
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Loop.RemoveBar",
                DisplayName = "Letzten Takt entfernen",
                Description = "Minus-Taste: Letzten Takt entfernen (min 1).",
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
                DisplayName = "Track verschieben (Reorder)",
                Description = "Track-Name links mit LMB draggen: Ghost-Row folgt Maus, Einfügeline zeigt Target.",
                Category = ActionCategory.TrackManagement,
                ValidScopes = BindingScope.TrackHeader,
                PrimaryInput = InputType.MouseLeft,
                PrimaryMouseAction = MouseAction.Drag,
                SortOrder = 70
            });

            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Track.Settings",
                DisplayName = "Track-Einstellungen",
                Description = "Doppelklick auf Track-Name oder RMB → Kontextmenü: Defaults für Volume, Pitch, PlaybackMode setzen.",
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
                DisplayName = "Externen Track hinzufügen (Drop)",
                Description = "Samples aus Explorer/anderer Quelle auf Grid ziehen → Track wird an Mausposition eingefügt.",
                Category = ActionCategory.TrackManagement,
                ValidScopes = BindingScope.Global,
                PrimaryInput = InputType.MouseLeft,
                PrimaryMouseAction = MouseAction.Drag,
                IsHardcoded = true,
                AllowRemapping = false,
                SortOrder = 72
            });

            // ===== UNDO/REDO =====
            scheme.Bindings.Add(new ControlBinding
            {
                ActionId = "Edit.Undo",
                DisplayName = "Rückgängig",
                Description = "Letzte Aktion rückgängig machen.",
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
                DisplayName = "Wiederholen",
                Description = "Rückgängig gemachte Aktion wiederholen.",
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
                DisplayName = "Pitch per Mausrad",
                Description = "Shift + Mausrad über Note/Selektion: Pitch ±¼ Semitone (bis ±24).",
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
                DisplayName = "Volume per Mausrad",
                Description = "Alt + Mausrad über Note/Selektion: Volume ±5% (0–250%).",
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
                DisplayName = "Steuerungskonfiguration öffnen",
                Description = "Zahnrad-Symbol klicken: Öffnet dieses Konfigurationsmenü.",
                Category = ActionCategory.Settings,
                ValidScopes = BindingScope.Global,
                PrimaryInput = InputType.MouseLeft,
                PrimaryMouseAction = MouseAction.Click,
                IsHardcoded = true,
                AllowRemapping = false,
                SortOrder = 100
            });

            scheme.RebuildIndex();
            return scheme;
        }
    }
}