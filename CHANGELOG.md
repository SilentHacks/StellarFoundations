# Changelog

## 0.6.1

- Declare SDModKit as the only required mod dependency; Sandbox Menu is not required.
- Rewrite the manager description, release notes and README for easier reading.
- Align packaging validation with the upstream manifest, which no longer includes a DLL field.
- Keep the existing gameplay, save format and preview limitations.

## 0.5.0

First public preview of StellarFoundations, following the local 0.1 to 0.4 previews.

- Add terrain foundations with level starting grids and adjustable piers up to six metres.
- Anchor existing floor structures while preserving their position, tilt and contents.
- Adapt between compact ground-contact anchors and extended piers automatically.
- Use timber surfaces, native build costs, holograms, tool hints and recycler highlights.
- Confirm conversion and final-support removal with short 0.8 second holds.
- Recycle covered anchors through their floor mounting spot without removing the floor.
- Retain collidable, damageable bases, working machinery and ordinary visitor docking.
- Inhibit grounded propulsion and warp, and release structures after their final support is lost.
- Save native world data and foundation metadata as a recoverable pair, including backups.
- Export a separate world copy without foundations for removing the mod.
- Keep stable part identities, attachment format 2 and network protocol 4.
- Remove obsolete interaction-card labels and unused metal-material allocations.
- Centralize runtime name/version metadata and guard release metadata with tests.
- Add release media, concise documentation and the mod manager's required ZIP layout.

Validated against StellarDrive Demo 0.8.2, MelonLoader 0.7.3 and SDModKit 1.3.3.
Remote co-op, extended normal-play testing and the full input/UI-scale matrix remain
unqualified. See [validation](docs/VALIDATION.md).
