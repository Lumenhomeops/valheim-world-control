using System.Text.Json;
using LumenGame.Contracts;

namespace LumenGame.Agent;

public sealed class StatusMonitor(AgentOptions options, ProcessRunner processes)
{
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await RefreshAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or TimeoutException)
            {
                // The last known status remains available; the activation path independently fails closed on player checks.
            }
        }
        while (await timer.WaitForNextTickAsync(cancellationToken));
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        LibraryResponse catalog = await ReadJsonAsync<LibraryResponse>(options.CatalogFile, cancellationToken);
        ActiveSelection? active = File.Exists(options.ActiveSelectionFile)
            ? await ReadJsonAsync<ActiveSelection>(options.ActiveSelectionFile, cancellationToken)
            : null;
        ProcessResult inspect = await processes.RunAsync("docker", ["inspect", options.ContainerName, "--format", "{{.State.Running}}|{{.State.StartedAt}}"], options.ComposeDirectory, TimeSpan.FromSeconds(10));
        string[] containerState = inspect.Output.Trim().Split('|');
        if (inspect.ExitCode != 0 || containerState.Length != 2)
        {
            throw new InvalidOperationException("The Valheim container state is unavailable.");
        }
        ProcessResult logs = await processes.RunAsync("docker", ["logs", "--since", containerState[1], options.ContainerName], options.ComposeDirectory, TimeSpan.FromSeconds(15));
        if (logs.ExitCode != 0)
        {
            throw new InvalidOperationException("The live player count is unavailable.");
        }
        int players = PlayerConnectionTracker.CountConnected(logs.Output);
        WorldInfo? world = active is null ? null : catalog.Worlds.SingleOrDefault(candidate => candidate.Id == active.WorldId);
        ServerStatusResponse status = new(
            containerState[0].Equals("true", StringComparison.OrdinalIgnoreCase) ? "running" : "stopped",
            options.ServerName,
            options.AdvertisedAddress,
            active?.WorldId,
            world?.DisplayName,
            active?.ProfileId,
            active?.ModPackId,
            active?.PlayerBasedRaids ?? false,
            active?.PassiveEnemies ?? false,
            players,
            DateTimeOffset.UtcNow);
        await WriteJsonAtomicAsync(options.StatusFile, status, cancellationToken);
    }

    private async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, _json, cancellationToken)
            ?? throw new InvalidDataException($"{path} is empty or invalid.");
    }

    private async Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".status-new";
        await using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, value, _json, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(true);
        }
        File.Move(temporary, path, true);
    }
}
