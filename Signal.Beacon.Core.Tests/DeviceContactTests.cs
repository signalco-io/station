using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Signal.Beacon.Core.Tests
{
    public class DeviceContactTests
    {
        [Fact]
        public void DeviceContact_MergeDataValues_MergeIntoEmpty()
        {
            var contact = new DeviceContact("test", "test", DeviceContactAccess.None);

            var result = contact.MergeDataValues(
                new List<string> { "c1", "c2" },
                i => i);

            Assert.NotNull(result);
            Assert.Equal(2, result.Count());
        }

        [Fact]
        public void DeviceContact_MergeDataValues_MergeEmptyIntoNull()
        {
            var contact = new DeviceContact("test", "test", DeviceContactAccess.None);

            var result = contact.MergeDataValues(
                new List<string>(),
                i => i);

            Assert.Null(result);
        }

        [Fact]
        public void DeviceContact_MergeDataValues_MergeNullIntoNull()
        {
            var contact = new DeviceContact("test", "test", DeviceContactAccess.None);

            var result = contact.MergeDataValues<string>(
                null,
                i => i);

            Assert.Null(result);
        }

        [Fact]
        public void DeviceContact_MergeDataValues_MergeEmptyIntoExisting()
        {
            var contact = new DeviceContact("test", "test", DeviceContactAccess.None)
            {
                DataValues = new List<DeviceContactDataValue> { new("c1", null) }
            };

            var result = contact
                .MergeDataValues(
                    new List<string>(),
                    i => i)
                ?.ToList();

            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Single(result, i => i.Value == "c1");
        }

        [Fact]
        public void DeviceContact_MergeDataValues_MergeNullIntoExisting()
        {
            var contact = new DeviceContact("test", "test", DeviceContactAccess.None)
            {
                DataValues = new List<DeviceContactDataValue> { new("c1", null) }
            };

            var result = contact
                .MergeDataValues<string>(
                    null,
                    i => i)
                ?.ToList();

            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Single(result, i => i.Value == "c1");
        }

        [Fact]
        public void DeviceContact_MergeDataValues_MergeIntoExisting()
        {
            var contact = new DeviceContact("test", "test", DeviceContactAccess.None)
            {
                DataValues = new List<DeviceContactDataValue> { new("c1", null) }
            };

            var result = contact
                .MergeDataValues(
                    new List<string>{"c2"},
                    i => i)
                ?.ToList();

            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Contains(result, i => i.Value == "c1");
            Assert.Contains(result, i => i.Value == "c2");
        }

        [Fact]
        public void DeviceContact_MergeDataValues_MergeIntoExistingDuplicate()
        {
            var contact = new DeviceContact("test", "test", DeviceContactAccess.None)
            {
                DataValues = new List<DeviceContactDataValue> { new("c1", null) }
            };

            var result = contact
                .MergeDataValues(
                    new List<string> { "c1", "c2" },
                    i => i)
                ?.ToList();

            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Contains(result, i => i.Value == "c1");
            Assert.Contains(result, i => i.Value == "c2");
        }

        [Fact]
        public void DeviceContact_MergeDataValues_MergeIntoExistingWithLabel()
        {
            var contact = new DeviceContact("test", "test", DeviceContactAccess.None)
            {
                DataValues = new List<DeviceContactDataValue> { new("c1", null) }
            };

            var result = contact
                .MergeDataValues(
                    new List<(string value, string label)> { ("c1", "1"), ("c2", "2") },
                    i => i.value,
                    i => i.label)
                ?.ToList();

            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Contains(result, i => i.Value == "c1" && i.Label == "1");
            Assert.Contains(result, i => i.Value == "c2" && i.Label == "2");
        }

        [Fact]
        public void DeviceContact_MergeDataValues_MergeIntoExistingWithLabelUnavablable()
        {
            var contact = new DeviceContact("test", "test", DeviceContactAccess.None)
            {
                DataValues = new List<DeviceContactDataValue> { new("c1", "old") }
            };

            var result = contact
                .MergeDataValues(
                    new List<(string value, string label)> { ("c1", null), ("c2", "2") },
                    i => i.value,
                    i => i.label)
                ?.ToList();

            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Contains(result, i => i.Value == "c1" && i.Label == "old");
            Assert.Contains(result, i => i.Value == "c2" && i.Label == "2");
        }

        [Fact]
        public void DeviceContact_MergeDataValues_MergeIntoExistingWithPopulatedLabel()
        {
            var contact = new DeviceContact("test", "test", DeviceContactAccess.None)
            {
                DataValues = new List<DeviceContactDataValue> { new("c1", "old") }
            };

            var result = contact
                .MergeDataValues(
                    new List<(string value, string label)> { ("c1", "1"), ("c2", "2") },
                    i => i.value,
                    i => i.label)
                ?.ToList();

            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Contains(result, i => i.Value == "c1" && i.Label == "old");
            Assert.Contains(result, i => i.Value == "c2" && i.Label == "2");
        }
    }
}