using System;
using System.Collections.Generic;
using System.Linq;

namespace Signal.Beacon.Core.Devices
{
    public record DeviceContact(string Name, string DataType, DeviceContactAccess Access)
    {
        public double? NoiseReductionDelta { get; init; }

        public IEnumerable<DeviceContactDataValue>? DataValues { get; init; }

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
}