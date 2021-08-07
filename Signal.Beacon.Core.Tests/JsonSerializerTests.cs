using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

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
    }
}