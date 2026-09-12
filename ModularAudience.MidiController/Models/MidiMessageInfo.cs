namespace ModularAudience.MidiController.Models;

/// <summary>
/// A parsed MIDI short message received from a controller.
/// </summary>
public sealed class MidiMessageInfo
{
    /// <summary>Channel (0–15).</summary>
    public int Channel { get; init; }

    /// <summary>Command status byte (e.g. 0x90 Note On, 0xB0 Control Change).</summary>
    public int Command { get; init; }

    /// <summary>First data byte (e.g. note number, CC number).</summary>
    public int Data1 { get; init; }

    /// <summary>Second data byte (e.g. velocity, CC value).</summary>
    public int Data2 { get; init; }

    /// <summary>Timestamp from when the port was opened.</summary>
    public TimeSpan Timestamp { get; init; }

    /// <summary>Raw 32-bit message.</summary>
    public uint RawMessage { get; init; }

    public override string ToString()
        => $"Ch{this.Channel} Cmd=0x{this.Command:X2} D1={this.Data1} D2={this.Data2}";
}
