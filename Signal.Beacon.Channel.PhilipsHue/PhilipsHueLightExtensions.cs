using Q42.HueApi;
using Signal.Beacon.Core.Extensions;

namespace Signal.Beacon.Channel.PhilipsHue
{
    internal static class PhilipsHueLightExtensions
    {
        public static PhilipsHueLight AsPhilipsHueLight(this Light light, string bridgeId)
        {
            // Correct invalid color temperature (not discovered for non Philips lights)
            var min = light.Capabilities.Control.ColorTemperature.Min;
            var max = light.Capabilities.Control.ColorTemperature.Max;
            if (min == 0 && max == ushort.MaxValue)
            {
                min = 2000;
                max = 6500;
            }

            return new(
                light.UniqueId, light.Id, bridgeId,
                new PhilipsHueLight.PhilipsHueLightState(
                    light.State.On,
                    light.State.ColorTemperature?.Normalize(min, max),
                    ((int)light.State.Brightness).Normalize(0, 255)));
        }
    }
}