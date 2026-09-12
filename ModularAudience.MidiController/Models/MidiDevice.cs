namespace ModularAudience.MidiController.Models;

/// <summary>
/// Represents a MIDI device (input or output) discovered on the system.
/// </summary>
public sealed class MidiDevice
{
    /// <summary>Unique device identifier (MIDI port number).</summary>
    public int Id { get; init; }

    /// <summary>Human-readable device name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Whether this device can receive MIDI messages.</summary>
    public bool IsInput { get; init; }

    /// <summary>Whether this device can send MIDI messages.</summary>
    public bool IsOutput { get; init; }

    public override string ToString() => this.Name;
}
