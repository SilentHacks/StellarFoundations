# Stellar Foundations

- This repository contains the independent Stellar Foundations gameplay mod. Author: SilentHacks.
- Shipping code targets net472. Standalone tests target net9.0; never install Harmony/MonoMod detours in CoreCLR.
- Game, loader and SDModKit assemblies resolve externally through GameDirectory. Never commit or package them.
- Keep all private inspection, saves, runtime drivers, screenshots, logs and backups in ../private/foundations-0.1.0.
- Building, testing and packaging must not deploy or launch the game. Deployment is a separate closed-game action with verified backups.
- Test only isolated world copies. Do not load, save, replace or modify the real world during validation.
- Preserve native inventories, floor/room topology, cables, machinery and simulation rates. Grounding changes movement and propulsion intentionally.
- Fail closed on unsupported binaries, malformed grounding metadata or conflicting movement/save patches. Do not release a saved base as a fallback.
- Keep authority on the host. Revalidate transactions, reject replay and invalidate confirmations after mutations.
- No publishing, pushing or releases without a separate user instruction. No AI co-author.
