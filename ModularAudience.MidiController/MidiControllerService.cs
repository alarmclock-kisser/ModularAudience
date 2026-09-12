using NAudio.Midi;
using ModularAudience.MidiController.Models;

namespace ModularAudience.MidiController;

/// <summary>
/// Manages MIDI device enumeration, opening of input/output ports,
/// and dispatches received messages to subscribers.
/// </summary>
public sealed class MidiControllerService : IDisposable
{
    private MidiIn? _input;
    private MidiOut? _output;
    private bool _disposed;

    /// <summary>Raised when a short MIDI message is received (non-UI thread).</summary>
    public event EventHandler<MidiMessageInfo>? MessageReceived;

    /// <summary>Raised when a sysex message is received (non-UI thread).</summary>
    public event EventHandler<byte[]>? SysexReceived;

    /// <summary>Raised when a MIDI device is plugged in or removed.</summary>
    public event EventHandler<MidiDevice>? DeviceChanged;

    /// <summary>Currently open input device, if any.</summary>
    public MidiDevice? InputDevice { get; private set; }

    /// <summary>Currently open output device, if any.</summary>
    public MidiDevice? OutputDevice { get; private set; }

    /// <summary>
    /// Enumerates all MIDI input and output devices on the system.
    /// </summary>
    public static async Task<List<MidiDevice>> GetDevicesAsync()
    {
        var devices = new List<MidiDevice>();

        try
        {
            for (int i = 0; i < MidiIn.NumberOfDevices; i++)
            {
                var caps = MidiIn.DeviceInfo(i);
                devices.Add(new MidiDevice { Id = i, Name = caps.ProductName, IsInput = true });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MidiController: input enumeration failed: {ex.Message}");
        }

        try
        {
            for (int i = 0; i < MidiOut.NumberOfDevices; i++)
            {
                var caps = MidiOut.DeviceInfo(i);
                devices.Add(new MidiDevice { Id = i, Name = caps.ProductName, IsOutput = true });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MidiController: output enumeration failed: {ex.Message}");
        }

        return await Task.FromResult(devices);
    }

    /// <summary>
    /// Opens the MIDI input port for the given device and starts listening.
    /// </summary>
    public async Task<bool> OpenInputAsync(MidiDevice device)
    {
        try
        {
            this.CloseInput();
            this._input = new MidiIn(device.Id);
            this._input.MessageReceived += this.OnInputMessageReceived;
            this._input.SysexMessageReceived += this.OnInputSysexReceived;
            this._input.Start();
            this.InputDevice = device;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MidiController: failed to open input '{device.Name}': {ex.Message}");
            this._input?.Dispose();
            this._input = null;
            return false;
        }
    }

    /// <summary>
    /// Opens the MIDI output port for the given device.
    /// </summary>
    public async Task<bool> OpenOutputAsync(MidiDevice device)
    {
        try
        {
            this.CloseOutput();
            this._output = new MidiOut(device.Id);
            this.OutputDevice = device;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MidiController: failed to open output '{device.Name}': {ex.Message}");
            this._output?.Dispose();
            this._output = null;
            return false;
        }
    }

    /// <summary>Sends a MIDI event to the open output port.</summary>
    public bool Send(MidiEvent midiEvent)
    {
        if (this._output is null) return false;
        try
        {
            this._output.Send(midiEvent.GetAsShortMessage());
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MidiController: send failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Sends a sysex buffer to the open output port.</summary>
    public bool SendBuffer(byte[] buffer)
    {
        if (this._output is null) return false;
        try
        {
            this._output.SendBuffer(buffer);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MidiController: send buffer failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Sends a raw 32-bit MIDI short message to the open output port.</summary>
    public bool SendRaw(uint rawMessage)
    {
        if (this._output is null) return false;
        try
        {
            var bytes = new byte[]
            {
                (byte)((rawMessage >> 24) & 0xFF),
                (byte)((rawMessage >> 16) & 0xFF),
                (byte)((rawMessage >> 8)  & 0xFF),
                (byte)(rawMessage & 0xFF)
            };
            this._output.SendBuffer(bytes);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MidiController: send raw failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Stops and closes the input port.</summary>
    public void CloseInput()
    {
        if (this._input is null) return;
        try
        {
            this._input.MessageReceived -= this.OnInputMessageReceived;
            this._input.SysexMessageReceived -= this.OnInputSysexReceived;
            this._input.Stop();
            this._input.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MidiController: close input failed: {ex.Message}");
        }
        this._input = null;
        this.InputDevice = null;
    }

    /// <summary>Closes the output port.</summary>
    public void CloseOutput()
    {
        if (this._output is null) return;
        try
        {
            this._output.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MidiController: close output failed: {ex.Message}");
        }
        this._output = null;
        this.OutputDevice = null;
    }

    /// <summary>Closes both ports and disposes the service.</summary>
    public void Dispose()
    {
        if (this._disposed) return;
        this._disposed = true;
        this.CloseInput();
        this.CloseOutput();
    }

    private void OnInputMessageReceived(object? sender, MidiInMessageEventArgs e)
    {
        var info = ParseMessage((uint)e.RawMessage);
        MessageReceived?.Invoke(this, info);
    }

    private void OnInputSysexReceived(object? sender, MidiInSysexMessageEventArgs e)
    {
        SysexReceived?.Invoke(this, e.SysexBytes);
    }

    private static MidiMessageInfo ParseMessage(uint raw)
    {
        int status = (int)((raw >> 24) & 0xFF);
        int data1  = (int)((raw >> 16) & 0xFF);
        int data2  = (int)((raw >> 8)  & 0xFF);
        int channel = status & 0x0F;

        return new MidiMessageInfo
        {
            Channel    = channel,
            Command    = status & 0xF0,
            Data1      = data1,
            Data2      = data2,
            RawMessage = raw
        };
    }
}
