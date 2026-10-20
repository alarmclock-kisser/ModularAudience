using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using ModularAudience.Forms.Modules;

namespace ModularAudience.Forms.Hotkeys
{
    public static class HotkeyActionResolver
    {
        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;

        private const int MK_LBUTTON = 0x0001;
        private const int MK_RBUTTON = 0x0002;
        private const int MK_SHIFT = 0x0004;
        private const int MK_CONTROL = 0x0008;
        private const int MK_MBUTTON = 0x0010;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern bool GetKeyboardState(byte[] lpKeyState);

        [DllImport("user32.dll")]
        private static extern bool SetKeyboardState(byte[] lpKeyState);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public static bool IsKeyPhysicallyDown(Keys key)
        {
            return (GetAsyncKeyState((int)(key & Keys.KeyCode)) & 0x8000) != 0;
        }

        public static HotkeyModifiers GetCurrentModifiers()
        {
            HotkeyModifiers modifiers = HotkeyModifiers.None;
            if ((GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0)
            {
                modifiers |= HotkeyModifiers.Control;
            }
            if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0)
            {
                modifiers |= HotkeyModifiers.Shift;
            }
            if ((GetAsyncKeyState(VK_MENU) & 0x8000) != 0)
            {
                modifiers |= HotkeyModifiers.Alt;
            }
            if ((GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0)
            {
                modifiers |= HotkeyModifiers.Win;
            }

            return modifiers;
        }

        public static HotkeyActionTarget BuildTarget(
            Control control,
            Form owner,
            HotkeyMouseGesture gesture,
            HotkeyModifiers actionModifiers,
            Point screenPoint)
        {
            Point clientPoint = control.PointToClient(screenPoint);
            int width = Math.Max(1, control.ClientSize.Width - 1);
            int height = Math.Max(1, control.ClientSize.Height - 1);

            HotkeyActionTarget target = new()
            {
                ActionKind = HotkeyActionKinds.ControlGesture,
                OwnerType = owner.GetType().FullName ?? owner.GetType().Name,
                OwnerTitle = owner.Text ?? string.Empty,
                ControlPath = BuildPath(control, owner),
                Gesture = gesture,
                ActionModifiers = actionModifiers,
                RelativeX = Math.Clamp(clientPoint.X / (float)width, 0f, 1f),
                RelativeY = Math.Clamp(clientPoint.Y / (float)height, 0f, 1f)
            };

            if (owner is TrackView trackView)
            {
                target.TrackViewId = trackView.TrackViewId;
                target.UseLastFocusedTrackView = true;
            }

            return target;
        }

        public static HotkeyActionTarget BuildContextMenuItemTarget(
            Control sourceControl,
            Form owner,
            ToolStripItem item,
            HotkeyModifiers actionModifiers,
            Point screenPoint)
        {
            Point clientPoint = sourceControl.PointToClient(screenPoint);
            int width = Math.Max(1, sourceControl.ClientSize.Width - 1);
            int height = Math.Max(1, sourceControl.ClientSize.Height - 1);

            HotkeyActionTarget target = new()
            {
                ActionKind = HotkeyActionKinds.ContextMenuItem,
                OwnerType = owner.GetType().FullName ?? owner.GetType().Name,
                OwnerTitle = owner.Text ?? string.Empty,
                ControlPath = BuildPath(sourceControl, owner),
                ContextMenuName = GetContextMenuName(item),
                MenuItemPath = BuildMenuItemPath(item),
                Gesture = HotkeyMouseGesture.RightClick,
                ActionModifiers = actionModifiers,
                RelativeX = Math.Clamp(clientPoint.X / (float)width, 0f, 1f),
                RelativeY = Math.Clamp(clientPoint.Y / (float)height, 0f, 1f)
            };

            if (owner is TrackView trackView)
            {
                target.TrackViewId = trackView.TrackViewId;
                target.UseLastFocusedTrackView = true;
            }

            return target;
        }

        public static HotkeyActionTarget BuildNumericUpDownTarget(
            NumericUpDown numericUpDown,
            Form owner,
            bool increment,
            HotkeyModifiers actionModifiers)
        {
            HotkeyActionTarget target = new()
            {
                ActionKind = HotkeyActionKinds.NumericUpDown,
                OwnerType = owner.GetType().FullName ?? owner.GetType().Name,
                OwnerTitle = owner.Text ?? string.Empty,
                ControlPath = BuildPath(numericUpDown, owner),
                NumericUpDownIncrement = increment,
                Gesture = HotkeyMouseGesture.LeftClick,
                ActionModifiers = actionModifiers,
                RelativeX = 0.5f,
                RelativeY = increment ? 0.25f : 0.75f
            };

            if (owner is TrackView trackView)
            {
                target.TrackViewId = trackView.TrackViewId;
                target.UseLastFocusedTrackView = true;
            }

            return target;
        }

        public static string BuildActionName(
            Form owner,
            Control control,
            HotkeyMouseGesture gesture,
            HotkeyModifiers actionModifiers)
        {
            string ownerName = GetOwnerDisplayName(owner);
            string controlName = GetControlDisplayName(control, owner);

            if (gesture == HotkeyMouseGesture.LeftClick && actionModifiers == HotkeyModifiers.None)
            {
                return $"{ownerName} - {controlName}";
            }

            string gestureText = HotkeyFormatter.FormatGesture(gesture, actionModifiers);
            return $"{ownerName} - {controlName} [{gestureText}]";
        }

        public static string BuildContextMenuItemActionName(Form owner, ToolStripItem item)
        {
            return $"{GetOwnerDisplayName(owner)} - {GetMenuItemDisplayName(item)}";
        }

        public static string BuildNumericUpDownActionName(
            Form owner,
            NumericUpDown numericUpDown,
            bool increment)
        {
            string ownerName = GetOwnerDisplayName(owner);
            string controlName = GetControlDisplayName(numericUpDown, owner);
            string direction = increment ? "Increment" : "Decrement";
            return $"{ownerName} - {controlName} [{direction}]";
        }

        public static bool IsToggleAction(Control control)
        {
            if (control is CheckBox or RadioButton)
            {
                return true;
            }

            string name = control.Name ?? string.Empty;
            string lower = name.ToLowerInvariant();

            if (lower is "button_loop" or "button_playback" or "button_pause")
            {
                return true;
            }

            if (lower.Contains("loop") || lower.Contains("toggle") || lower.Contains("mute")
                || lower.Contains("solo") || lower.Contains("sync"))
            {
                return true;
            }

            if (control is Button && control.Parent is Panel panel
                && string.Equals(panel.Name, "panel_buttons", StringComparison.Ordinal)
                && control.FindForm() is LoopControl)
            {
                return true;
            }

            return false;
        }

        public static bool ExecuteControlGesture(
            HotkeyActionTarget target,
            HotkeyModifiers additionalModifiers = HotkeyModifiers.None)
        {
            Form? owner = ResolveOwner(target);
            if (owner == null || owner.IsDisposed || owner.Disposing)
            {
                System.Diagnostics.Debug.WriteLine($"Hotkeys: owner for '{target.OwnerType}' is not open.");
                return false;
            }

            Control? control = ResolveControl(owner, target.ControlPath);
            if (control == null || control.IsDisposed || control.Disposing)
            {
                System.Diagnostics.Debug.WriteLine($"Hotkeys: target control for '{target.OwnerType}' was not found.");
                return false;
            }

            if (control is not Form && (!control.Visible || !control.Enabled))
            {
                System.Diagnostics.Debug.WriteLine($"Hotkeys: target control for '{target.OwnerType}' is not available.");
                return false;
            }

            try
            {
                if (owner.WindowState == FormWindowState.Minimized)
                {
                    owner.WindowState = FormWindowState.Normal;
                }

                SendGesture(control, target, target.ActionModifiers | additionalModifiers);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Hotkeys: could not execute '{target.OwnerType}': {ex.Message}");
                return false;
            }
        }

        public static bool ExecuteContextMenuItem(HotkeyActionTarget target)
        {
            Form? owner = ResolveOwner(target);
            if (owner == null || owner.IsDisposed || owner.Disposing)
            {
                System.Diagnostics.Debug.WriteLine($"Hotkeys: owner for '{target.OwnerType}' is not open.");
                return false;
            }

            Control? sourceControl = ResolveControl(owner, target.ControlPath);
            ContextMenuStrip? menu = ResolveContextMenu(owner, sourceControl, target.ContextMenuName);
            if (menu == null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Hotkeys: context menu '{target.ContextMenuName}' was not found for '{target.OwnerType}'.");
                return false;
            }

            ToolStripItem? item = ResolveMenuItem(menu, target.MenuItemPath);
            if (item == null
                || item is ToolStripSeparator
                || item is ToolStripControlHost
                || !item.Available
                || !item.Enabled)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Hotkeys: context menu item was not available for '{target.OwnerType}'.");
                return false;
            }

            try
            {
                if (owner.WindowState == FormWindowState.Minimized)
                {
                    owner.WindowState = FormWindowState.Normal;
                }

                item.PerformClick();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Hotkeys: could not execute context menu item for '{target.OwnerType}': {ex.Message}");
                return false;
            }
        }

