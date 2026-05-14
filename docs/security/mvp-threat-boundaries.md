# MVP Threat Boundaries

The MVP is a visible enterprise DRM client/server foundation. It is not stealth software and does not delete arbitrary files.

## Enforced in MVP

- Protected files use authenticated encryption.
- Server policy is checked before opening.
- Revoked files are denied future opens.
- Audit events are recorded for registration, access decisions, and revocation.
- Windows agent/service is visible and signed in production builds.

## Not Enforced in MVP

- Perfect screenshot prevention.
- Kernel-level anti-tamper.
- Office/CAD/native app editing control.
- Transparent folder encryption.
- Arbitrary file deletion.

## Remote Delete Rule

Remote delete applies only to files whose container metadata proves they were created or enrolled by this platform. The agent must never accept a server command to delete a path that is not known as a protected-file inventory item.
