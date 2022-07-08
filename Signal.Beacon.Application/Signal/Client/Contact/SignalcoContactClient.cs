using System;
using System.Threading;
using System.Threading.Tasks;
using Signal.Beacon.Core.Entity;
using Signal.Beacon.Core.Signal;

namespace Signal.Beacon.Application.Signal.Client.Contact;

internal class SignalcoContactClient : ISignalcoContactClient
{
    private const string ApiContactUrl = "/contact";

    private readonly ISignalClient client;

    public SignalcoContactClient(
        ISignalClient client)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task UpsertAsync(ContactUpsertCommand command, CancellationToken cancellationToken)
    {
        var valueSerialized = this.SerializeValue(command.Value);
        await client.PostAsJsonAsync(
            ApiContactUrl,
            new SignalcoContactUpsertDto(command.EntityId, command.ChannelName, command.Name, valueSerialized, command.TimeStamp),
            cancellationToken);
    }
}