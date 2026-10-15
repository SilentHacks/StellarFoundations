# Stellar Foundations

An independent gameplay mod by **SilentHacks** for the inspected StellarDrive Demo
0.8.2 Mono build. Version 0.1.0 is a local preview, not a published release.

Foundation piers hold a native structure in a fixed position relative to its planet.
New construction gets a level grid; converting an existing build preserves its
position and tilt. Native floors, inventories, cables, rooms and machinery stay in
their original entity.

## Playing

Requires MelonLoader 0.7.3, SDModKit 1.3.3 and matching Foundations installations on
the host and clients. StellarPerformance and StellarWorkshop are optional. Game
assembly identities are checked at startup; unsupported updates need revalidation.

1. Select **Foundation Pier**, next to Wood Floor in the build menu. It unlocks
   with the basic wooden floor and costs twice that floor's recipe (currently four
   wood). There is no power, fuel or maintenance cost for grounding.
2. Aim at planetary terrain. Scroll changes heading; **Shift + scroll** changes
   height. The modifier can be rebound in the mod settings. The foot needs a
   continuous terrain surface within six metres. Place the pier, then place an
   ordinary floor on its mounting plate and continue normal construction.
3. To convert an existing structure, aim at an ordinary floor with the pier
   selected. The outline identifies the whole entity being converted. Undock
   visiting ships, stop movement and rotation, then hold build to confirm. The
   mounting plate follows the existing floor; conversion does not level the build.
4. Additional piers provide redundancy. Use the recycler to remove a pier. The
   last pier requires a warning hold; losing the final pier through native damage
   releases the structure at the next physics boundary.

Grounded structures remain collidable and damageable. Their own thrusters,
reaction wheels, pilot-seat gyros and gyroscopes are inhibited. Their propulsion
does not consume fuel or gyro power while inhibited. Rotors and production
machinery continue operating. A docked assembly cannot warp. A visiting ship can
dock and undock normally, but cannot connect two independently grounded bases.

One surviving pier holds the whole native entity. This version does not introduce
new structural integrity, load limits or arbitrary hull-fragment splitting. It
supports planetary terrain rather than asteroids, orbiting stations or a rotor's
moving section. Piers cannot be picked up or copied into valid ground attachments;
place a new validated pier instead. Terrain streaming and LOD changes do not
release a saved base. Compatibility with terrain-deformation mods is unqualified.

## Saves and removing the mod

Back up the **whole world directory**, including `sdmodkit-content.json`. Native
world data and the foundation sidecar are saved as a recoverable pair. Keep
`.stellarfoundations-save` if recovery files are present. Paired automatic-backup
metadata lives in `.stellarfoundations-backups` beside the native backups.

Never remove the DLL from a world that still contains piers. In the Foundations
settings page, choose **Save a separate copy without Foundations**. This first
saves the current world, then creates a separate save with piers and grounding
metadata removed. Its structures use ordinary ship physics. The original retains
its foundations. Add a normal floor to an otherwise empty pier, or recycle that
pier, before exporting.

If world and metadata hashes disagree, loading stops before ship physics and
returns to the menu with an error. Restore the matching pair rather than editing
the hash. This protection requires the mod to remain installed.

Start with an isolated world copy. Automated validation covered a copied
5,556-part structure and actual local multiplayer transport, but does not replace
a manual co-op playtest. No FPS improvement is claimed: this removes base movement
and associated root-force work, while rendering, room simulation and production
work remain native.

## Building and testing

Install .NET SDK 9.0.101. Game, loader and SDModKit assemblies are resolved from the
installed game and are never distributed. Set `GameDirectory` with an MSBuild
property, `STELLARDRIVE_GAME_DIRECTORY`, or an ignored
`Directory.Build.local.props` file if the default Steam path differs.

```powershell
dotnet build StellarFoundations.sln -c Release
dotnet test tests/StellarFoundations.Tests/StellarFoundations.Tests.csproj -c Release
```

Shipping code targets net472; tests target net9.0. Tests exercise managed policy,
save recovery, export transformations and inspected native contracts without
installing runtime detours in CoreCLR. Runtime drivers, saves, logs and screenshots
remain outside this repository. See [validation](docs/VALIDATION.md) for evidence
and limitations.

Packaging is separate from building and deployment:

```powershell
./scripts/Package.ps1
```

The script requires clean committed source and matching Release build output. It
creates a local ZIP in ignored `artifacts/`; it does not install, launch, publish
or upload anything. Install only `StellarFoundations.dll` into the game's `Mods`
folder while the game is closed, after taking a complete save backup.

MIT licensed. No game, loader or SDModKit assemblies are included.
