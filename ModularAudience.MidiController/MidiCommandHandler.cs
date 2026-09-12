using ModularAudience.MidiController.Models;

namespace ModularAudience.MidiController;

/// <summary>
/// Base class for handling mapped MIDI commands.
/// Subclasses implement the actual application logic (playback, looping, UI, etc.).
/// </summary>
public abstract class MidiCommandHandler
{
    /// <summary>
    /// The MIDI controller service this handler is bound to.
    /// </summary>
    protected MidiControllerService Service { get; }

    /// <summary>
    /// The command mapper used to translate raw messages to commands.
    /// </summary>
    protected MidiCommandMapper Mapper { get; }

    protected MidiCommandHandler(MidiControllerService service, MidiCommandMapper mapper)
    {
        this.Service = service;
        this.Mapper = mapper;
    }

    /// <summary>
    /// Wires the handler to the service's MessageReceived event.
    /// </summary>
    public void Start()
    {
        this.Service.MessageReceived += this.OnMessageReceived;
    }

    /// <summary>
    /// Unwires the handler from the service.
    /// </summary>
    public void Stop()
    {
        this.Service.MessageReceived -= this.OnMessageReceived;
    }

    private void OnMessageReceived(object? sender, MidiMessageInfo message)
    {
        var command = this.Mapper.Map(message);
        if (command == MidiCommand.None)
            return;

        this.HandleCommand(command, message);
    }

    /// <summary>
    /// Handles a mapped MIDI command. Subclasses override this to implement behavior.
    /// </summary>
    protected abstract void HandleCommand(MidiCommand command, MidiMessageInfo message);
}