        public static bool ExecuteNumericUpDown(HotkeyActionTarget target)
        {
            Form? owner = ResolveOwner(target);
            if (owner == null || owner.IsDisposed || owner.Disposing)
            {
                System.Diagnostics.Debug.WriteLine($"Hotkeys: owner for '{target.OwnerType}' is not open.");
                return false;
            }

            Control? control = ResolveControl(owner, target.ControlPath);
            if (control is not NumericUpDown numericUpDown
                || numericUpDown.IsDisposed
                || numericUpDown.Disposing)
            {
                System.Diagnostics.Debug.WriteLine($"Hotkeys: numeric up/down target for '{target.OwnerType}' was not found.");
                return false;
            }

            if (!numericUpDown.Visible || !numericUpDown.Enabled)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Hotkeys: numeric up/down target for '{target.OwnerType}' is not available.");
                return false;
            }

            try
            {
                if (owner.WindowState == FormWindowState.Minimized)
                {
                    owner.WindowState = FormWindowState.Normal;
                }

                if (target.NumericUpDownIncrement)
                {
                    numericUpDown.UpButton();
                }
                else
                {
                    numericUpDown.DownButton();
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Hotkeys: could not change numeric up/down for '{target.OwnerType}': {ex.Message}");
                return false;
            }
        }

