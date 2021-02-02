using Q42.HueApi;

namespace Signal.Beacon.Channel.PhilipsHue
{
    internal static class PhilipsHueLightExtensions
    {
        public static PhilipsHueLight AsPhilipsHueLight(this Light light, string bridgeId) =>
            new(
                light.UniqueId, light.Id, bridgeId, 
                new PhilipsHueLight.PhilipsHueLightState(light.State.On));
    }
}