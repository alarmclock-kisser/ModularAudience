using ModularAudience.MidiController.Models;

namespace ModularAudience.MidiController;

/// <summary>
/// Maps raw MIDI messages to high-level <see cref="MidiCommand"/> values.
/// Users can register custom mappings for specific (channel, command, data1) combinations.
/// </summary>
public sealed class MidiCommandMapper
{
    /// <summary>
    /// Custom mappings keyed by (channel, command, data1).
    /// </summary>
    private readonly Dictionary<(int Channel, int Command, int Data1), MidiCommand> _customMappings
        = new();

    /// <summary>
    /// Default CC number → command mapping (applied when no custom mapping exists).
    /// </summary>
    private readonly Dictionary<int, MidiCommand> _ccMappings = new()
    {
        // Common CC assignments
        [0x00] = MidiCommand.MasterVolume,   // Bank Select (MSB) – repurposed as master volume
        [0x07] = MidiCommand.MasterVolume,   // Volume
        [0x0A] = MidiCommand.VolumeUp,       // Pan – repurposed
        [0x2B] = MidiCommand.ToggleLoop,     // Effects 5 – common loop toggle
        [0x2C] = MidiCommand.PlayPause,      // Effects 6 – common play/pause
        [0x2D] = MidiCommand.Stop,           // Effects 7 – common stop
        [0x2E] = MidiCommand.NextTrack,      // Effects 8 – common next
        [0x2F] = MidiCommand.PreviousTrack,  // Effects 9 – common previous
        [0x30] = MidiCommand.Restart,        // Effects 10 – common restart
        [0x31] = MidiCommand.Seek,           // Effects 11 – common seek
        [0x32] = MidiCommand.LoopIn,         // Effects 12
        [0x33] = MidiCommand.LoopOut,        // Effects 13
        [0x34] = MidiCommand.ToggleMute,     // Effects 14
        [0x35] = MidiCommand.ToggleSolo,     // Effects 15
        [0x36] = MidiCommand.OpenWindow,     // Effects 16
        [0x37] = MidiCommand.CloseWindow,    // Effects 17
    };

    /// <summary>
    /// Registers a custom mapping for a specific (channel, command, data1) combination.
    /// </summary>
    public void AddCustomMapping(int channel, int command, int data1, MidiCommand midiCommand)
        => this._customMappings[(channel, command, data1)] = midiCommand;

    /// <summary>
    /// Removes a custom mapping.
    /// </summary>
    public void RemoveCustomMapping(int channel, int command, int data1)
        => this._customMappings.Remove((channel, command, data1));

    /// <summary>
    /// Returns all registered custom mappings (for UI display).
    /// </summary>
    public IReadOnlyCollection<(int Channel, int Command, int Data1, MidiCommand MappedCommand)> GetCustomMappings()
        => this._customMappings.Select(kv => (kv.Key.Channel, kv.Key.Command, kv.Key.Data1, kv.Value)).ToList();

    /// <summary>
    /// Maps a received MIDI message to a <see cref="MidiCommand"/>.
    /// Returns <see cref="MidiCommand.None"/> if no mapping applies.
    /// </summary>
    public MidiCommand Map(MidiMessageInfo message)
    {
        // 1. Check custom mappings first
        var key = (message.Channel, message.Command, message.Data1);
        if (this._customMappings.TryGetValue(key, out var custom))
            return custom;

        // 2. Note On (velocity > 0) → Play
        if (message.Command == 0x90 && message.Data2 > 0)
            return MidiCommand.Play;

        // 3. Note Off → Stop
        if (message.Command == 0x80)
            return MidiCommand.Stop;

        // 4. Control Change → look up CC mapping
        if (message.Command == 0xB0)
        {
            if (this._ccMappings.TryGetValue(message.Data1, out var ccCommand))
                return ccCommand;
        }

        return MidiCommand.None;
    }
}
