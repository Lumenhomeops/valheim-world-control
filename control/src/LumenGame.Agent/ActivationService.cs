using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using LumenGame.Contracts;

namespace LumenGame.Agent;

public sealed record ActiveSelection(
    string WorldId,
    string RuntimeName,
    string ProfileId,
    string ModPackId,
    DateTimeOffset ActivatedAt,
    bool PlayerBasedRaids = false,
    bool PassiveEnemies = false);

public sealed class ActivationService(AgentOptions options, ProcessRunner processes)
{
    private static readonly Regex SafeId = new("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly Regex SafeRuntimeName = new("^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly IReadOnlyDictionary<string, string> VanillaPresets = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["normal"] = "Normal",
        ["casual"] = "Casual",
        ["easy"] = "Easy",
        ["hard"] = "Hard",
        ["hardcore"] = "Hardcore",
        ["immersive"] = "Immersive",
        ["hammer-mode"] = "Hammer"
    };
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<AgentActivationResponse> ActivateAsync(AgentActivationRequest request)
    {
        DateTimeOffset completedAt;
        ActiveSelection? prior = null;
        ActiveSelection? next = null;
        string? backupDirectory = null;
        try
        {
            ValidateRequest(request);
            Directory.CreateDirectory(Path.GetDirectoryName(options.LockFile)!);
            await using FileStream operationLock = new(options.LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            LibraryResponse catalog = await ReadJsonAsync<LibraryResponse>(options.CatalogFile);
            ServerStatusResponse status = await ReadJsonAsync<ServerStatusResponse>(options.StatusFile);
            if (status.ConnectedPlayers != 0)
            {
                throw new InvalidOperationException("World activation is blocked while players are connected.");
            }
            await EnsureNoConnectedPlayersAsync();

            WorldInfo world = catalog.Worlds.SingleOrDefault(candidate => candidate.Id == request.WorldId)
                ?? throw new InvalidOperationException("The requested world is not in the approved catalog.");
            if (!SafeRuntimeName.IsMatch(world.RuntimeName))
            {
                throw new InvalidOperationException("The selected runtime name contains unsupported characters.");
            }
            ProfileInfo profile = world.Profiles.SingleOrDefault(candidate => candidate.Id == request.ProfileId)
                ?? throw new InvalidOperationException("The requested gameplay profile is not approved for this world.");
            _ = catalog.ModPacks.SingleOrDefault(candidate => candidate.Id == request.ModPackId)
                ?? throw new InvalidOperationException("The requested mod pack is not approved.");

            string worldRoot = GuardedChild(options.LibraryRoot, world.Id);
            string stateDatabase = Path.Combine(worldRoot, "state", world.RuntimeName + ".db");
            string sourceDatabase = Path.Combine(worldRoot, "source", world.RuntimeName + ".db");
            string profileFile = Path.Combine(GuardedChild(Path.Combine(worldRoot, "profiles"), profile.Id), world.RuntimeName + ".fwl");
            string selectedDatabase = File.Exists(stateDatabase) ? stateDatabase : sourceDatabase;
            string stateDirectory = Path.Combine(worldRoot, "state", world.RuntimeName);
            string sourceDirectory = Path.Combine(worldRoot, "source", world.RuntimeName);
            string selectedDirectory = Directory.Exists(stateDirectory) ? stateDirectory : sourceDirectory;
            bool directorySave = Directory.Exists(selectedDirectory);
            if (directorySave)
            {
                RequireDirectory(selectedDirectory, "selected Valheim 1.0 world");
                _ = ResolveVanillaPreset(profile.Id);
            }
            else
            {
                RequireFile(selectedDatabase, "selected world database");
                RequireFile(profileFile, "selected world profile");
            }

            prior = File.Exists(options.ActiveSelectionFile)
                ? await ReadJsonAsync<ActiveSelection>(options.ActiveSelectionFile)
                : null;
            WorldInfo? currentWorld = catalog.Worlds.SingleOrDefault(candidate => candidate.Id == (prior?.WorldId ?? status.ActiveWorldId));
            string currentRuntimeName = prior?.RuntimeName
                ?? currentWorld?.RuntimeName
                ?? world.RuntimeName;
            if (!File.Exists(options.ServerEnvironmentFile))
            {
                await WriteServerEnvironmentAsync(
                    currentRuntimeName,
                    currentWorld?.PasswordSecretId,
                    prior?.ProfileId,
                    Directory.Exists(Path.Combine(options.RuntimeWorldsRoot, currentRuntimeName)),
                    prior?.PlayerBasedRaids ?? false,
                    prior?.PassiveEnemies ?? false);
            }
            string transaction = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + request.RequestId;
            backupDirectory = GuardedChild(options.BackupRoot, transaction);
            Directory.CreateDirectory(backupDirectory);

            ProcessResult stop = await RunComposeAsync(["stop", "-t", "120", "valheim"], TimeSpan.FromSeconds(150));
            if (stop.ExitCode != 0)
            {
                throw new InvalidOperationException("Valheim did not stop cleanly: " + LastLine(stop.Output));
            }

            try
            {
                BackupRuntime(backupDirectory, currentRuntimeName);
                if (prior is not null)
                {
                    SavePriorState(prior);
                }

                RemovePriorRuntimePair(prior);
                Directory.CreateDirectory(options.RuntimeWorldsRoot);
                if (directorySave)
                {
                    CopyDirectoryVerified(selectedDirectory, Path.Combine(options.RuntimeWorldsRoot, world.RuntimeName));
                }
                else
                {
                    CopyVerified(selectedDatabase, Path.Combine(options.RuntimeWorldsRoot, world.RuntimeName + ".db"));
                    CopyVerified(profileFile, Path.Combine(options.RuntimeWorldsRoot, world.RuntimeName + ".fwl"));
                }
                await WriteServerEnvironmentAsync(
                    world.RuntimeName,
                    world.PasswordSecretId,
                    profile.Id,
                    directorySave,
                    request.PlayerBasedRaids,
                    request.PassiveEnemies);
                next = new(
                    world.Id,
                    world.RuntimeName,
                    profile.Id,
                    request.ModPackId,
                    DateTimeOffset.UtcNow,
                    request.PlayerBasedRaids,
                    request.PassiveEnemies);
                await WriteJsonAtomicAsync(options.ActiveSelectionFile, next);

                ProcessResult start = await RunComposeAsync(["up", "-d", "valheim"], TimeSpan.FromSeconds(90));
                if (start.ExitCode != 0)
                {
                    throw new InvalidOperationException("Valheim did not start: " + LastLine(start.Output));
                }

                await ValidateStartedWorldAsync(world.RuntimeName);
                completedAt = DateTimeOffset.UtcNow;
                AgentActivationResponse success = new(request.RequestId, true, "activated", $"Activated {world.DisplayName} / {profile.DisplayName}.", completedAt);
                await AppendAuditAsync(request, success, prior, next, backupDirectory);
                return success;
            }
            catch
            {
                await RestoreBackupAsync(backupDirectory, prior);
                throw;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or TimeoutException or JsonException)
        {
            completedAt = DateTimeOffset.UtcNow;
            AgentActivationResponse failure = new(request.RequestId, false, "activation_failed", exception.Message, completedAt);
            await AppendAuditAsync(request, failure, prior, next, backupDirectory);
            return failure;
        }
    }

    private async Task EnsureNoConnectedPlayersAsync()
    {
        ProcessResult inspect = await processes.RunAsync("docker", ["inspect", options.ContainerName, "--format", "{{.State.Running}}|{{.State.StartedAt}}"], options.ComposeDirectory, TimeSpan.FromSeconds(10));
        string[] containerState = inspect.Output.Trim().Split('|');
        if (inspect.ExitCode != 0 || containerState.Length != 2 || !containerState[0].Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The live player count could not be verified.");
        }
        ProcessResult logs = await processes.RunAsync("docker", ["logs", "--since", containerState[1], options.ContainerName], options.ComposeDirectory, TimeSpan.FromSeconds(15));
        if (logs.ExitCode != 0)
        {
            throw new InvalidOperationException("The live player count is unavailable; world activation remains locked.");
        }
        int connections = PlayerConnectionTracker.CountConnected(logs.Output);
        if (connections != 0)
        {
            throw new InvalidOperationException("World activation is blocked while players are connected.");
        }
    }

    private async Task ValidateStartedWorldAsync(string runtimeName)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(options.HealthCheckSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            ProcessResult inspect = await processes.RunAsync("docker", ["inspect", options.ContainerName, "--format", "{{.State.Running}}|{{.RestartCount}}|{{.State.StartedAt}}"], options.ComposeDirectory, TimeSpan.FromSeconds(10));
            string[] state = inspect.Output.Trim().Split('|');
            string startedAt = state.Length == 3 ? state[2] : DateTimeOffset.UtcNow.ToString("O");
            ProcessResult logs = await processes.RunAsync("docker", ["logs", "--since", startedAt, options.ContainerName], options.ComposeDirectory, TimeSpan.FromSeconds(15));
            bool running = inspect.ExitCode == 0 && state.Length == 3 && state[0].Equals("true", StringComparison.OrdinalIgnoreCase) && state[1] == "0";
            bool loaded = logs.Output.Contains($"Load world: {runtimeName}", StringComparison.Ordinal) ||
                logs.Output.Contains("Game server connected", StringComparison.Ordinal);
            bool opened = logs.Output.Contains("Opened Steam server", StringComparison.Ordinal);
            bool missing = logs.Output.Split(Environment.NewLine).Any(line =>
                line.Contains("missing ", StringComparison.OrdinalIgnoreCase) &&
                line.Contains(runtimeName, StringComparison.Ordinal));
            if (running && loaded && opened && !missing)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(5));
        }

        throw new InvalidOperationException($"Valheim did not pass the world-load health check within {options.HealthCheckSeconds} seconds.");
    }

