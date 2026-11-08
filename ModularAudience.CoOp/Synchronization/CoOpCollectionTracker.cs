using System.Collections.Concurrent;
using System.ComponentModel;
using ModularAudience.Audio;

namespace ModularAudience.CoOp.Synchronization;

public sealed class CoOpAudioItemEventArgs(AudioObj audio) : EventArgs
{
    public AudioObj Audio { get; } = audio;
}

/// <summary>
/// Watches the <see cref="BindingList{T}"/> of an <see cref="AudioCollection"/>
/// and reports item additions, changes and removals. Remote applications are
/// wrapped in <see cref="SuppressNotifications"/> so they do not echo back.
/// Property edits are debounced so a batch of field updates (for example when
/// a track editor applies a change) results in a single synchronization event.
/// </summary>
internal sealed class CoOpCollectionTracker : IDisposable
{
    private static readonly TimeSpan ChangeDebounce = TimeSpan.FromMilliseconds(150);

    private readonly AudioCollection collection;
    private readonly ConcurrentDictionary<Guid, AudioObj> known = new();
    private readonly ConcurrentDictionary<Guid, AudioObj> pendingChanges = new();
    private readonly Timer debounceTimer;
    private int suppression;
    private volatile bool disposed;

    public CoOpCollectionTracker(AudioCollection collection)
    {
        this.collection = collection ?? throw new ArgumentNullException(nameof(collection));
        foreach (AudioObj audio in collection.Audios)
        {
            this.Track(audio);
        }

        this.debounceTimer = new Timer(this.DebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
        collection.Audios.ListChanged += this.Audios_ListChanged;
    }

    public event EventHandler<CoOpAudioItemEventArgs>? ItemAdded;

    public event EventHandler<CoOpAudioItemEventArgs>? ItemChanged;

    public event EventHandler<CoOpAudioItemEventArgs>? ItemRemoved;

    public IDisposable SuppressNotifications()
    {
        Interlocked.Increment(ref this.suppression);
        return new SuppressionScope(this);
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.collection.Audios.ListChanged -= this.Audios_ListChanged;
        this.debounceTimer.Dispose();
        this.pendingChanges.Clear();

        foreach (AudioObj audio in this.known.Values)
        {
            audio.PropertyChanged -= this.Audio_PropertyChanged;
        }

        this.known.Clear();
    }

    private void Track(AudioObj audio)
    {
        if (this.known.TryGetValue(audio.Id, out AudioObj? previous))
        {
            if (ReferenceEquals(previous, audio))
            {
                return;
            }

            previous.PropertyChanged -= this.Audio_PropertyChanged;
        }

        this.known[audio.Id] = audio;
        audio.PropertyChanged -= this.Audio_PropertyChanged;
        audio.PropertyChanged += this.Audio_PropertyChanged;
    }

    private void Untrack(Guid audioId, AudioObj audio)
    {
        audio.PropertyChanged -= this.Audio_PropertyChanged;
        this.known.TryRemove(audioId, out _);
        this.pendingChanges.TryRemove(audioId, out _);
    }

    private void Audios_ListChanged(object? sender, ListChangedEventArgs e)
    {
        if (this.disposed)
        {
            return;
        }

        switch (e.ListChangedType)
        {
            case ListChangedType.ItemAdded:
                this.HandleAdded(e.NewIndex);
                break;
            case ListChangedType.ItemChanged:
                this.HandleChanged(e.NewIndex, e.PropertyDescriptor);
                break;
            case ListChangedType.ItemDeleted:
                this.HandleRemoved();
                break;
            case ListChangedType.Reset:
                this.HandleReset();
                break;
        }
    }

    private void Audio_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (this.disposed || sender is not AudioObj audio)
        {
            return;
        }

        if (!this.known.TryGetValue(audio.Id, out AudioObj? tracked) || !ReferenceEquals(tracked, audio))
        {
            return;
        }

        this.ScheduleChanged(audio);
    }

    private void HandleAdded(int index)
    {
        if (index < 0 || index >= this.collection.Audios.Count)
        {
            return;
        }

        AudioObj audio = this.collection.Audios[index];
        this.Track(audio);
        this.Raise(this.ItemAdded, audio);
    }

    private void HandleChanged(int index, PropertyDescriptor? propertyDescriptor)
    {
        if (index < 0 || index >= this.collection.Audios.Count)
        {
            return;
        }

        AudioObj audio = this.collection.Audios[index];

        // BindingList raises ItemChanged for INotifyPropertyChanged items too.
        // Those are handled by the direct PropertyChanged subscription, which
        // coalesces rapid edits. Only explicit ResetItem calls and index
        // replacements need handling here.
        if (propertyDescriptor == null)
        {
            this.HandleRemoved();
        }

        bool isNew = !this.known.ContainsKey(audio.Id);
        this.Track(audio);

        if (propertyDescriptor == null)
        {
            if (isNew)
            {
                this.Raise(this.ItemAdded, audio);
            }
            else
            {
                this.ScheduleChanged(audio);
            }
        }
    }

    private void HandleRemoved()
    {
        HashSet<Guid> current = this.collection.Audios.Select(audio => audio.Id).ToHashSet();
        foreach ((Guid id, AudioObj audio) in this.known.ToList())
        {
            if (current.Contains(id))
            {
                continue;
            }

            this.Untrack(id, audio);
            this.Raise(this.ItemRemoved, audio);
        }
    }

    private void HandleReset()
    {
        this.HandleRemoved();

        foreach (AudioObj audio in this.collection.Audios)
        {
            bool isNew = !this.known.ContainsKey(audio.Id);
            this.Track(audio);
            if (isNew)
            {
                this.Raise(this.ItemAdded, audio);
            }
            else
            {
                this.ScheduleChanged(audio);
            }
        }
    }

    private void ScheduleChanged(AudioObj audio)
    {
        if (this.disposed || Volatile.Read(ref this.suppression) > 0)
        {
            return;
        }

        this.pendingChanges[audio.Id] = audio;
        try
        {
            this.debounceTimer.Change(ChangeDebounce, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // The tracker is shutting down.
        }
    }

    private void DebounceElapsed(object? state)
    {
        if (this.disposed)
        {
            return;
        }

        foreach (Guid audioId in this.pendingChanges.Keys)
        {
            if (!this.pendingChanges.TryRemove(audioId, out AudioObj? audio))
            {
                continue;
            }

            if (!this.known.TryGetValue(audioId, out AudioObj? tracked) || !ReferenceEquals(tracked, audio))
            {
                continue;
            }

            this.Raise(this.ItemChanged, audio);
        }
    }

    private void Raise(EventHandler<CoOpAudioItemEventArgs>? handler, AudioObj audio)
    {
        if (this.disposed || handler == null || Volatile.Read(ref this.suppression) > 0)
        {
            return;
        }

        handler(this, new CoOpAudioItemEventArgs(audio));
    }

    private sealed class SuppressionScope(CoOpCollectionTracker tracker) : IDisposable
    {
        private CoOpCollectionTracker? tracker = tracker;

        public void Dispose()
        {
            CoOpCollectionTracker? current = Interlocked.Exchange(ref this.tracker, null);
            if (current != null)
            {
                Interlocked.Decrement(ref current.suppression);
            }
        }
    }
}
