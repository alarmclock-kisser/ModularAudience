using ModularAudience.Audio;
using ModularAudience.MidiController.Models;

namespace ModularAudience.MidiController;

/// <summary>
/// Example handler that maps MIDI commands to playback actions on an <see cref="AudioObj"/>.
/// </summary>
public sealed class PlaybackCommandHandler : MidiCommandHandler
{
    private readonly AudioObj _audioObj;

    public PlaybackCommandHandler(MidiControllerService service, MidiCommandMapper mapper, AudioObj audioObj)
        : base(service, mapper)
    {
        this._audioObj = audioObj;
    }

    protected override void HandleCommand(MidiCommand command, MidiMessageInfo message)
    {
        switch (command)
        {
            case MidiCommand.Play:
                _ = this._audioObj.PlayAsync(default);
                break;

            case MidiCommand.Stop:
                _ = this._audioObj.StopAsync();
                break;

            case MidiCommand.Pause:
            case MidiCommand.PlayPause:
                _ = this._audioObj.PauseAsync();
                break;

            case MidiCommand.Restart:
                this._audioObj.Seek(0);
                break;

            case MidiCommand.Seek:
                // Use Data2 as a percentage (0–127) → seek to that fraction of the track
                if (this._audioObj.Duration > TimeSpan.Zero)
                {
                    double fraction = message.Data2 / 127.0;
                    this._audioObj.Seek((double) this._audioObj.Duration.TotalSeconds * fraction);
                }
                break;

            case MidiCommand.ToggleLoop:
                this._audioObj.LoopEnabled = !this._audioObj.LoopEnabled;
                break;

            case MidiCommand.VolumeUp:
                this._audioObj.SetVolume(Math.Min(1.0f, this._audioObj.Volume / 100f + 0.05f));
                break;

            case MidiCommand.VolumeDown:
                this._audioObj.SetVolume(Math.Max(0.0f, this._audioObj.Volume / 100f - 0.05f));
                break;

            case MidiCommand.MasterVolume:
                this._audioObj.SetVolume(message.Data2 / 127f);
                break;

            case MidiCommand.NextTrack:
            case MidiCommand.PreviousTrack:
                // These require playlist-level access; wire up via a callback or
                // pass a reference to the PlaylistEngine / WindowMain here.
                break;

            default:
                break;
        }
    }
}