    private void BackupRuntime(string backupDirectory, string runtimeName)
    {
        string runtimeDirectory = Path.Combine(options.RuntimeWorldsRoot, runtimeName);
        if (Directory.Exists(runtimeDirectory))
        {
            CopyDirectoryVerified(runtimeDirectory, Path.Combine(backupDirectory, runtimeName));
        }

        foreach (string extension in new[] { ".db", ".fwl" })
        {
            string file = Path.Combine(options.RuntimeWorldsRoot, runtimeName + extension);
            if (File.Exists(file))
            {
                CopyVerified(file, Path.Combine(backupDirectory, Path.GetFileName(file)));
            }
        }

        if (File.Exists(options.ActiveSelectionFile))
        {
            File.Copy(options.ActiveSelectionFile, Path.Combine(backupDirectory, "active-selection.json"), true);
        }

        if (File.Exists(options.ServerEnvironmentFile))
        {
            File.Copy(options.ServerEnvironmentFile, Path.Combine(backupDirectory, "server.env"), true);
        }
    }

    private void SavePriorState(ActiveSelection prior)
    {
        string runtimeDirectory = Path.Combine(options.RuntimeWorldsRoot, prior.RuntimeName);
        if (Directory.Exists(runtimeDirectory))
        {
            string priorStateDirectory = Path.Combine(GuardedChild(GuardedChild(options.LibraryRoot, prior.WorldId), "state"), prior.RuntimeName);
            if (Directory.Exists(priorStateDirectory))
            {
                Directory.Delete(priorStateDirectory, true);
            }
            CopyDirectoryVerified(runtimeDirectory, priorStateDirectory);
            return;
        }

        string runtimeDatabase = Path.Combine(options.RuntimeWorldsRoot, prior.RuntimeName + ".db");
        if (!File.Exists(runtimeDatabase))
        {
            return;
        }

        string priorState = Path.Combine(GuardedChild(GuardedChild(options.LibraryRoot, prior.WorldId), "state"), prior.RuntimeName + ".db");
        Directory.CreateDirectory(Path.GetDirectoryName(priorState)!);
        CopyVerified(runtimeDatabase, priorState);
    }

