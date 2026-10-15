# Runtime boundaries

The native ship remains the authoritative inventory, parts, cables, rooms and
machinery container. The host stores a stable grounding identity, native ship and
pier identities, planet identity, double-precision planet-relative root position,
rotation and validated pier geometry in SDModKit world data. Temporary floating
origin space IDs are never the persistent attachment identity.

The root-physics prefix replaces root forces for grounded entities with a
collidable kinematic pose. It preserves native Rigidbody creation, uses
ContinuousSpeculative collision detection, and keeps finite native assembly mass
for impact damage. Native damage callbacks continue; contact lists are cleared
even when destruction exits early. Release is queued for the next physics
boundary, resolving the current reference frame at that point.

Native graph construction prefers the grounded node over mass-based selection on
both host and clients. Dock validation traverses live door and rotor connections,
including connections made before the next graph rebuild. Two independent ground
attachments are refused. Native rotor simulation and normal entity membership
remain in place.

Commands contain no acting-player identity. The host resolves the authenticated
connection, checks ownership, reach, geometry, revisions, material cost, rate and
sequence. Conversion stillness is sampled at physics boundaries, including gaps
between command packets. Topology edits cancel pending confirmations. Grounding
and removal confirmations require uninterrupted observations of the same target.

The native authentication/spawn path is delayed until a compatible client has
installed and acknowledged the grounding snapshot. Small ASCII snapshot chunks
avoid native Steam pre-authentication Split-packet handling. Reliable generation
numbers discard stale snapshots and permit resynchronization during a join.

The save wrapper captures native serializable state and main-thread quaternion
Euler values before handing serialization to a worker. It pairs the native world
with SDModKit metadata, including unrelated mod metadata, through a durable
rollback journal and SHA-256 pairing marker. Native backup sidecars travel with
their matching native backup. Failed captures retain the previous complete files.

Compatibility checks cover inspected assembly MVIDs and movement/save-hook
conflicts. A separately installed load gate rejects orphan piers before creating
their native ship. A runtime fault stops foundation actions and new saves while
existing grounded roots remain controlled. Invalid world metadata stops loading
and returns to the menu without saving.

Build, tests, packaging and closed-game deployment are separate operations.
Private validation helpers are not a runtime dependency or a package component.
