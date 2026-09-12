namespace ModularAudience.MidiController.Models;

/// <summary>
/// High-level commands that a MIDI controller can trigger.
/// </summary>
public enum MidiCommand
{
    // Playback
    Play,
    Stop,
    Pause,
    PlayPause,

    // Transport / Navigation
    NextTrack,
    PreviousTrack,
    Restart,
    Seek,

    // Looping
    ToggleLoop,
    LoopStart,
    LoopEnd,
    LoopIn,
    LoopOut,

    // Volume / Fader
    VolumeUp,
    VolumeDown,
    MasterVolume,
    TrackVolume,

    // UI / General
    SelectTrack,
    ToggleMute,
    ToggleSolo,
    OpenWindow,
    CloseWindow,
    Custom,

    None
}