    private void RemovePriorRuntimePair(ActiveSelection? prior)
    {
        if (prior is null)
        {
            return;
        }

        File.Delete(Path.Combine(options.RuntimeWorldsRoot, prior.RuntimeName + ".db"));
        File.Delete(Path.Combine(options.RuntimeWorldsRoot, prior.RuntimeName + ".fwl"));
        string runtimeDirectory = Path.Combine(options.RuntimeWorldsRoot, prior.RuntimeName);
        if (Directory.Exists(runtimeDirectory))
        {
            Directory.Delete(runtimeDirectory, true);
        }
    }

    private async Task RestoreBackupAsync(string backupDirectory, ActiveSelection? prior)
    {
        if (File.Exists(options.ActiveSelectionFile))
        {
            ActiveSelection current = await ReadJsonAsync<ActiveSelection>(options.ActiveSelectionFile);
            File.Delete(Path.Combine(options.RuntimeWorldsRoot, current.RuntimeName + ".db"));
            File.Delete(Path.Combine(options.RuntimeWorldsRoot, current.RuntimeName + ".fwl"));
            string currentDirectory = Path.Combine(options.RuntimeWorldsRoot, current.RuntimeName);
            if (Directory.Exists(currentDirectory))
            {
                Directory.Delete(currentDirectory, true);
            }
        }

        foreach (string directory in Directory.EnumerateDirectories(backupDirectory))
        {
            CopyDirectoryVerified(directory, Path.Combine(options.RuntimeWorldsRoot, Path.GetFileName(directory)));
        }

        foreach (string file in Directory.EnumerateFiles(backupDirectory))
        {
            string name = Path.GetFileName(file);
            if (name.EndsWith(".db", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".fwl", StringComparison.OrdinalIgnoreCase))
            {
                CopyVerified(file, Path.Combine(options.RuntimeWorldsRoot, name));
            }
        }

        string selectionBackup = Path.Combine(backupDirectory, "active-selection.json");
        if (File.Exists(selectionBackup))
        {
            File.Copy(selectionBackup, options.ActiveSelectionFile, true);
        }
        else if (prior is null && File.Exists(options.ActiveSelectionFile))
        {
            File.Delete(options.ActiveSelectionFile);
        }


        string environmentBackup = Path.Combine(backupDirectory, "server.env");
        if (File.Exists(environmentBackup))
        {
            File.Copy(environmentBackup, options.ServerEnvironmentFile, true);
        }
        else if (prior is not null)
        {
            await WriteServerEnvironmentAsync(
                prior.RuntimeName,
                null,
                prior.ProfileId,
                Directory.Exists(Path.Combine(options.RuntimeWorldsRoot, prior.RuntimeName)),
                prior.PlayerBasedRaids,
                prior.PassiveEnemies);
        }

        _ = await RunComposeAsync(["up", "-d", "valheim"], TimeSpan.FromSeconds(90));
    }

