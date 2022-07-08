using System;
using System.Collections.Generic;
using System.Linq;

namespace Signal.Beacon.Core.Entity;

public class EntityDetails : IEntityDetails
{
    public EntityType Type { get; }

    public string Id { get; }

    public string Alias { get; }

    public IEnumerable<IContact> Contacts { get; }
    
    public EntityDetails(string id, string alias, IEnumerable<IContact>? contacts = null)
    {
        this.Id = id ?? throw new ArgumentNullException(nameof(id));
        this.Alias = alias ?? throw new ArgumentNullException(nameof(alias));
        this.Contacts = contacts ?? Enumerable.Empty<IContact>();
    }

    public IContact? Contact(ContactPointer pointer) =>
        pointer.EntityId != this.Id 
            ? null 
            : this.Contacts.FirstOrDefault(c => c.ChannelName == pointer.ChannelName && c.Name == pointer.Name);

    public IContact? Contact(string channelName, string contactName) =>
        this.Contacts.FirstOrDefault(c => c.ChannelName == channelName && c.Name == contactName);

    public IContact ContactOrDefault(string channelName, string contactName) =>
        this.Contact(channelName, contactName) ?? new Contact(channelName, contactName);
}