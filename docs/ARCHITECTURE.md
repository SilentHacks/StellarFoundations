# Architecture and development

## Runtime boundaries

The native ship remains the authoritative parts, inventory, cable, room and
machinery container. Grounding records stable ship/pier identities, the planet,
a double-precision planet-relative pose and validated support geometry in
SDModKit world data. Temporary floating-origin space IDs are not persisted.

Grounded roots retain a collidable kinematic Rigidbody with speculative collision
detection and finite native assembly mass. Root forces and grounded propulsion
are inhibited; native production, rotor simulation and damage callbacks remain.
Release is queued for the next physics boundary and resolves the current frame
then. Native graph selection prefers the grounded node. Live door/rotor traversal
permits visitor docking but rejects connections between independently grounded bases.

The host resolves the authenticated connection and checks ownership, distance,
geometry, materials, rate, sequence and revision. Commands contain no acting-player
identity. Conversion stillness is sampled at physics boundaries. The 0.8 second
conversion/release holds require uninterrupted observations of the same target;
movement, topology/revision changes, cancellation or missing observations reset them.

## Geometry, visuals and interaction

Contact anchors accept signed cap clearance from -0.1 to 0.2 metres. Larger gaps
extend a pier up to six metres. Bounded terrain queries filter planet hits
separately from hull/machinery obstruction checks. No hit or an exhausted query
buffer is a rejection. Preview and authority share validation.

The collar is exposed above an existing floor and inherits native part identity.
Converted supports depend on an exact floor coordinate. Recycling that floor is
blocked until its support is removed; native damage removes the dependent support
without a refund. Starter foundations remain independent of the first floor added.

The wooden hull instancer supplies panel/edge meshes, UVs, vertex colours and native
texture-array materials at runtime. A cached timber panel supplies square caps,
post and foot. Preview and construction share this assembly, while the footing
preserves heading projected onto the terrain normal. Primitive frames define the
existing collider dimensions; they do not allocate fallback metal materials.
Visual details have no enabled colliders. Recycler highlights use per-instance mesh
clones with the native hull palette and restore them on deselection.

Recycler targeting resolves the first native ray hit, preserving range, layers and
occlusion. A direct anchor hit or its exact mounting footprint through a floor maps
to one logical ship/part target for highlights, hints and removal. Physical colliders
are not enlarged. Other floors, walls and objects retain native selection. Removal
stops automatic recycling so the floor remains until another press.

Only the last support of an entity with other native parts requires a release hold.
This is a live host PartCount decision, not client permission. Docking/rotor
connectors count as native payload. Protocol 4 carries the warning decision explicitly.

The HUD adapts the existing ToolKeyTipUi glyph, font, canvas and scaling. Routine
placement, shortages, inspection and completion rely on native costs, holograms,
effects and bottom hints. Hold actions include their consequence in one punctuated
label. A compact progress bar appears only while holding; only attempted failures
and an active moving conversion add unboxed text. The inspected optional Build Plus
adapter is MVID-guarded and suspends conflicting drag/placement-lock behavior only
during this tool's controls. Critical startup/load errors retain a fallback display.

Support markers are pooled, bounded to 32 within 30 metres and refreshed at 5 Hz
only while aiming the foundation tool at the relevant structure. Previews and
markers disappear on tool/menu/world changes.

## Network and save safety

Replies carry typed phase, duration, support count, warning mode and host progress.
The sequence window rejects stale/cancelled feedback. Interpolation is limited to
one observation interval and cannot show completion early. Snapshot/reply ordering
cannot cancel a successful action against its own updated target. Only accepted
host replies extend the three-second response deadline; repeated requests cannot.

Native authentication/spawn waits for compatible clients to install and acknowledge
the grounding snapshot. Bounded ASCII chunks avoid Steam pre-authentication split
packet handling. Snapshot generations discard stale state and permit join resync.
Every participant must use the same mod version.

Attachment format 2 stores support kind, signed clearance, floor dependency and
collar presence. Validated format-1 piers migrate with their original geometry.
Part ID 61240, asset SilentHacks.FoundationPier and persistent metadata keys remain
stable across the rename to StellarFoundations.

Saving captures native serializable state and main-thread quaternion Euler values
before worker serialization. A durable rollback journal and SHA-256 pairing marker
join native world data with all SDModKit metadata, including other mods' records.
Automatic backup sidecars remain paired with their native backups. Failed captures
retain previous complete files. Export saves a separate copy without foundations;
it does not strip the original world.

Compatibility checks cover inspected assembly identities and movement/save patch
conflicts. An early load gate rejects orphan foundations before ship creation.
Runtime faults stop foundation actions/new saves while retaining control of already
grounded roots. Invalid metadata aborts loading and returns to the menu without saving.

## Build and test

Use .NET SDK 9.0.101. Game, loader and SDModKit references resolve externally and
must not be committed or distributed. Set `GameDirectory` through an MSBuild
property, `STELLARDRIVE_GAME_DIRECTORY` or ignored `Directory.Build.local.props`
when the default Steam directory differs.

```powershell
dotnet build StellarFoundations.sln -c Release
dotnet test tests/StellarFoundations.Tests/StellarFoundations.Tests.csproj -c Release --no-build
```

Shipping targets net472. Tests target net9.0 and exercise managed policy, recovery,
exports and native contracts without installing Harmony detours in CoreCLR.
Private runtime drivers, worlds, backups and evidence remain outside this repository.
Only curated release screenshots belong in `assets/`.

## Package and release

Commit the final source on `main`, then run:

```powershell
./scripts/Package.ps1
```

The script requires a clean checkout, builds/tests that revision, checks release
identity and produces `artifacts/0.5.0/StellarFoundations.zip`. ZIP entries are
`Mods/StellarFoundations.dll` and root `mod.json`, matching the manager's deployment
relative to the game directory. The MIT license is embedded in the DLL. README,
release notes, media and developer docs stay in the repository. Deterministic entry
timestamps and a byte-for-byte verification produce a checksum and provenance
receipt beside the ZIP. Packaging does not deploy, launch or publish.

Public distribution requires a public repository, `main/mod.json`, tag `v0.5.0`
and release asset `StellarFoundations.zip`. Use `RELEASE_NOTES.md` for the release
body and mark this version as a preview. The manifest's `thumbnail` is relative to
the repository root. Catalog inclusion also requires the mod manager's registry
to list the repository; a GitHub release alone does not register it.

Publishing is a separate action after reviewing the prepared artifacts. Local
installation is also separate: close the game and verify a complete save backup
before changing `Mods/StellarFoundations.dll`.
