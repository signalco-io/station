using System;
using System.Collections.Generic;
using System.Linq;

namespace Signal.Beacon.Core.Devices;

public record DeviceContact(string Name, string DataType, DeviceContactAccess Access)
{
    public double? NoiseReductionDelta { get; init; }

    public IEnumerable<DeviceContactDataValue>? DataValues { get; init; }

    public IEnumerable<DeviceContactDataValue>? MergeDataValues<T>(
        IEnumerable<T>? newItems,
        Func<T, string> valueSelect,
        Func<T, string?>? labelSelect = null)
    {
        var existingItems = new List<DeviceContactDataValue>(this.DataValues ?? Enumerable.Empty<DeviceContactDataValue>());
        var setItems = this.SetDataValues(newItems, valueSelect, labelSelect)?.ToList() ?? new List<DeviceContactDataValue>();

        // Merge: union of setItems and existingItems except ones in setItems
        var merged = setItems
            .Union(existingItems.Where(ei => setItems.All(si => si.Value != ei.Value)))
            .ToList();

        // Return null if nothing is in collection
        return !merged.Any() ? null : merged;
    }

    public IEnumerable<DeviceContactDataValue>? SetDataValues<T>(
        IEnumerable<T>? newItems,
        Func<T, string> valueSelect,
        Func<T, string?>? labelSelect = null)
    {
        var existingDataValues = new List<DeviceContactDataValue>(
            this.DataValues ?? Enumerable.Empty<DeviceContactDataValue>());

        return newItems?.Select(dv =>
        {
            var value = valueSelect(dv);
            return new DeviceContactDataValue(
                value,
                existingDataValues.FirstOrDefault(edv => edv.Value == value)?.Label ?? labelSelect?.Invoke(dv));
        }).ToList();
    }

    public virtual bool Equals(DeviceContact? other)
    {
        if (other == null)
            return false;

        return this.Name == other.Name &&
               this.DataType == other.DataType &&
               this.Access == other.Access &&
               Math.Abs((this.NoiseReductionDelta ?? 0) -
                        (other.NoiseReductionDelta ?? 0)) <= double.Epsilon &&
               (this.DataValues ?? new List<DeviceContactDataValue>())
               .SequenceEqual(other.DataValues ?? new List<DeviceContactDataValue>());
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(this.NoiseReductionDelta, this.DataValues, this.Name, this.DataType, (int)this.Access);
    }
}