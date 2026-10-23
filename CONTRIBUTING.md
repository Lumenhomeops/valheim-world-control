# Contributing

Issues and pull requests are welcome. Do not attach real world saves, passwords,
certificates, private hostnames, public IP addresses, or operational audit logs.

Before submitting a change:

```powershell
pwsh ./scripts/check-secrets.ps1
dotnet build ./control/src/LumenGame.Agent/LumenGame.Agent.csproj -c Release
dotnet build ./control/src/LumenGame.Api/LumenGame.Api.csproj -c Release
dotnet build ./control/src/LumenGame.Windows/LumenGame.Windows.csproj -c Release
docker compose config --quiet
```

Changes to activation, backup, restore, path validation, authentication, or TLS
handling should include a clear threat analysis and rollback test notes.
