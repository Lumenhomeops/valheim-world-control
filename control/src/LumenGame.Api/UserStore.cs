using System.Text.Json;
using System.Text.RegularExpressions;
using LumenGame.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace LumenGame.Api;

public sealed class UserStore
{
    private static readonly Regex SafeUserName = new("^[a-z0-9][a-z0-9._-]{2,31}$", RegexOptions.CultureInvariant);
    private readonly string _usersFile;
    private readonly PasswordHasher<StoredUser> _hasher = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public UserStore(IOptions<ControlOptions> options)
    {
        ControlOptions value = options.Value;
        Directory.CreateDirectory(value.DataDirectory);
        _usersFile = Path.Combine(value.DataDirectory, "users.json");
        Bootstrap(value.BootstrapAdminPasswordFile);
    }

    public async Task<StoredUser?> ValidateAsync(string userName, string password)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            StoredUser? user = ReadUsers().SingleOrDefault(candidate =>
                candidate.Enabled && string.Equals(candidate.UserName, userName, StringComparison.OrdinalIgnoreCase));
            if (user is null)
            {
                return null;
            }

            PasswordVerificationResult result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
            return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded ? user : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<UserSummary>> ListAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return ReadUsers().Select(user => new UserSummary(user.UserName, user.Role, user.Enabled)).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<UserSummary> CreateOrUpdateUserAsync(CreateUserRequest request)
    {
        string userName = request.UserName.Trim().ToLowerInvariant();
        if (!SafeUserName.IsMatch(userName) || string.Equals(userName, "admin", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Use 3-32 lowercase letters, numbers, dots, underscores, or hyphens; admin is reserved.");
        }
        if (request.Password.Length < 12)
        {
            throw new InvalidOperationException("User passwords must contain at least 12 characters.");
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            List<StoredUser> users = [.. ReadUsers()];
            StoredUser template = new(userName, string.Empty, "User", true);
            StoredUser updated = template with { PasswordHash = _hasher.HashPassword(template, request.Password) };
            int index = users.FindIndex(user => string.Equals(user.UserName, userName, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                users[index] = updated;
            }
            else
            {
                users.Add(updated);
            }
            WriteUsers(users);
            return new UserSummary(updated.UserName, updated.Role, updated.Enabled);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Bootstrap(string passwordFile)
    {
        if (File.Exists(_usersFile))
        {
            return;
        }

        if (!File.Exists(passwordFile))
        {
            throw new InvalidOperationException("The bootstrap administrator password file is required for first start.");
        }

        string password = File.ReadAllText(passwordFile).Trim();
        if (password.Length < 14)
        {
            throw new InvalidOperationException("The bootstrap administrator password must contain at least 14 characters.");
        }

        StoredUser template = new("admin", string.Empty, "Admin", true);
        StoredUser administrator = template with { PasswordHash = _hasher.HashPassword(template, password) };
        WriteUsers([administrator]);
    }

    private IReadOnlyList<StoredUser> ReadUsers()
    {
        using FileStream stream = File.OpenRead(_usersFile);
        return JsonSerializer.Deserialize<List<StoredUser>>(stream, _json) ?? [];
    }

    private void WriteUsers(IReadOnlyList<StoredUser> users)
    {
        string temporary = _usersFile + ".new";
        using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, users, _json);
            stream.Flush(true);
        }

        File.Move(temporary, _usersFile, true);
    }
}