        public static bool? GetToggleState(HotkeyActionTarget target)
        {
            try
            {
                if (target.ActionKind == HotkeyActionKinds.ContextMenuItem)
                {
                    Form? menuOwner = ResolveOwner(target);
                    if (menuOwner == null || menuOwner.IsDisposed || menuOwner.Disposing)
                    {
                        return null;
                    }

                    Control? sourceControl = ResolveControl(menuOwner, target.ControlPath);
                    ContextMenuStrip? menu = ResolveContextMenu(
                        menuOwner,
                        sourceControl,
                        target.ContextMenuName);
                    ToolStripItem? item = menu == null
                        ? null
                        : ResolveMenuItem(menu, target.MenuItemPath);
                    return item is ToolStripMenuItem menuItem ? menuItem.Checked : null;
                }

                if (target.ActionKind != HotkeyActionKinds.ControlGesture)
                {
                    return null;
                }

                Form? owner = ResolveOwner(target);
                if (owner == null || owner.IsDisposed || owner.Disposing)
                {
                    return null;
                }

                Control? control = ResolveControl(owner, target.ControlPath);
                if (control == null || control.IsDisposed || control.Disposing)
                {
                    return null;
                }

                if (control is CheckBox checkBox)
                {
                    return checkBox.Checked;
                }

                if (control is RadioButton radioButton)
                {
                    return radioButton.Checked;
                }

                AccessibleStates state = control.AccessibilityObject.State;
                return (state & AccessibleStates.Checked) != 0 ? true : null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Hotkeys: could not read toggle state for '{target.OwnerType}': {ex.Message}");
                return null;
            }
        }

