# Installation and configuration

This guide assumes a modern x86-64 Linux host with Docker Engine, the Docker
Compose plugin, systemd, and SteamCMD. The Windows dashboard is optional.

## 1. Prepare the host

Clone this repository at `/opt/valheim-world-control`, then create a dedicated
service identity and private data tree:

```bash
sudo groupadd --system valheim-control
sudo useradd --system --gid valheim-control --groups docker \
  --home-dir /nonexistent --shell /usr/sbin/nologin valheim-control
sudo install -d -o 1000 -g valheim-control -m 2770 \
  /srv/valheim-world-control/{server,runtime,library,backups,control,secrets}
sudo install -d -o 1000 -g valheim-control -m 2770 \
  /srv/valheim-world-control/control/{api-data,tls} \
  /srv/valheim-world-control/secrets/world-passwords
```

Docker group membership is effectively root-equivalent. Keep the agent account
non-interactive and do not reuse it for general administration.

Install the dedicated server into the private server directory:

```bash
steamcmd +force_install_dir /srv/valheim-world-control/server \
  +login anonymous +app_update 896660 validate +quit
```

The Steam payload is not included in this repository.

## 2. Configure secrets and TLS

Create the default game password and the first dashboard administrator password.
Use different values and keep both files out of Git:

```bash
sudo sh -c 'umask 077; printf "%s" "replace-with-game-password" > /srv/valheim-world-control/secrets/server_password'
sudo sh -c 'umask 077; printf "%s" "replace-with-a-long-admin-password" > /srv/valheim-world-control/secrets/bootstrap_admin_password'
```

Place a certificate and unencrypted private key readable by container UID 1000 at:

```text
/srv/valheim-world-control/control/tls/server.crt
/srv/valheim-world-control/control/tls/server.key
```

The certificate must match the hostname users enter in the Windows dashboard.
Use a public CA, an internal CA already trusted by your clients, or a reverse
proxy. This project deliberately does not install a private CA on client PCs.

## 3. Configure Compose and the agent

Copy `.env.example` to `.env`. Set `VWC_AGENT_GID` to the numeric group ID:

```bash
getent group valheim-control
```

Leave `CONTROL_AGENT_ENABLED=false` until the server, dashboard login, backups,
and rollback behavior have been tested.

Copy `examples/agent.env.example` to
`/etc/valheim-world-control/agent.env` and customize the server name and public
address. The `VWC_CONTAINER_NAME` value must match `.env`.

Copy `examples/server.env.example` to
`/srv/valheim-world-control/control/server.env` and select the initial world.

## 4. Add worlds

Copy `examples/catalog.example.json` to
`/srv/valheim-world-control/control/catalog.json`, then edit the example entries.
IDs use lowercase letters, numbers, and hyphens. Runtime names use letters,
numbers, underscore, dot, and hyphen.

For a Valheim 1.0 directory save, store the immutable starting copy here:

```text
/srv/valheim-world-control/library/<world-id>/source/<RuntimeName>/
```

The active copy lives here:

```text
/srv/valheim-world-control/runtime/worlds_local/<RuntimeName>/
```

For legacy saves, put the database at
`library/<world-id>/source/<RuntimeName>.db` and one `.fwl` per profile at
`library/<world-id>/profiles/<profile-id>/<RuntimeName>.fwl`. The helper in
`tools/promote-world-library.py` imports that legacy layout.

To use a separate password for one world, create a mode-0600 file below
`secrets/world-passwords/` and set `passwordSecretId` in the catalog to its file
name. Never put the password itself in JSON.

## 5. Build and install the agent

Download the Linux archive from a GitHub Release, extract it, and install the
agent and unit:

```bash
sudo install -d -o root -g root -m 0755 /opt/valheim-world-control/bin
sudo install -o root -g root -m 0755 ValheimWorldControl.Agent \
  /opt/valheim-world-control/bin/ValheimWorldControl.Agent
sudo install -o root -g root -m 0644 valheim-world-control-agent.service \
  /etc/systemd/system/valheim-world-control-agent.service
sudo systemctl daemon-reload
sudo systemctl enable valheim-world-control-agent.service
```

Ensure `/opt/valheim-world-control` also contains `compose.yaml`, `runtime/`,
and `control/`, or set `VWC_COMPOSE_DIRECTORY` to the actual clone location.

## 6. Start and validate

Start the Valheim service first, then the agent and API:

```bash
docker compose up -d valheim
sudo systemctl start valheim-world-control-agent.service
docker compose up -d control-api
```

Check the API at `https://your-host:8443/health`, install the Windows dashboard,
and sign in as `admin` with the bootstrap password. Confirm the status page and
perform a test switch with no players connected. Review:

```bash
systemctl status valheim-world-control-agent.service
docker logs valheim-server
cat /srv/valheim-world-control/control/server-status.json
```

After backup and rollback tests pass, set `CONTROL_AGENT_ENABLED=true` in `.env`
and recreate the API container.

## Network ports

- UDP 2456-2457: Valheim gameplay and query traffic
- TCP 8443: dashboard API (prefer LAN, VPN, or a secured reverse proxy)

Only forward UDP 2456-2457 for normal external PC play. The dashboard API
should not be exposed directly to the public internet without additional access
controls.

## Windows dashboard

Download the Windows ZIP from Releases, extract it, and run `Install.cmd`. It
installs only for the current Windows user and creates a desktop shortcut. It
does not install certificates or change the Windows hosts file. Enter your own
HTTPS API address on the sign-in screen.
