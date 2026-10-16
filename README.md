# StellarFoundations

Build a base that stays put. Add timber foundations to a new build, or anchor an
existing structure without rebuilding it or moving its contents.

![Timber foundation placed on planetary terrain](https://raw.githubusercontent.com/SilentHacks/StellarFoundations/main/assets/thumbnail.png)

- Ground-contact anchors and adjustable piers up to six metres tall.
- Level starting grids for new bases. Existing builds keep their position and tilt.
- Native floors, rooms, inventories, cables and machinery remain in place.
- Native build costs, holograms and tool controls, with short confirmation holds
  when anchoring a structure or removing its last support.
- Multiple supports, ordinary recycling and a separate-save export for uninstalling.

## Requirements

**StellarDrive Demo 0.8.2**, **MelonLoader 0.7.3** and **SDModKit 1.3.3**.
Other game or dependency builds are not supported. StellarPerformance,
StellarWorkshop and Build Plus are optional.

**0.5.0 is a preview.** Single-player interactions and saves have been tested on
isolated worlds. Remote co-op is not yet qualified. Multiplayer requires the same
version on the host and every client. See [validation](docs/VALIDATION.md).

## Install

Use Stellar Mod Manager, or download **StellarFoundations.zip** from the release
and extract it into the game folder. The DLL belongs at
`Mods/StellarFoundations.dll`. Install the requirements separately.

Close the game before changing mod files. Back up the whole world directory
before first use or upgrading, including `sdmodkit-content.json` and hidden files.

## Build and anchor

Select **Foundation** next to **Floor** in **Hull (Wood)**. It unlocks with the
wooden floor and currently costs **4 wood**.

**Start a base:** aim at terrain, scroll to rotate and use **Shift + scroll** to
adjust height. Place the foundation, then build an ordinary floor on its top plate.
The height modifier can be rebound in the mod settings.

**Anchor an existing build:** aim at an ordinary floor and hold build for
**0.8 seconds**. The outline shows the entire structure that will be anchored.
Undock visiting ships and stop the structure first. The floor must touch terrain
or have clear ground within six metres below it. The anchor adapts to the gap.

**Remove a support:** aim the recycler at its plate or the mounting spot on the
floor above it. Empty foundations and redundant supports recycle normally.
Removing the final support of a built structure takes a **0.8 second hold** and
releases it to normal physics. The floor remains in place.

Grounded bases remain collidable and damageable. Their own movement, propulsion
and warp are stopped; production machinery and rotors keep working. Damage that
destroys an anchor's attached floor also removes that anchor. Losing every support
releases the structure. Anchors support planetary terrain, not asteroids or moving
rotor sections. No performance improvement is promised.

## Saves and uninstalling

Keep the mod installed while a world contains foundations. To remove it, use
**Foundations > Save a separate copy without Foundations** in the mod settings.
The exported copy has ordinary ship physics; the original world keeps its anchors.
Add a floor to any otherwise empty starter foundation, or recycle it, before export.

Native world data and foundation metadata are saved as a recoverable pair. Keep
them together, including `.stellarfoundations-save` and
`.stellarfoundations-backups` when present. If loading reports a mismatch, restore
a complete matching backup instead of editing the files.

## Development

Source code is MIT licensed. Game artwork belongs to CuriousOwlGames.
See [credits](CREDITS.md), [architecture and build instructions](docs/ARCHITECTURE.md),
[validation](docs/VALIDATION.md) and [changes](CHANGELOG.md).
