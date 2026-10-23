namespace LumenGame.Agent;

public sealed record AgentOptions(
    string SocketPath,
    string CatalogFile,
    string StatusFile,
    string ActiveSelectionFile,
    string LibraryRoot,
    string RuntimeWorldsRoot,
    string BackupRoot,
    string ComposeDirectory,
    string ServerEnvironmentFile,
    string AuditFile,
    string LockFile,
    int HealthCheckSeconds,
    string ContainerName,
    string ServerName,
    string AdvertisedAddress,
    int ServerPort,
    bool ServerPublic,
    string WorldPasswordDirectory)
{
    public static AgentOptions FromEnvironment()
    {
        string root = Get("VWC_DATA_ROOT", "/srv/valheim-world-control");
        return new(
            Get("VWC_AGENT_SOCKET", "/run/valheim-world-control/agent.sock"),
            Get("VWC_CATALOG_FILE", Path.Combine(root, "control", "catalog.json")),
            Get("VWC_STATUS_FILE", Path.Combine(root, "control", "server-status.json")),
            Get("VWC_ACTIVE_SELECTION_FILE", Path.Combine(root, "control", "active-selection.json")),
            Get("VWC_LIBRARY_ROOT", Path.Combine(root, "library")),
            Get("VWC_RUNTIME_WORLDS_ROOT", Path.Combine(root, "runtime", "worlds_local")),
            Get("VWC_BACKUP_ROOT", Path.Combine(root, "backups", "switches")),
            Get("VWC_COMPOSE_DIRECTORY", "/opt/valheim-world-control"),
            Get("VWC_SERVER_ENV_FILE", Path.Combine(root, "control", "server.env")),
            Get("VWC_AUDIT_FILE", Path.Combine(root, "control", "audit.jsonl")),
            Get("VWC_LOCK_FILE", Path.Combine(root, "control", "operation.lock")),
            GetInt("VWC_HEALTH_CHECK_SECONDS", 180, 15, 300),
            Get("VWC_CONTAINER_NAME", "valheim-server"),
            Get("VWC_SERVER_NAME", "Valheim Server"),
            Get("VWC_ADVERTISED_ADDRESS", "valheim.example.com:2456"),
            GetInt("VWC_SERVER_PORT", 2456, 1024, 65534),
            GetBool("VWC_SERVER_PUBLIC", false),
            Get("VWC_WORLD_PASSWORD_DIRECTORY", "/run/world-passwords"));
    }

    private static string Get(string name, string fallback) =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)) ? fallback : Environment.GetEnvironmentVariable(name)!;

    private static int GetInt(string name, int fallback, int minimum, int maximum) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out int value) && value >= minimum && value <= maximum ? value : fallback;

    private static bool GetBool(string name, bool fallback) =>
        bool.TryParse(Environment.GetEnvironmentVariable(name), out bool value) ? value : fallback;
}
