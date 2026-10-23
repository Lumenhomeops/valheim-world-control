using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text.Json;
using LumenGame.Agent;
using LumenGame.Contracts;

[assembly: SupportedOSPlatform("linux")]

AgentOptions options = AgentOptions.FromEnvironment();
Directory.CreateDirectory(Path.GetDirectoryName(options.SocketPath)!);
if (File.Exists(options.SocketPath))
{
    File.Delete(options.SocketPath);
}

Socket listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
listener.Bind(new UnixDomainSocketEndPoint(options.SocketPath));
File.SetUnixFileMode(options.SocketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite);
listener.Listen(8);
ProcessRunner processes = new();
ActivationService activations = new(options, processes);
StatusMonitor status = new(options, processes);
_ = status.RunAsync(CancellationToken.None);
JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

while (true)
{
    using Socket client = await listener.AcceptAsync();
    await using NetworkStream stream = new(client, ownsSocket: false);
    try
    {
        AgentActivationRequest request = await JsonSerializer.DeserializeAsync<AgentActivationRequest>(stream, json)
            ?? throw new InvalidDataException("The activation request was empty.");
        AgentActivationResponse response = await activations.ActivateAsync(request);
        await TryWriteResponseAsync(stream, response, json);
    }
    catch (Exception exception) when (exception is JsonException or InvalidDataException)
    {
        AgentActivationResponse failure = new(Guid.Empty.ToString(), false, "invalid_request", exception.Message, DateTimeOffset.UtcNow);
        await TryWriteResponseAsync(stream, failure, json);
    }
}

static async Task TryWriteResponseAsync(NetworkStream stream, AgentActivationResponse response, JsonSerializerOptions json)
{
    try
    {
        await JsonSerializer.SerializeAsync(stream, response, json);
    }
    catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
    {
        // The transaction is already audited. A disconnected caller must not terminate the host agent.
    }
}
