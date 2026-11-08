# Co-op collections

`ModularAudience.CoOp` provides TCP hosting/joining, LAN discovery, collection
synchronization, and Windows firewall preparation. It references the audio
model, not WinForms; `CoOpUiController` in the Forms project connects its events
to collection windows.

## Using co-op

1. Right-click **Import** and select **Host co-op**. The application displays an
   address such as `192.168.1.20:47823`, which can also be copied from that menu.
2. On another computer, right-click **Import**. The menu scans the LAN; selecting
   a discovered host in the combo box connects immediately. Alternatively, use
   the single **Connect to address...** action for a host name or IP address.
   Populating the combo box does not select or connect to a host automatically.
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

Receiving, editing, relaying, and retaining deleted tracks keep their samples
only in application RAM (`float[]` plus in-memory synchronization snapshots).
Co-op does not save temporary audio files or an on-disk sample cache. A received
`FilePath` is metadata, not a local file that the receiver opens or creates.
Audio files are written only by an explicit export/save action; merely creating
the workspace's empty export/recording directories does not persist samples.

## Audio exports and recordings

Received collections use the same user-facing workspace as local collections:
the Windows **Music** known folder, then `ModularAudience\_Exports` and
`ModularAudience\_Recordings`. A co-op collection ID does not add a GUID-named
subdirectory, and the sender's file paths do not determine the recipient's
export destination. Temporary/AppData directories are not a default export
fallback. Explicit custom working directories remain supported.

If the main directory is changed with WindowMain's **[...]** folder picker,
default co-op mirrors follow that application directory as well, including
mirrors already open. The selected path is restored from application preferences
on startup and shared with other local application instances; it is not sent to
network peers. Explicit session working-directory overrides stay independent.

Shift-exporting a collection reuses `_Exports\<Collection name>` on subsequent
exports. Existing files are retained; new name collisions become `Sample (1)`,
`Sample (2)`, and so on. This applies to WAV and MP3, including parallel exports.
Previously created temporary export folders, numbered collection folders, and
legacy `_Records` directories are not moved or deleted automatically.

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

### Local networks and mobile hotspots

Co-op uses direct TCP sockets between the clients and the selected host, not
GitHub, HTTP proxies, cloud relays, or a WAN rendezvous service. Discovery uses
the source IP of a local UDP reply, not a remote host's advertised Internet
address. Probes and replies have an IP TTL of one; directed broadcasts target
known non-tunnel local IPv4 subnets. The advertised host address prefers an
active Ethernet/Wi-Fi adapter with a gateway over dedicated tunnel/PPP adapters
and virtual adapters without a gateway.

If a manually entered host name resolves to both on-subnet/loopback addresses
and other addresses, connections are restricted to those local candidates.
A failed local connection does not silently fall back to a public address.
An explicitly entered public IP (or a name with no known local candidate) is
still a direct connection to that requested endpoint and can use the Internet.
The log reports the actual local and remote TCP endpoints for diagnosis.

When both PCs and the host use their local addresses on the same hotspot, the
traffic normally stays inside that Wi-Fi/LAN and does not traverse the mobile
uplink or consume cellular data volume. For example, connect to the host's
`192.168.43.x` address rather than its public Internet address. Windows still
owns routing: VPN policies, unusual routes, guest/client isolation, or hotspot
restrictions can alter/block local communication. The application does not
change routes, bypass isolation, or guarantee a provider's billing behavior.
GitHub update checks and release downloads are separate Internet traffic and
can consume mobile data.

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
