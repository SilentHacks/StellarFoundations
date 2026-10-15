# Local validation — 15 October 2026

This is a local 0.1.0 preview. All live changes used isolated save copies. The
automated drivers are private tools and are absent from the shipping DLL/package.
Normal game launches do not run tests, load a test world or quit automatically.

## Stage 1: placement and native physics

- Startup, part registration and the four-wood recipe passed in the installed
  MelonLoader 0.7.3 / SDModKit 1.3.3 stack.
- Authoritative placement used real terrain validation and the registered server
  command handler. The request was serialized/deserialized before dispatch.
- The native build-preview detector snapped a first floor onto the mounting grid;
  the native floor placement RPC added it.
- A real dynamic collision probe contacted the new base without moving its
  canonical pose. Kinematic bodies and enabled collisions were asserted.
- Native material inspection and screenshots exposed and corrected incompatible
  default materials and an initially incorrect first-floor mesh orientation.

## Stage 2: persistence and networking

- Native paired save, process restart and fixed-pose restoration passed.
- Native automatic backup produced a hash-matched metadata sidecar; loading the
  backup retained its world data through world entry.
- An intentionally mismatched copy was rejected before world entry and returned
  to the menu. Both rejected files remained byte-for-byte unchanged.
- Portable-copy export passed. That copy loaded with Foundations initialization
  disabled and contained no foundation parts. Source attachments remained intact.
- Separate headless game processes tested actual loopback transport: host, two
  matching clients including a late join, and an initialization-disabled client
  rejected before authentication. This is a protocol test, not rendered co-op QA.
- Steam-hosted tests exposed the shipped transport's pre-authentication Split
  handling once a snapshot grew beyond one packet. Small snapshot chunks fixed
  the failure and subsequent Steam-hosted captures passed.

## Stage 3: conversion and interactions

- An existing 430-part structure converted through the authoritative hold, keeping
  existing native part objects and its pose.
- The copied 5,556-part structure converted with all 4,964 hull parts and 592
  stateful parts preserved. Loading its physics region exposed and corrected
  placement's selection of the wrong scene on planets with multiple regions.
- The final-pier warning hold and return to native dynamic physics passed.
- A 108-mass visitor docked to a 28.1-mass grounded base. The base remained root
  after native joint rebuilding. A same-tick bridge to a second independent base
  was rejected. Switching off the native magnet signal undocked the visitor while
  the base stayed fixed.
- An additional pier attached successfully. Native damage removal of one support
  retained grounding; removal of the final support restored dynamic physics.
- A native graph out-parameter binding bug discovered by these tests was fixed.

## Stage 4: qualification and limitations

- 46 offline tests cover policy, interruption and replay, bounded coordinates, metadata integrity,
  recovery journals, damaged recovery backups, portable-copy preservation, wire
  serialization, native contracts and grounded graph selection. No Harmony or
  MonoMod detours are installed in the CoreCLR test process.
- Runtime tests used the user's existing mod stack. A separate test activated
  Workshop's world-scoped saving on the isolated copy. It exposed competing save
  captures; a guarded adapter now gives Foundations sole ownership of paired saves
  while retaining Workshop's behavior on worlds without grounding data. The
  qualified Workshop binary has MVID `40054375-01b2-4786-8f92-7675b17a25d2`.
- Preview terrain checks are limited to 20 Hz while the pier is selected. There
  are no always-on diagnostic captures or whole-world scans each frame.
- No frame-rate or multiplayer throughput improvement is claimed. Native render
  submission, rooms, production and connected entities continue running.
- Manual UI input, wide gameplay soak tests, terrain-deformation integrations,
  arbitrary third-party docking/physics mods and WAN co-op performance remain
  unqualified. Native headless-rendering and shutdown messages were distinguished
  from Foundations exceptions in the private logs.

The original world has not been converted. Test worlds and their exported copies
remain separate from the real save. Installation and original-file restoration
receipts are retained with the private runtime evidence.
