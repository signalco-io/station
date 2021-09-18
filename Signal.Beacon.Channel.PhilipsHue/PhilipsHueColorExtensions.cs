using Signal.Beacon.Core.Extensions;

namespace Signal.Beacon.Channel.PhilipsHue
{
    internal static class PhilipsHueColorExtensions
    {
        public static int NormalizedToMirek(this double mirek, int kelvinMin, int kelvinMax)
        {
            var nMin = kelvinMin;
            var nMax = kelvinMax;
            if (nMin == 0 && kelvinMax == ushort.MaxValue)
            {
                nMin = 2000;
                nMax = 6500;
            }

            return mirek.Denormalize(nMin, nMax).KelvinToMirek();
        }

        private static int KelvinToMirek(this double kelvin) => (int)(1000000 / kelvin);

        public static double? MirekToNormalized(this int? mirek, int kelvinMin, int kelvinMax)
        {
            var nMin = kelvinMin;
            var nMax = kelvinMax;
            if (kelvinMin == 0 && kelvinMax == ushort.MaxValue)
            {
                nMin = 2000;
                nMax = 6500;
            }

            return (1000000d / mirek)?.Normalize(nMin, nMax);
        }
    }
}