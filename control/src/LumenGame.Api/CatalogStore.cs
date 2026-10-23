using System.Text.Json;
using LumenGame.Contracts;
using Microsoft.Extensions.Options;

namespace LumenGame.Api;

public sealed class CatalogStore(IOptions<ControlOptions> options)
{
    private readonly ControlOptions _options = options.Value;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task<LibraryResponse> ReadLibraryAsync(CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(_options.CatalogFile);
        return await JsonSerializer.DeserializeAsync<LibraryResponse>(stream, _json, cancellationToken)
            ?? throw new InvalidDataException("The world catalog is empty or invalid.");
    }

    public async Task<ServerStatusResponse> ReadStatusAsync(CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(_options.StatusFile);
        return await JsonSerializer.DeserializeAsync<ServerStatusResponse>(stream, _json, cancellationToken)
            ?? throw new InvalidDataException("The server status document is empty or invalid.");
    }
}
