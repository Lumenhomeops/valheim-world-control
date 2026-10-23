# Valheim World Control

Valheim World Control is a self-hosted multi-world control plane for a dedicated
Valheim server. It provides a Windows dashboard, an authenticated HTTPS API, and
a privileged Linux host agent that switches worlds transactionally.

The switch operation refuses to run while players are connected, takes a
checksum-verified backup, saves the outgoing world, applies a vanilla gameplay
preset and optional world modifiers, then validates the restarted server. A
failed health check automatically restores the prior world.

Originally developed for Lumen Home and released under the MIT license.

## Features

- Multiple independent worlds in an approved catalog
- Vanilla presets: Normal, Casual, Easy, Hard, Hardcore, Immersive, and Hammer Mode
- Independent Player-Based Raids and Passive Enemies switches
- Per-world password files without putting passwords in the catalog
- Admin and read-only dashboard accounts
- Audit log, pre-switch backups, health checks, and rollback
- Docker-isolated Valheim runtime and API
- Linux systemd host agent with a narrow Unix socket boundary
- Self-contained Windows dashboard and Linux agent release artifacts

## Architecture

```text
Windows dashboard --HTTPS--> control API --Unix socket--> host agent
                                                        |-- world library
                                                        |-- backups/audit
                                                        `-- Docker Compose
```

The API never receives the Docker socket. The host agent is the only component
allowed to manipulate world files or restart the Valheim container.

## Start here

1. Read [docs/SETUP.md](docs/SETUP.md).
2. Copy `.env.example` to `.env` and customize it.
3. Copy `examples/catalog.example.json` to your private control data directory.
4. Create private password files and TLS files as described in the setup guide.
5. Install the Linux agent, validate a switch, and only then set
   `CONTROL_AGENT_ENABLED=true`.
6. Download the Windows dashboard from the latest GitHub Release.

Do not commit worlds, passwords, TLS private keys, user databases, status files,
or audit records. The repository ignore rules cover the standard locations and
file types, but operators remain responsible for reviewing commits.

## Documentation

- [Installation and configuration](docs/SETUP.md)
- [Security model](SECURITY.md)
- [Contributing](CONTRIBUTING.md)

## Compatibility

The runtime and health checks support the current dedicated-server log format
used by Valheim 1.0 and both directory-based saves and legacy `.db`/`.fwl`
libraries. This project does not redistribute Valheim server binaries, game
worlds, or third-party mods.

Valheim is a trademark of Iron Gate AB. This project is independent and is not
affiliated with or endorsed by Iron Gate AB, Coffee Stain Publishing, or Valve.