        public static ToolStripItem? GetContextMenuItemAt(Point screenPoint)
        {
            POINT point = new() { X = screenPoint.X, Y = screenPoint.Y };
            IntPtr handle = WindowFromPoint(point);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            Control? control = Control.FromChildHandle(handle);
            ToolStripDropDown? dropDown = control as ToolStripDropDown;
            while (dropDown == null && control != null)
            {
                control = control.Parent;
                dropDown = control as ToolStripDropDown;
            }

            if (dropDown == null || !dropDown.Visible)
            {
                return null;
            }

            ToolStripItem? item = dropDown.GetItemAt(dropDown.PointToClient(screenPoint));
            if (item == null || item is ToolStripSeparator)
            {
                return null;
            }

            return item;
        }

        public static bool TryGetNumericUpDownChange(
            Point screenPoint,
            out NumericUpDown? numericUpDown,
            out bool increment)
        {
            numericUpDown = null;
            increment = false;

            POINT point = new() { X = screenPoint.X, Y = screenPoint.Y };
            IntPtr handle = WindowFromPoint(point);
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            Control? control = Control.FromChildHandle(handle);
            while (control != null && control is not NumericUpDown && control is not Form)
            {
                control = control.Parent;
            }

            NumericUpDown? target = control as NumericUpDown;
            if (target == null || !target.Visible || !target.Enabled)
            {
                return false;
            }

            Control? buttons = target.Controls
                .Cast<Control>()
                .FirstOrDefault(child => string.Equals(
                    child.GetType().Name,
                    "UpDownButtons",
                    StringComparison.Ordinal));
            if (buttons == null || !buttons.Visible || !buttons.Enabled)
            {
                return false;
            }

            Rectangle bounds = buttons.RectangleToScreen(buttons.ClientRectangle);
            if (!bounds.Contains(screenPoint))
            {
                return false;
            }

            Point localPoint = buttons.PointToClient(screenPoint);
            increment = localPoint.Y < Math.Max(1, buttons.ClientSize.Height) / 2f;
            numericUpDown = target;
            return true;
        }

        public static bool IsSelectableContextMenuItem(ToolStripItem? item)
        {
            if (item == null
                || item is ToolStripSeparator
                || item is ToolStripControlHost
                || !item.Available
                || !item.Enabled)
            {
                return false;
            }

            return item is not ToolStripDropDownItem dropDownItem || !dropDownItem.HasDropDownItems;
        }

