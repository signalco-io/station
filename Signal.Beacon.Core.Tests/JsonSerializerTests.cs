using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Newtonsoft.Json;
using Signal.Beacon.Core.Conditions;
using Signal.Beacon.Core.Conducts;
using Signal.Beacon.Core.Devices;
using Signal.Beacon.Core.Processes;
using Xunit;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Signal.Beacon.Core.Tests
{
    public class JsonSerializerTests
    {
        [Fact]
        public void JsonSerializer_DeserializeIntoEnumerable()
        {
            var items = JsonSerializer.Deserialize<IEnumerable<string>>("[\"a\", \"b\"]");

            Assert.NotNull(items);
            Assert.Equal(2, items.Count());
        }

        [Fact]
        public void JsonSerialize_DeserializeProcess()
        {
            var deserializationSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Include,
                DefaultValueHandling = DefaultValueHandling.Populate,
                Converters =
                {
                    new BestMatchDeserializeConverter<IConditionValue>(
                        typeof(ConditionValueStatic),
                        typeof(ConditionValueDeviceState)),
                    new BestMatchDeserializeConverter<IConditionComparable>(
                        typeof(ConditionValueComparison),
                        typeof(Condition))
                }
            };
            var processConfig = JsonConvert.DeserializeObject<StateTriggerProcessConfiguration>(
                "{\"Delay\":0,\"Triggers\":[{\"Channel\":\"signal\",\"Identifier\":\"signal/presence\",\"Contact\":\"presence\"}],\"Condition\":{\"Operation\":0,\"Operations\":[{\"Operation\":1,\"Left\":{\"Target\":{\"Channel\":\"signal\",\"Identifier\":\"signal/presence\",\"Contact\":\"presence\"}},\"ValueOperation\":0,\"Right\":{\"Value\":0}}]},\"Conducts\":[{\"Target\":{\"Channel\":\"philipshue\",\"Identifier\":\"philipshue/00:17:88:01:04:90:d5:9e-0b\",\"Contact\":\"on\"},\"Delay\":0,\"Value\":\"false\"}]}", deserializationSettings);

            Assert.NotNull(processConfig);
        }

        [Fact]
        public void JsonSerializer_SerializeProcess()
        {
            var value = JsonSerializer.Serialize(new StateTriggerProcessConfiguration(
                0,
                new[]
                {
                    new DeviceTarget("signal", "signal/presence", "presence")
                },
                new Condition(ConditionOperation.Result, new[]
                {
                    new ConditionValueComparison(ConditionOperation.Result, new ConditionValueDeviceState(
                        new DeviceTarget(
                            "signal", "signal/presence", "presence")), ConditionValueOperation.Equal, new ConditionValueStatic(0))
                }),
                new[]
                {
                    new Conduct(new DeviceTarget("philipshue", "philipshue/00:17:88:01:04:90:d5:9e-0b", "on"), false, 0)
                }));
        }
    }
}