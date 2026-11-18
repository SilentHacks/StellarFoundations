# StellarFoundations

Build planetary bases that stay put. Start a level base on timber piers, or anchor an existing build without moving its contents.

## Features

- Ground-contact anchors and adjustable piers up to six metres tall.
- Level starting grids for new bases. Existing builds keep their position and tilt.
- Native floors, rooms, inventories, cables and machinery.
- Native build costs, holograms, tool controls and recycling.
- Short confirmation holds for anchoring and final-support removal.
- A separate-save export for uninstalling.

## Install

Requires Windows, StellarDrive Demo 0.8.3, MelonLoader 0.7.3 and SDModKit 1.3.3.

Install through Stellar Mod Manager.

For manual installation, close the game and install the requirements separately. Download StellarFoundations.zip from [GitHub Releases](https://github.com/SilentHacks/StellarFoundations/releases) and extract it into the game folder. The DLL belongs at Mods/StellarFoundations.dll.

Back up the entire world directory before first use or upgrading, including sdmodkit-content.json and hidden files.

If Foundation is missing, check the StellarFoundations version in `MelonLoader/Latest.log`. Version 0.6.2 targets Demo 0.8.2. For a compatibility warning, use a mod profile that supports your game build and the mods used in your world.

StellarFoundations 0.6.4 qualifies Custom Seat 1.1.1, Arsenal 1.12.2 and Ship Schematics 1.0.3 for placement and ordinary saves. Saving with any of these mods can pause the game while the world commits. Export is unavailable with these companions because they store additional world data. Build Zoop 1.6.1, Safety Rails 1.0.0 and StellarStriderLib 1.5.1, including Fusion and Warp10 profiles that use it, are incompatible on this stack.

## Build and anchor

Select Foundation beside Floor in Hull (Wood). It unlocks with the wooden floor and costs 4 wood.

To start a base, aim at terrain. Scroll to rotate; Shift + scroll adjusts height. Place the foundation, then build an ordinary floor on its top plate. The height modifier can be rebound in settings.

To anchor an existing build, aim at an ordinary floor and hold build for 0.8 seconds. The outline shows the structure being anchored. Undock visiting ships and stop the structure first. The floor must touch terrain or have clear ground within six metres below it.

To remove a support, aim the recycler at its plate or the floor mounting spot above it. Empty foundations and redundant supports recycle normally. Removing the final support of a built structure takes a 0.8 second hold and restores normal physics. The floor stays in place.

## Base behaviour

Grounded bases remain collidable and damageable. Their movement, propulsion and warp are stopped; production machinery and rotors keep working. Losing every support releases the structure. Destroying an anchor's attached floor also removes that anchor.

Anchors support planetary terrain. Asteroids and moving rotor sections are unsupported.

## Saves and uninstalling

Keep the mod installed while a world contains foundations. To remove it, open the Foundations settings and choose Save a separate copy without Foundations. The exported copy has normal ship physics; the original keeps its anchors.

Add a floor to an otherwise empty starter foundation, or recycle it, before export.

Keep native world data and foundation metadata together, including .stellarfoundations-save and .stellarfoundations-backups when present. If loading reports a mismatch, restore a complete matching backup.

## More information

- [Release notes](RELEASE_NOTES.md)
- [Changelog](CHANGELOG.md)
- [Architecture and build instructions](docs/ARCHITECTURE.md)
- [Credits](CREDITS.md) and [MIT license](LICENSE)

Game artwork belongs to CuriousOwlGames.