        private static ContextMenuStrip? ResolveContextMenu(
            Form owner,
            Control? sourceControl,
            string menuName)
        {
            ContextMenuStrip? candidate = sourceControl?.ContextMenuStrip;
            if (MatchesMenuName(candidate, menuName))
            {
                return candidate;
            }

            foreach (Control control in EnumerateControls(owner))
            {
                candidate = control.ContextMenuStrip;
                if (MatchesMenuName(candidate, menuName))
                {
                    return candidate;
                }
            }

            if (string.IsNullOrWhiteSpace(menuName))
            {
                return null;
            }

            try
            {
                foreach (FieldInfo field in owner.GetType().GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!typeof(ContextMenuStrip).IsAssignableFrom(field.FieldType))
                    {
                        continue;
                    }

                    candidate = field.GetValue(owner) as ContextMenuStrip;
                    if (MatchesMenuName(candidate, menuName))
                    {
                        return candidate;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Hotkeys: could not inspect context menu fields: {ex.Message}");
            }

            return null;
        }

        private static bool MatchesMenuName(ContextMenuStrip? menu, string menuName)
        {
            if (menu == null)
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(menuName)
                || string.Equals(menu.Name, menuName, StringComparison.Ordinal);
        }

        private static IEnumerable<Control> EnumerateControls(Control root)
        {
            yield return root;
            foreach (Control child in root.Controls)
            {
                foreach (Control descendant in EnumerateControls(child))
                {
                    yield return descendant;
                }
            }
        }

        private static ToolStripItem? ResolveMenuItem(
            ContextMenuStrip menu,
            List<HotkeyMenuItemPathSegment> path)
        {
            if (path == null || path.Count == 0)
            {
                return null;
            }

            ToolStripItemCollection items = menu.Items;
            ToolStripItem? current = null;
            for (int index = 0; index < path.Count; index++)
            {
                current = FindMenuItem(items, path[index]);
                if (current == null)
                {
                    return null;
                }

                if (index < path.Count - 1)
                {
                    if (current is not ToolStripDropDownItem dropDownItem)
                    {
                        return null;
                    }

                    items = dropDownItem.DropDownItems;
                }
            }

            return current;
        }

        private static ToolStripItem? FindMenuItem(
            ToolStripItemCollection items,
            HotkeyMenuItemPathSegment segment)
        {
            List<ToolStripItem> candidates = items.Cast<ToolStripItem>().ToList();
            ToolStripItem? match = null;

            if (!string.IsNullOrEmpty(segment.Name))
            {
                match = candidates.FirstOrDefault(item =>
                    string.Equals(item.Name, segment.Name, StringComparison.Ordinal)
                    && IsMenuItemType(item, segment.TypeName));

                match ??= candidates.FirstOrDefault(item =>
                    string.Equals(item.Name, segment.Name, StringComparison.Ordinal));
            }

            if (match == null && !string.IsNullOrEmpty(segment.Text))
            {
                match = candidates.FirstOrDefault(item =>
                    string.Equals(GetMenuItemText(item), segment.Text, StringComparison.Ordinal)
                    && IsMenuItemType(item, segment.TypeName));

                match ??= candidates.FirstOrDefault(item =>
                    string.Equals(GetMenuItemText(item), segment.Text, StringComparison.Ordinal));
            }

            if (match == null)
            {
                List<ToolStripItem> sameType = candidates
                    .Where(item => IsMenuItemType(item, segment.TypeName))
                    .ToList();

                if (segment.Index >= 0 && segment.Index < sameType.Count)
                {
                    match = sameType[segment.Index];
                }
            }

            return match;
        }

        private static bool IsMenuItemType(ToolStripItem item, string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return true;
            }

            return string.Equals(item.GetType().FullName, typeName, StringComparison.Ordinal)
                || string.Equals(item.GetType().Name, typeName, StringComparison.Ordinal);
        }

        private static List<HotkeyMenuItemPathSegment> BuildMenuItemPath(ToolStripItem target)
        {
            List<ToolStripItem> chain = [];
            ToolStripItem? current = target;
            while (current != null)
            {
                chain.Add(current);
                current = current.OwnerItem;
            }

            chain.Reverse();

            List<HotkeyMenuItemPathSegment> path = [];
            foreach (ToolStripItem item in chain)
            {
                int index = 0;
                ToolStripItemCollection? siblings = item.Owner?.Items;
                if (siblings != null)
                {
                    List<ToolStripItem> sameType = siblings
                        .Cast<ToolStripItem>()
                        .Where(sibling => sibling.GetType() == item.GetType())
                        .ToList();
                    index = sameType.IndexOf(item);
                }

                path.Add(new HotkeyMenuItemPathSegment
                {
                    TypeName = item.GetType().FullName ?? item.GetType().Name,
                    Name = item.Name ?? string.Empty,
                    Text = Truncate(GetMenuItemText(item), 64),
                    Index = Math.Max(0, index)
                });
            }

            return path;
        }

        private static string GetContextMenuName(ToolStripItem item)
        {
            ToolStripItem root = item;
            while (root.OwnerItem != null)
            {
                root = root.OwnerItem;
            }

            return (root.Owner as ContextMenuStrip)?.Name ?? string.Empty;
        }

        private static string GetMenuItemDisplayName(ToolStripItem item)
        {
            List<string> parts = [];
            ToolStripItem? current = item;
            while (current != null)
            {
                string text = GetMenuItemText(current);
                if (string.IsNullOrWhiteSpace(text))
                {
                    text = string.IsNullOrWhiteSpace(current.Name)
                        ? Humanize(current.GetType().Name)
                        : Humanize(current.Name);
                }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    parts.Add(text);
                }

                current = current.OwnerItem;
            }

            parts.Reverse();
            return string.Join(" > ", parts);
        }

        private static string GetMenuItemText(ToolStripItem item)
        {
            return (item.Text ?? string.Empty).Replace("&", string.Empty).Trim();
        }

