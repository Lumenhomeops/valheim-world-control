namespace LumenGame.Api;

public sealed record StoredUser(string UserName, string PasswordHash, string Role, bool Enabled);
