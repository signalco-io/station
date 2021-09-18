using Q42.HueApi;
using Signal.Beacon.Core.Extensions;

namespace Signal.Beacon.Channel.PhilipsHue
{
    internal static class PhilipsHueLightExtensions
    {
        public static PhilipsHueLight AsPhilipsHueLight(this Light light, string bridgeId) =>
            new(
                light.UniqueId, light.Id, bridgeId,
                new PhilipsHueLight.PhilipsHueLightState(
                    light.State.On,
                    light.State.ColorTemperature.MirekToNormalized(
                        light.Capabilities.Control.ColorTemperature.Min,
                        light.Capabilities.Control.ColorTemperature.Max),
                    ((int)light.State.Brightness).Normalize(0, 255)));
    }
}