        public static Form? ResolveOwner(HotkeyActionTarget target)
        {
            if (target.UseLastFocusedTrackView)
            {
                TrackView? lastTrackView = WindowMain.LastSelectedTrackView;
                if (lastTrackView != null && !lastTrackView.IsDisposed && !lastTrackView.Disposing)
                {
                    return lastTrackView;
                }
            }

            if (string.Equals(target.OwnerType, typeof(WindowMain).FullName, StringComparison.Ordinal))
            {
                return WindowMain.Instance;
            }

            if (string.Equals(target.OwnerType, typeof(LoopControl).FullName, StringComparison.Ordinal))
            {
                LoopControl? loopControl = WindowMain.LoopControlWindow;
                if (loopControl != null && !loopControl.IsDisposed && !loopControl.Disposing)
                {
                    return loopControl;
                }
            }

            List<Form> matches = Application.OpenForms
                .Cast<Form>()
                .Where(form => !form.IsDisposed && !form.Disposing && IsOwnerType(form, target.OwnerType))
                .ToList();

            if (target.TrackViewId.HasValue)
            {
                TrackView? exact = matches
                    .OfType<TrackView>()
                    .FirstOrDefault(form => form.TrackViewId == target.TrackViewId.Value);
                if (exact != null)
                {
                    return exact;
                }
            }

            Form? active = Form.ActiveForm;
            if (active != null && matches.Contains(active))
            {
                return active;
            }

            if (!string.IsNullOrWhiteSpace(target.OwnerTitle))
            {
                Form? byTitle = matches.FirstOrDefault(form =>
                    string.Equals(form.Text, target.OwnerTitle, StringComparison.Ordinal));
                if (byTitle != null)
                {
                    return byTitle;
                }
            }

            List<Form> visible = matches.Where(form => form.Visible).ToList();
            if (visible.Count > 0)
            {
                return visible[^1];
            }

            return matches.LastOrDefault();
        }

        private static bool IsOwnerType(Form form, string ownerType)
        {
            return string.Equals(form.GetType().FullName, ownerType, StringComparison.Ordinal)
                || string.Equals(form.GetType().Name, ownerType, StringComparison.Ordinal);
        }

        public static Control? ResolveControl(Form owner, List<HotkeyControlPathSegment> path)
        {
            Control current = owner;
            foreach (HotkeyControlPathSegment segment in path)
            {
                Control? match = null;
                List<Control> children = current.Controls.Cast<Control>().ToList();

                if (!string.IsNullOrEmpty(segment.Name))
                {
                    match = children.FirstOrDefault(child =>
                        string.Equals(child.Name, segment.Name, StringComparison.Ordinal)
                        && IsControlType(child, segment.TypeName));

                    match ??= children.FirstOrDefault(child =>
                        string.Equals(child.Name, segment.Name, StringComparison.Ordinal));
                }

                if (match == null)
                {
                    List<Control> sameType = children
                        .Where(child => IsControlType(child, segment.TypeName))
                        .ToList();

                    if (!string.IsNullOrEmpty(segment.Tag))
                    {
                        match = sameType.FirstOrDefault(child =>
                            string.Equals(GetTagString(child), segment.Tag, StringComparison.Ordinal));
                    }

                    if (match == null && segment.Index >= 0 && segment.Index < sameType.Count)
                    {
                        match = sameType[segment.Index];
                    }

                    if (match == null && !string.IsNullOrEmpty(segment.Text))
                    {
                        match = sameType.FirstOrDefault(child =>
                            string.Equals(child.Text, segment.Text, StringComparison.Ordinal));
                    }
                }

                if (match == null)
                {
                    return null;
                }

                current = match;
            }

            return current;
        }

        private static bool IsControlType(Control control, string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return true;
            }

