# Copilot Instructions

## General Guidelines
- For this repo, LLM failures should always be logged and shown in a copyable OK dialog; raw LLM responses should be logged.

## Project-Specific Rules
- The BreakbeatGenerator beatmap should render as a drum-by-step matrix in the picture box.
- For the BreakbeatGenerator LLM flow, the system prompt should define the Gemma-style JSON contract, while the user prompt should contain the verbal beat request plus the numeric parameters and drumkit dictionary payload.
- When modifying WinForms dialogs in this repo, new UI elements should be created and wired in the corresponding `<Window>.Designer.cs` file rather than programmatically at runtime.
- Audio processing logic should not reside in WinForms window classes but rather in audio processor classes (e.g., Processors_V4). 
- Use a partial schema for window classes and aim for the 30-30 rule as a soft structural goal (approximately 30 methods of 30 lines each).
- Maintain high priority for the playback thread during audio playback to avoid stuttering and lag.