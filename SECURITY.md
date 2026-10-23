# Security policy

## Reporting a vulnerability

Please use GitHub's private vulnerability reporting feature for this repository.
Do not open a public issue containing credentials, private keys, world data, or
an unpatched exploit.

## Operator security checklist

- Keep world saves, password files, TLS private keys, API user data, and audit
  logs outside Git.
- Use a valid HTTPS certificate and verify its hostname from every dashboard PC.
- Restrict TCP 8443 to a trusted LAN, VPN, or authenticated reverse proxy.
- Forward only UDP 2456-2457 when public Valheim access is required.
- Treat membership in the Docker group as root-equivalent.
- Keep `CONTROL_AGENT_ENABLED=false` until transaction and rollback tests pass.
- Give secret files mode 0600 and private directories mode 0700 or 2770.
- Review every catalog change; the host agent trusts the root-owned catalog.
- Back up `/srv/valheim-world-control` independently of transaction backups.

The API container does not receive the Docker socket. Administrative activation
crosses a group-restricted Unix socket to the host agent, which validates IDs,
refuses switches with connected players, and constrains library paths beneath
configured roots.
