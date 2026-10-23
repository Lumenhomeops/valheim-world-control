namespace LumenGame.Api;

public sealed class ControlOptions
{
    public const string SectionName = "ValheimWorldControl";
    public string DataDirectory { get; init; } = "/data";
    public string CatalogFile { get; init; } = "/library/catalog.json";
    public string StatusFile { get; init; } = "/status/server-status.json";
    public string BootstrapAdminPasswordFile { get; init; } = "/run/secrets/bootstrap_admin_password";
    public string AgentSocketPath { get; init; } = "/run/valheim-world-control/agent.sock";
    public int SessionHours { get; init; } = 8;
    public bool ControlAgentEnabled { get; init; }
}