    private async Task AppendAuditAsync(AgentActivationRequest request, AgentActivationResponse response, ActiveSelection? prior, ActiveSelection? next, string? backupDirectory)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(options.AuditFile)!);
        string record = JsonSerializer.Serialize(new { request, response, prior, next, backupDirectory }, _json);
        await File.AppendAllTextAsync(options.AuditFile, record + Environment.NewLine);
    }

    private static void ValidateRequest(AgentActivationRequest request)
    {
        if (!Guid.TryParse(request.RequestId, out _) || string.IsNullOrWhiteSpace(request.Actor))
        {
            throw new InvalidOperationException("The request identity is invalid.");
        }

        if (!SafeId.IsMatch(request.WorldId) || !SafeId.IsMatch(request.ProfileId) || !SafeId.IsMatch(request.ModPackId))
        {
            throw new InvalidOperationException("Selection identifiers contain unsupported characters.");
        }
    }

    private static string GuardedChild(string root, string child)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(Path.Combine(fullRoot, child));
        if (!candidate.StartsWith(fullRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A selected path escaped its approved root.");
        }

        return candidate;
    }

    private static void RequireFile(string path, string description)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
        {
            throw new InvalidOperationException($"The {description} is missing or empty.");
        }
    }

    private static void RequireDirectory(string path, string description)
    {
        if (!Directory.Exists(path) || !Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any())
        {
            throw new InvalidOperationException($"The {description} is missing or empty.");
        }
    }

    private static void CopyVerified(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + ".new";
        File.Copy(source, temporary, true);
        byte[] sourceHash;
        byte[] destinationHash;
        using (FileStream sourceStream = File.OpenRead(source))
        {
            sourceHash = SHA256.HashData(sourceStream);
        }
        using (FileStream destinationStream = File.OpenRead(temporary))
        {
            destinationHash = SHA256.HashData(destinationStream);
        }
        if (!CryptographicOperations.FixedTimeEquals(sourceHash, destinationHash))
        {
            File.Delete(temporary);
            throw new IOException($"Checksum verification failed while copying {Path.GetFileName(source)}.");
        }

        File.Move(temporary, destination, true);
    }

    private static void CopyDirectoryVerified(string source, string destination)
    {
        RequireDirectory(source, "source world directory");
        string temporary = destination + ".new";
        if (Directory.Exists(temporary))
        {
            Directory.Delete(temporary, true);
        }
        Directory.CreateDirectory(temporary);

        foreach (string sourceFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (File.ResolveLinkTarget(sourceFile, false) is not null)
            {
                throw new IOException("Symbolic links are not permitted in a world save.");
            }
            string relative = Path.GetRelativePath(source, sourceFile);
            CopyVerified(sourceFile, Path.Combine(temporary, relative));
        }

        foreach (string sourceFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, sourceFile);
            using FileStream sourceStream = File.OpenRead(sourceFile);
            using FileStream destinationStream = File.OpenRead(Path.Combine(temporary, relative));
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(sourceStream), SHA256.HashData(destinationStream)))
            {
                Directory.Delete(temporary, true);
                throw new IOException($"Checksum verification failed while copying {relative}.");
            }
        }

        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, true);
        }
        Directory.Move(temporary, destination);
    }

    private async Task<T> ReadJsonAsync<T>(string path)
    {
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, _json) ?? throw new InvalidDataException($"{path} is empty or invalid.");
    }

    private async Task WriteJsonAtomicAsync<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".new";
        await using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, value, _json);
            await stream.FlushAsync();
            stream.Flush(true);
        }

        File.Move(temporary, path, true);
    }

    private async Task WriteServerEnvironmentAsync(
        string runtimeName,
        string? passwordSecretId,
        string? profileId,
        bool directorySave,
        bool playerBasedRaids,
        bool passiveEnemies)
    {
        if (passwordSecretId is not null && !SafeId.IsMatch(passwordSecretId))
        {
            throw new InvalidOperationException("The password secret identifier contains unsupported characters.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(options.ServerEnvironmentFile)!);
        string temporary = options.ServerEnvironmentFile + ".new";
        string passwordFile = passwordSecretId is null
            ? "/run/secrets/server_password"
            : $"{options.WorldPasswordDirectory.TrimEnd('/')}/{passwordSecretId}";
        string preset = directorySave && profileId is not null ? ResolveVanillaPreset(profileId) : string.Empty;
        string content = $"SERVER_NAME={options.ServerName}{Environment.NewLine}WORLD_NAME={runtimeName}{Environment.NewLine}SERVER_PORT={options.ServerPort}{Environment.NewLine}SERVER_QUERY_PORT={options.ServerPort + 1}{Environment.NewLine}SERVER_PUBLIC={(options.ServerPublic ? 1 : 0)}{Environment.NewLine}SERVER_PASSWORD_FILE={passwordFile}{Environment.NewLine}WORLD_PLAYER_EVENTS={playerBasedRaids.ToString().ToLowerInvariant()}{Environment.NewLine}WORLD_PASSIVE_ENEMIES={passiveEnemies.ToString().ToLowerInvariant()}{Environment.NewLine}WORLD_PRESET={preset}{Environment.NewLine}";
        await File.WriteAllTextAsync(temporary, content);
        File.Move(temporary, options.ServerEnvironmentFile, true);
    }

    private static string ResolveVanillaPreset(string profileId) => VanillaPresets.TryGetValue(profileId, out string? preset)
        ? preset
        : throw new InvalidOperationException("The selected profile is not a supported vanilla Valheim preset.");

    private Task<ProcessResult> RunComposeAsync(IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        List<string> command = ["compose"];
        command.AddRange(arguments);
        return processes.RunAsync("docker", command, options.ComposeDirectory, timeout);
    }

    private static string LastLine(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "no diagnostic output";
}
