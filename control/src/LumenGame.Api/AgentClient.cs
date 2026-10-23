using System.Net.Sockets;
using System.Text.Json;
using LumenGame.Contracts;
using Microsoft.Extensions.Options;

namespace LumenGame.Api;

public sealed class AgentClient(IOptions<ControlOptions> options)
{
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task<AgentActivationResponse> ActivateAsync(ActivationRequest selection, string actor, CancellationToken cancellationToken)
    {
        string requestId = Guid.NewGuid().ToString();
        AgentActivationRequest request = new(
            requestId,
            actor,
            selection.WorldId,
            selection.ProfileId,
            selection.ModPackId,
            selection.PlayerBasedRaids,
            selection.PassiveEnemies);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(6));
        using Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(options.Value.AgentSocketPath), timeout.Token);
        await using NetworkStream stream = new(socket, ownsSocket: false);
        await JsonSerializer.SerializeAsync(stream, request, _json, timeout.Token);
        await stream.FlushAsync(timeout.Token);
        socket.Shutdown(SocketShutdown.Send);
        AgentActivationResponse response = await JsonSerializer.DeserializeAsync<AgentActivationResponse>(stream, _json, timeout.Token)
            ?? throw new InvalidDataException("The host agent returned an empty response.");
        if (!string.Equals(response.RequestId, requestId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The host agent response did not match the request.");
        }

        return response;
    }
}
