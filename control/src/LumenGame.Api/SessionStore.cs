using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace LumenGame.Api;

public sealed record SessionRecord(string UserName, string Role, DateTimeOffset ExpiresAt);

public sealed class SessionStore(IOptions<ControlOptions> options)
{
    private readonly ConcurrentDictionary<string, SessionRecord> _sessions = new(StringComparer.Ordinal);
    private readonly TimeSpan _lifetime = TimeSpan.FromHours(Math.Clamp(options.Value.SessionHours, 1, 24));

    public (string Token, SessionRecord Session) Create(StoredUser user)
    {
        string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        SessionRecord session = new(user.UserName, user.Role, DateTimeOffset.UtcNow.Add(_lifetime));
        _sessions[token] = session;
        return (token, session);
    }

    public SessionRecord? Validate(string token)
    {
        if (!_sessions.TryGetValue(token, out SessionRecord? session))
        {
            return null;
        }

        if (session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(token, out _);
            return null;
        }

        return session;
    }
}
