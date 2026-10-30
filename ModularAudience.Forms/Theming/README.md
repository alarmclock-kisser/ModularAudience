# Color themes

Right-click the **WindowMain background** to open **Theme Editor** or select a
saved JSON theme from **Load Theme**. **No theme — original Forms colors** restores
the application's original colors and disables automatic theming.

Themes live in `%APPDATA%\ModularAudience\Themes`. `active-theme.json` is the
startup snapshot, not an additional entry in the saved-theme list.

## Editor

- Change the global palette, start with Light/Dark, or leave values blank to inherit.
- Enter `#RRGGBB`, `#AARRGGBB`, or a named color. Valid input previews while typing.
- Double-click a color value or use **Choose color** for the Windows color picker.
- Select a control in the open-window tree, or use **Pick element on screen**.
- Add a rule for that element, all controls of its type, its window, or globally.
- Rule selectors accept `*` and `?`; full type names and base types are supported.
- Later rules override earlier rules. Duplicate, reorder, disable, or remove rules.
- Empty rule properties inherit from earlier rules / the palette.
- The property list includes writable `Color` properties supplied by custom controls.
- Use the JSON editor for advanced changes; **Validate & use JSON** imports them.
- **Save JSON** saves a named file. **Apply & remember** saves the active theme
  (and updates the selected file). Closing restores the last applied theme.
- The saved-theme combo switches editing documents. **Delete theme** removes the
  selected file and returns to original application colors.

## Rule example

```json
{
  "Version": 1,
  "Name": "Custom buttons",
  "Palette": { "Background": "#202124", "Foreground": "#E8EAED" },
  "Rules": [
    {
      "Enabled": true,
      "Description": "Buttons everywhere",
      "FormType": "*",
      "ControlType": "ButtonBase",
      "ElementPath": "*",
      "Colors": { "BackColor": "#334455", "ForeColor": "#FFFFFF" }
    }
  ]
}
```

The central service watches open windows and newly added controls; forms do not
need a new base class. It supports menus, grids, property grids, standard lists,
combo items, tab headers, and application owner-drawn lists. Native Windows chrome
(title bars, scrollbar/thumb rendering, system file/color dialogs) and graphics
drawn directly with fixed brushes are not writable Control color properties;
these require a dedicated renderer rather than a JSON property override.
