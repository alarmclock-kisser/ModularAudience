# Copilot Instructions

## General Guidelines
- For this repo, LLM failures should always be logged and shown in a copyable OK dialog; raw LLM responses should be logged.

## Project-Specific Rules
- The BreakbeatGenerator beatmap should render as a drum-by-step matrix in the picture box.
- For the BreakbeatGenerator LLM flow, the system prompt should define the Gemma-style JSON contract, while the user prompt should contain the verbal beat request plus the numeric parameters and drumkit dictionary payload.