# Changelog

## 0.1.0 — local preview

- Add adjustable terrain piers, a level first-floor grid, and whole-entity
  conversion that preserves existing pose and native contents.
- Anchor bases with native collidable kinematic physics; preserve impact damage,
  redundant supports, safe-boundary release, docking and native rotor machinery.
- Inhibit grounded propulsion and refuse warp/repositioning while attached.
- Validate actions on the host and synchronize grounding before client spawning.
- Save world and metadata as a recoverable pair, pair native backups, and export a
  separate save that can load without Foundations initialization.
- Correct native Rigidbody initialization, graph out-parameter binding,
  nearest-scene selection, first-floor orientation, native materials and small
  Steam handshake packet sizing found during isolated runtime validation.
- Keep all automated test launch/exit behavior in private, opt-in validation tools.
