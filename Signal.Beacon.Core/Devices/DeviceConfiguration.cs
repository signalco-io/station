using System;
using System.Collections.Generic;
using System.Linq;

namespace Signal.Beacon.Core.Devices;

public class DeviceConfiguration
{
    public string Id { get; }

    public string Alias { get; }

    public bool IsConfigured { get; }

    public string Identifier { get; }

    public IEnumerable<DeviceEndpoint> Endpoints { get; }
    

    public DeviceConfiguration(string id, string alias, string identifier, IEnumerable<DeviceEndpoint>? endpoints = null)
    {
        this.Id = id ?? throw new ArgumentNullException(nameof(id));
        this.Alias = alias ?? throw new ArgumentNullException(nameof(alias));
        this.Identifier = identifier ?? throw new ArgumentNullException(nameof(identifier));
        this.Endpoints = endpoints ?? Enumerable.Empty<DeviceEndpoint>();
        this.IsConfigured = true;
    }

    public DeviceContact? Contact(string channelName, string contactName) =>
        this.Endpoints.FirstOrDefault(e => e.Channel == channelName)?
            .Contacts.FirstOrDefault(c => c.Name == contactName);

    public DeviceContact ContactOrDefault(string channelName, string contactName) =>
        this.Contact(channelName, contactName) ?? new DeviceContact(contactName, "", DeviceContactAccess.None);
}