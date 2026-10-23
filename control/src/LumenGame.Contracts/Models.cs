namespace LumenGame.Contracts;

public sealed record LoginRequest(string UserName, string Password);

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, string UserName, string Role);

public sealed record ProfileInfo(string Id, string DisplayName, bool IsOriginal);

public sealed record WorldInfo(
    string Id,
    string DisplayName,
    string RuntimeName,
    long DatabaseBytes,
    IReadOnlyList<ProfileInfo> Profiles,
    string? PasswordSecretId = null);

public sealed record ModPackInfo(
    string Id,
    string DisplayName,
    string Version,
    bool RequiresClientProfile,
    string? ClientProfileFile);

public sealed record LibraryResponse(
    IReadOnlyList<WorldInfo> Worlds,
    IReadOnlyList<ModPackInfo> ModPacks);

public sealed record ServerStatusResponse(
    string State,
    string ServerName,
    string Address,
    string? ActiveWorldId,
    string? ActiveWorldName,
    string? ActiveProfileId,
    string? ActiveModPackId,
    bool PlayerBasedRaids,
    bool PassiveEnemies,
    int ConnectedPlayers,
    DateTimeOffset ObservedAt);

public sealed record ActivationRequest(
    string WorldId,
    string ProfileId,
    string ModPackId,
    bool PlayerBasedRaids = false,
    bool PassiveEnemies = false);

public sealed record CreateUserRequest(string UserName, string Password);

public sealed record UserSummary(string UserName, string Role, bool Enabled);

public sealed record AgentActivationRequest(
    string RequestId,
    string Actor,
    string WorldId,
    string ProfileId,
    string ModPackId,
    bool PlayerBasedRaids = false,
    bool PassiveEnemies = false);

public sealed record AgentActivationResponse(string RequestId, bool Success, string Code, string Message, DateTimeOffset CompletedAt);

public sealed record ApiError(string Code, string Message);
