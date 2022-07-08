using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Signal.Beacon.Core.Entity;
using Signal.Beacon.Core.Signal;

namespace Signal.Beacon.Application.Signal.Client.Entity;

internal class SignalcoEntityClient : ISignalcoEntityClient
{
    private const string ApiEntityUrl = "/entity";

    private readonly ISignalClient client;

    public SignalcoEntityClient(
        ISignalClient client)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<IEnumerable<IEntityDetails>> AllAsync(CancellationToken cancellationToken)
    {
        var response = await client.GetAsync<IEnumerable<SignalcoEntityDetailsDto>>(
            ApiEntityUrl,
            cancellationToken);
        if (response == null)
            throw new Exception("Failed to retrieve devices from API.");

        return response.Select(d => new EntityDetails(
            d.Id ?? throw new InvalidOperationException(),
            d.Alias ?? throw new InvalidOperationException(),
            d.Contacts?
                .Where(c => 
                    !string.IsNullOrWhiteSpace(c.Channel) && 
                    !string.IsNullOrWhiteSpace(c.Name))
                .Select(ds =>
                new Core.Entity.Contact(
                    ds.Channel ?? throw new InvalidOperationException(), 
                    ds.Name ?? throw new InvalidOperationException(), 
                    ds.ValueSerialized, 
                    ds.TimeStamp))
            ?? Enumerable.Empty<IContact>()));
    }

    public async Task<string> UpsertAsync(EntityUpsertCommand command, CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync<SignalcoEntityUpsertDto, SignalcoEntityUpsertResponseDto>(
            ApiEntityUrl,
            new SignalcoEntityUpsertDto(null, command.Alias),
            cancellationToken);

        if (response == null || string.IsNullOrWhiteSpace(response.EntityId))
            throw new Exception("Didn't get valid response for entity upsert.");

        return response.EntityId;
    }
}