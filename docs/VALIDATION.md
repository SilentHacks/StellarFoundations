# Validation and support scope

## Supported environment

- StellarDrive Demo 0.8.2, Mono, Unity 6000.3.24f1.
- MelonLoader 0.7.3 and SDModKit 1.3.3.
- Shipping assembly: .NET Framework 4.7.2. Offline tests: .NET 9.

Assembly identities and inspected native contracts are checked. Unsupported
binaries or conflicting movement/save patches fail closed. A game update needs
revalidation rather than simply changing the version check.

## Release candidate 0.5.0, 16 October 2026

This candidate changes release identity, packaging and documentation, removes
unused interaction-card fields/text and fallback metal-material allocations, and
extracts the existing hint sentence join for regression coverage. All 108 offline
tests pass with warning-free Release compilation. It retains the 0.4.3 geometry, physics, transactions,
0.8 second holds, item ID 61240, save format 2 and protocol 4.

Release preparation builds and tests the committed revision, verifies manifest/DLL/
content metadata, and checks every ZIP entry against its source bytes. The manager
payload contains only `Mods/StellarFoundations.dll` and `mod.json`. Source revision,
DLL/ZIP hashes and the intended tag are recorded beside the ZIP.

The release thumbnail was captured in the native game from an isolated world copy,
using a staged camera and lighting. It shows the actual timber model. Capture
tools and test worlds are excluded from the release. All 74 original save files
and all 96 baseline files/backups were verified unchanged after capture.

The game was closed after media capture so release preparation could continue
without taking over the desktop. The 0.5.0 candidate has offline validation;
the native checks below ran against its 0.4.3 gameplay predecessor. Do not describe
those as native testing of the final 0.5.0 binary.

## Native gameplay evidence

0.4.3 passed 104 offline tests and warning-free Release compilation. Its exact
shipping DLL passed complete native interaction runs at **1920 x 1080** and
**2560 x 1080**, using fresh isolated save copies. Programmatic camera/input data
exercised the native builder, recycler, inventory, physics and host feedback.

- Ordinary placement and material shortages use native visual feedback without
  routine explanatory boxes.
- Hold actions and consequences share one native hint label with sentence
  punctuation and no wrapping at the tested resolutions.
- Covered-anchor targeting, cancellation/restart, short conversion/removal holds
  and floor preservation pass.
- Missing host responses time out after three seconds even while requests continue.
- Protected-floor rejection happens before recycler success effects/repeating.
- First-floor snapping, two dynamic collision contacts, fixed pose, conversion and
  paired native saving pass.
- Timber materials, rotated footing, selected-floor/contact markers and recycler
  highlights were inspected visually through the native render pipeline.

Earlier native runs covered a **5,556-part** conversion, save reload, automatic
backup pairing, crash recovery/export, damage, redundant supports, final release,
docking/undocking, rotor topology and local multiplayer transport/handshake checks.
These are historical checks, not a complete requalification of every later binary.

Every native mutation used an isolated world copy. Original worlds were hash-checked
against backups, private drivers were removed and test copies were archived.
No game, loader, SDK assemblies or private world data are distributed.

## Remaining qualification

0.5.0 is suitable for a labelled single-player preview of this inspected stack.
It is not a fully qualified co-op release. Remaining work includes:

- A real remote host/client session, including latency and disconnect/rejoin.
- Extended ordinary play, saving and world re-entry.
- Human controller input and the complete HUD-scale/resolution matrix.
- Compatibility with terrain-deformation mods and uninspected third-party patches.

No FPS or simulation-performance improvement is claimed.
