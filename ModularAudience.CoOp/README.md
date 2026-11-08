# Co-op collections

`ModularAudience.CoOp` provides TCP hosting/joining, LAN discovery, collection
synchronization, and Windows firewall preparation. It references the audio
model, not WinForms; `CoOpUiController` in the Forms project connects its events
to collection windows.

## Using co-op

1. Right-click **Import** and select **Host co-op**. The application displays an
   address such as `192.168.1.20:47823`, which can also be copied from that menu.
2. On another computer, right-click **Import**. The menu scans the LAN; select a
   discovered host and **Connect co-op**, or use **Connect to address...**.
3. Right-click a collection's track list and check **Share Collection**. Local
   shared collections and their remote mirrors have green window borders.

Collections may be shared before anyone connects. A joining client automatically
receives every currently shared collection, including empty collections, using
its current content. Audio removed before that client's arrival is not sent as
a deleted item. No re-sharing is necessary after someone connects.

Any connected peer can add or edit audio in a shared collection. The host relays
those changes to the collection's original owner and every other connected
client. This also applies to collections originally shared by a client, and to
audio newly created in another peer's collection. Track identity is retained;
existing audio objects are updated in place. Pending outgoing transfers are
shown grey with a clock icon.

## Retaining local control

- An owner's removal marks the corresponding remote audio red with
  `[deleted by <owner>]`. Its last synchronized samples remain available locally.
- Removing audio from a remote mirror is a local discard only: it does not
  remove that audio from the owner, other peers, or the host's late-join state.
- Editing retained deleted audio stays local and cannot restore a removed item
  on its owner. The owner can explicitly re-add an item to restore it.
- Un-sharing or disconnecting marks remote collections red instead of deleting
  their contents. Local edits in an unshared collection are no longer relayed.
- Re-sharing reconciles the current membership without disposing missing remote
  audio: those items remain as tombstones for existing peers, but are omitted
  from later clients' initial state.
- Starting or joining another session does not discard previously received
  collections. Retention is in memory until application exit; export any audio
  that should persist between application runs.

## Protocol and implementation

- Host-relay topology; multiple TCP clients can connect to one host.
- Length-prefixed, polymorphic JSON messages. Each audio snapshot contains
  metadata and raw 32-bit floating-point samples encoded as base64. Updates
  transfer complete snapshots, not sample-level diffs.
- Property notifications and explicit `BindingList` resets detect changes.
  Property edits are coalesced for 150 ms; notification suppression prevents
  received updates from echoing back into the session.
- Code mutating an existing sample array, beat-grid array, or the metrics
  dictionary in place must call `Audios.ResetItem(index)` or `ResetBindings()`
  afterward. Replacing properties and using the audio metric indexer already
  emit content notifications.
- A serialized session gate orders protocol mutations; an optional
  `SynchronizationContext` applies incoming changes on the UI thread.
- Revision acknowledgements clear pending indicators without allowing an older
  acknowledgement to clear a newer outgoing edit.
- Remote mirrors retain session snapshots independently of locally discarded
  UI items, so a host's local discard cannot remove another owner's audio from
  subsequent clients' initial state.
- Concurrent edits use arrival order at the host; there is no conflict merging,
  synchronized transport/playback, or editor-window collaboration yet.

## Networking and security

The host tries TCP ports **47823–47842**. Discovery uses one available UDP port
in **47824–47828**; menu scans probe the entire range and de-duplicate responses.
Different local host instances can use different ports.

Before hosting, Windows firewall preparation checks the named inbound TCP/UDP
rules. If adding a missing rule without elevation fails, a `runas` operation
requests UAC elevation. Rejecting UAC does not prevent local hosting, but incoming
connections may remain blocked; the failure is logged. Organization policies,
router/NAT configuration, network isolation, and outbound firewall restrictions
cannot be bypassed by this helper.

This first iteration has **no authentication or encryption**. Use it only with
trusted peers on a trusted LAN; do not expose its ports directly to the Internet.
Individual serialized frames are limited to 768 MiB; large tracks also require
memory for their snapshots and JSON/base64 representation.

## Validation

```powershell
dotnet test ModularAudience.CoOp.Tests\ModularAudience.CoOp.Tests.csproj --nologo -v:m
dotnet test ModularAudience.Audio.Tests\ModularAudience.Audio.Tests.csproj --nologo -v:m
dotnet build ModularAudience.Forms\ModularAudience.Forms.csproj --nologo -v:m
```

Tests cover framed messages and concurrent sends, endpoint parsing, snapshot
mapping, change tracking, host/client/peer additions and edits, late joining,
tombstone retention, re-sharing, reconnecting, LAN discovery, and firewall
argument generation. The Audio test project also checks background content
updates in a bound collection view and borrowed collection ownership.

For a real two-computer smoke test, verify discovery, accepting/cancelling the
UAC prompt, late joining after multiple collections were pre-shared, additions
from both computers, red retained deletions, and red borders after un-sharing.
Unit tests do not alter the Windows firewall or accept UAC prompts.
