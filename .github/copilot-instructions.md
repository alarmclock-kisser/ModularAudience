# Copilot Instructions
dass es 
## General Guidelines
- For this repo, LLM failures should always be logged and shown in a copyable OK dialog; raw LLM responses should be logged.
- Provide concise status messages; execute plans without lengthy announcements and perform steps in a single run when possible.
- Implement necessary fixes immediately; do not ask for approval or request follow-up questions for obvious corrections.
- Do not automatically commit or push changes; only restart the app when the user explicitly requests or performs it. When debugging, continue the existing instance; do not restart automatically.

## Project-Specific Rules
- The BreakbeatGenerator beatmap should render as a drum-by-step matrix in the picture box.
- For the BreakbeatGenerator LLM flow, the system prompt should define the Gemma-style JSON contract, while the user prompt should contain the verbal beat request plus the numeric parameters and drumkit dictionary payload.
- When modifying WinForms dialogs in this repo, new UI elements should be created and wired in the corresponding `<Window>.Designer.cs` file rather than programmatically at runtime.
- Audio processing logic should not reside in WinForms window classes but rather in audio processor classes (e.g., Processors_V4). 
- Use a partial schema for window classes and aim for the 30-30 rule as a soft structural goal (approximately 30 methods of 30 lines each).
- Maintain high priority for the playback thread during audio playback to avoid stuttering and lag.
- Use `format.Contains("3")` to detect MP3 export formats instead of checking for exact equality like `".mp3"`.
- For the current rate bug:
  - Use a logarithmic varispeed curve for hScrollBar_rate so adjustments near 0 are finer (map scrollbar position to playback rate via a logarithmic/exponential mapping rather than linear).
  - Exclude Syncer involvement (tested with exactly one open, unsynced track).
  - Ensure the altered sound is audible only while the scrollbar thumb is actively moved; rate changes made before playback must not persist when played later.
  - Remove the effect as soon as the scrollbar thumb stops moving (i.e., when its position becomes steady), even if the user continues to hold the thumb; do not wait for mouse/button release.
  - Support Ctrl+Left-click on hScrollBar_rate to reliably reset the rate to center (value 0).
  - Add an explicit "Center/Reset Rate" entry to the rate scrollbar's right-click context menu.
- LoopControl for playlist playback must target all concurrently active playlist overlap tracks, not just a single playlist track.

## UI Interaction Rules
- Prefer keyboard modifiers and context-menu entries for common quick actions (e.g., centering controls, resetting values).
- Keep UI affordances discoverable and consistent across similar controls.