# StellarFoundations

Build planetary bases that stay put. Start a level base on timber piers, or anchor an existing build without moving its contents.

Version 0.6.1 is a preview. This update improves mod-manager dependencies and presentation; gameplay and save formats are unchanged.

## Features

- Ground-contact anchors and adjustable piers up to six metres tall.
- Level starting grids for new bases. Existing builds keep their position and tilt.
- Native floors, rooms, inventories, cables and machinery.
- Native build costs, holograms, tool controls and recycling.
- Short confirmation holds for anchoring and final-support removal.
- A separate-save export for uninstalling.

## Install

Requires StellarDrive Demo 0.8.2, MelonLoader 0.7.3, SDModKit 1.3.3 and Sandbox Menu. Other game or SDModKit builds are unsupported. StellarPerformance, StellarWorkshop and Build Plus are optional.

Install through Stellar Mod Manager. The manifest declares SDModKit and Sandbox Menu as dependencies.

For manual installation, close the game and install the requirements separately. Download StellarFoundations.zip from the [0.6.1 release](https://github.com/SilentHacks/StellarFoundations/releases/tag/v0.6.1) and extract it into the game folder. The DLL belongs at Mods/StellarFoundations.dll.

Back up the entire world directory before first use or upgrading, including sdmodkit-content.json and hidden files.

## Build and anchor

Select Foundation beside Floor in Hull (Wood). It unlocks with the wooden floor and costs 4 wood.

To start a base, aim at terrain. Scroll to rotate; Shift + scroll adjusts height. Place the foundation, then build an ordinary floor on its top plate. The height modifier can be rebound in settings.

To anchor an existing build, aim at an ordinary floor and hold build for 0.8 seconds. The outline shows the structure being anchored. Undock visiting ships and stop the structure first. The floor must touch terrain or have clear ground within six metres below it.

To remove a support, aim the recycler at its plate or the floor mounting spot above it. Empty foundations and redundant supports recycle normally. Removing the final support of a built structure takes a 0.8 second hold and restores normal physics. The floor stays in place.

## Behaviour and preview limits

Grounded bases remain collidable and damageable. Their movement, propulsion and warp are stopped; production machinery and rotors keep working. Losing every support releases the structure. Destroying an anchor's attached floor also removes that anchor.

Anchors support planetary terrain. Asteroids and moving rotor sections are unsupported. No performance improvement is promised.

Single-player interactions and saves have been tested on isolated worlds. Remote co-op and extended normal-play testing remain unqualified. Multiplayer requires the same version on the host and every client. See [validation](docs/VALIDATION.md).

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