            return string.Equals(control.GetType().FullName, typeName, StringComparison.Ordinal)
                || string.Equals(control.GetType().Name, typeName, StringComparison.Ordinal);
        }

        private static List<HotkeyControlPathSegment> BuildPath(Control target, Form owner)
        {
            List<Control> chain = [];
            Control? current = target;
            while (current != null && !ReferenceEquals(current, owner))
            {
                chain.Add(current);
                current = current.Parent;
            }

            chain.Reverse();

            List<HotkeyControlPathSegment> path = [];
            foreach (Control control in chain)
            {
                Control? parent = control.Parent;
                int index = 0;
                if (parent != null)
                {
                    index = parent.Controls
                        .Cast<Control>()
                        .Where(child => child.GetType() == control.GetType())
                        .ToList()
                        .IndexOf(control);
                }

                path.Add(new HotkeyControlPathSegment
                {
                    TypeName = control.GetType().FullName ?? control.GetType().Name,
                    Name = control.Name ?? string.Empty,
                    Tag = GetTagString(control),
                    Text = Truncate(control.Text, 64),
                    Index = Math.Max(0, index)
                });
            }

            return path;
        }

        private static string GetTagString(Control control)
        {
            try
            {
                return Truncate(control.Tag?.ToString() ?? string.Empty, 64);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value ?? string.Empty;
            }

            return value[..maxLength];
        }

        private static string GetOwnerDisplayName(Form owner)
        {
            if (owner is TrackView trackView)
            {
                return "Track View #" + trackView.TrackViewId.ToString("D2");
            }

            if (owner is WindowMain)
            {
                return "Main Window";
            }

            if (owner is LoopControl)
            {
                return "Loop Control";
            }

            if (!string.IsNullOrWhiteSpace(owner.Text))
            {
                return owner.Text.Trim();
            }

            return Humanize(owner.GetType().Name.Replace("Dialog", string.Empty, StringComparison.Ordinal));
        }

        private static string GetControlDisplayName(Control control, Form owner)
        {
            if (control is Form)
            {
                return "Window";
            }

            if (owner is LoopControl && control is Button && control.Parent is Panel panel
                && string.Equals(panel.Name, "panel_buttons", StringComparison.Ordinal))
            {
                string loopText = control.Text?.Trim() ?? string.Empty;
                return string.IsNullOrEmpty(loopText) ? "Loop" : "Loop " + loopText;
            }

            string name = control.Name ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(name))
            {
                string friendlyName = GetFriendlyControlName(name);
                if (!string.IsNullOrWhiteSpace(friendlyName))
                {
                    return friendlyName;
                }
            }

            string text = control.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            return Humanize(control.GetType().Name);
        }

        private static string GetFriendlyControlName(string name)
        {
            string cleaned = name;
            string[] prefixes =
            [
                "button_", "checkBox_", "radioButton_", "textBox_", "comboBox_", "listBox_",
                "checkedListBox_", "pictureBox_", "panel_", "label_", "toolStripMenuItem_",
                "menuItem_", "menuToolStripItem_", "hScrollBar_", "vScrollBar_",
                "numericUpDown_", "domainUpDown_", "tabPage_", "toolStrip_"
            ];

            foreach (string prefix in prefixes)
            {
                if (cleaned.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    cleaned = cleaned[prefix.Length..];
                    break;
                }
            }

            Dictionary<string, string> known = new(StringComparer.OrdinalIgnoreCase)
            {
                ["button_playback"] = "Playback",
                ["button_pause"] = "Pause",
                ["button_loop"] = "Loop",
                ["button_copy"] = "Copy",
                ["button_forward"] = "Forward",
                ["button_backward"] = "Backward",
                ["button_apply"] = "Apply Changes",
                ["button_copyLog"] = "Copy Log",
                ["button_bringAllToFront"] = "Bring All To Front",
                ["button_playlistAllOn"] = "Playlist All On",
                ["button_playlistAllOff"] = "Playlist All Off",
                ["checkBox_settings"] = "Settings",
                ["checkBox_sync"] = "Sync Playback",
                ["checkBox_mute"] = "Mute",
                ["checkBox_solo"] = "Solo",
                ["button_controller"] = "Control",
                ["button_cuda"] = "CUDA"
            };

            if (known.TryGetValue(name, out string? friendly))
            {
                return friendly;
            }

            return Humanize(cleaned);
        }

        private static string Humanize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new(value.Length + 8);
            char previous = '\0';
            foreach (char current in value)
            {
                if (current == '_')
                {
                    if (builder.Length > 0 && builder[^1] != ' ')
                    {
                        builder.Append(' ');
                    }

                    previous = current;
                    continue;
                }

                if (builder.Length > 0
                    && char.IsUpper(current)
                    && !char.IsUpper(previous)
                    && previous != ' '
                    && builder[^1] != ' ')
                {
                    builder.Append(' ');
                }

                builder.Append(current);
                previous = current;
            }

            return builder.ToString().Trim();
        }

        private static void SendGesture(
            Control control,
            HotkeyActionTarget target,
            HotkeyModifiers actionModifiers)
        {
            int width = Math.Max(1, control.ClientSize.Width - 1);
            int height = Math.Max(1, control.ClientSize.Height - 1);
            int x = Math.Clamp((int)Math.Round(target.RelativeX * width), 0, width);
            int y = Math.Clamp((int)Math.Round(target.RelativeY * height), 0, height);
            IntPtr lParam = (IntPtr)((y << 16) | (x & 0xFFFF));

            int modifierFlags = 0;
            if (actionModifiers.HasFlag(HotkeyModifiers.Control))
            {
                modifierFlags |= MK_CONTROL;
            }
            if (actionModifiers.HasFlag(HotkeyModifiers.Shift))
            {
                modifierFlags |= MK_SHIFT;
            }

            byte[] originalState = new byte[256];
            if (!GetKeyboardState(originalState))
            {
                originalState = new byte[256];
            }

            byte[] actionState = (byte[])originalState.Clone();
            ApplyModifierState(actionState, VK_CONTROL, actionModifiers.HasFlag(HotkeyModifiers.Control));
            ApplyModifierState(actionState, VK_SHIFT, actionModifiers.HasFlag(HotkeyModifiers.Shift));
            ApplyModifierState(actionState, VK_MENU, actionModifiers.HasFlag(HotkeyModifiers.Alt));
            ApplyModifierState(actionState, VK_LWIN, actionModifiers.HasFlag(HotkeyModifiers.Win));
            ApplyModifierState(actionState, VK_RWIN, actionModifiers.HasFlag(HotkeyModifiers.Win));

            IntPtr hWnd = control.Handle;
            SetKeyboardState(actionState);
            try
            {
                switch (target.Gesture)
                {
                    case HotkeyMouseGesture.LeftDoubleClick:
                        SendMessage(hWnd, WM_LBUTTONDOWN, (IntPtr)(MK_LBUTTON | modifierFlags), lParam);
                        SendMessage(hWnd, WM_LBUTTONUP, (IntPtr)modifierFlags, lParam);
                        SendMessage(hWnd, WM_LBUTTONDBLCLK, (IntPtr)(MK_LBUTTON | modifierFlags), lParam);
                        SendMessage(hWnd, WM_LBUTTONUP, (IntPtr)modifierFlags, lParam);
                        break;

                    case HotkeyMouseGesture.RightClick:
                        SendMessage(hWnd, WM_RBUTTONDOWN, (IntPtr)(MK_RBUTTON | modifierFlags), lParam);
                        SendMessage(hWnd, WM_RBUTTONUP, (IntPtr)modifierFlags, lParam);
                        break;

                    case HotkeyMouseGesture.MiddleClick:
                        SendMessage(hWnd, WM_MBUTTONDOWN, (IntPtr)(MK_MBUTTON | modifierFlags), lParam);
                        SendMessage(hWnd, WM_MBUTTONUP, (IntPtr)modifierFlags, lParam);
                        break;

                    default:
                        SendMessage(hWnd, WM_LBUTTONDOWN, (IntPtr)(MK_LBUTTON | modifierFlags), lParam);
                        SendMessage(hWnd, WM_LBUTTONUP, (IntPtr)modifierFlags, lParam);
                        break;
                }
            }
            finally
            {
                SetKeyboardState(originalState);
            }
        }

        private static void ApplyModifierState(byte[] state, int virtualKey, bool down)
        {
            if (down)
            {
                state[virtualKey] |= 0x80;
            }
            else
            {
                state[virtualKey] = (byte)(state[virtualKey] & ~0x80);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);
    }
}
