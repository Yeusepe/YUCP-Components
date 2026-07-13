using UnityEngine;

namespace YUCP.Components
{
    public static class VisemeTestMath
    {
        public const int VisemeCount = 15;

        public static readonly string[] VisemeNames =
        {
            "sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "I", "O", "U"
        };

        public static int DominantViseme(float[] weights)
        {
            if (weights == null || weights.Length == 0) return 0;
            var bestIndex = 0;
            var bestWeight = float.NegativeInfinity;
            var count = Mathf.Min(VisemeCount, weights.Length);
            for (var i = 0; i < count; i++)
            {
                var value = float.IsNaN(weights[i]) || float.IsInfinity(weights[i]) ? 0f : weights[i];
                if (value <= bestWeight) continue;
                bestWeight = value;
                bestIndex = i;
            }
            return bestIndex;
        }

        public static float ExpSmooth(float current, float target, float deltaTime, float responseSeconds)
        {
            if (responseSeconds <= 0f) return target;
            var alpha = 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / responseSeconds);
            return Mathf.LerpUnclamped(current, target, alpha);
        }

        public static float VoiceFromRms(float rms, float gate, float gain)
        {
            var adjusted = Mathf.Max(0f, rms * Mathf.Max(0f, gain) - Mathf.Max(0f, gate));
            var range = Mathf.Max(0.001f, 0.18f - Mathf.Max(0f, gate));
            return Mathf.Clamp01(Mathf.Sqrt(adjusted / range));
        }

        public static float RootMeanSquare(float[] samples)
        {
            if (samples == null || samples.Length == 0) return 0f;
            double sum = 0d;
            for (var i = 0; i < samples.Length; i++) sum += samples[i] * samples[i];
            return Mathf.Sqrt((float)(sum / samples.Length));
        }

        public static int ApproximateViseme(float[] samples, int sampleRate, float voice, int previousViseme)
        {
            if (samples == null || samples.Length < 16 || sampleRate <= 0 || voice < 0.025f) return 0;

            double absoluteDelta = 0d;
            var crossings = 0;
            for (var i = 1; i < samples.Length; i++)
            {
                absoluteDelta += Mathf.Abs(samples[i] - samples[i - 1]);
                if ((samples[i] >= 0f) != (samples[i - 1] >= 0f)) crossings++;
            }

            var zcr = crossings / (float)(samples.Length - 1);
            var roughness = (float)(absoluteDelta / (samples.Length - 1));
            var low = BandEnergy(samples, sampleRate, 120f, 700f);
            var mid = BandEnergy(samples, sampleRate, 700f, 2200f);
            var high = BandEnergy(samples, sampleRate, 2200f, 7000f);
            var total = Mathf.Max(1e-7f, low + mid + high);
            low /= total;
            mid /= total;
            high /= total;

            // Unvoiced consonants are separated first; this makes microphone fallback
            // responsive while the licensed Oculus engine remains the exact backend.
            if (zcr > 0.28f || high > 0.62f) return high > 0.78f ? 7 : 2; // SS / FF
            if (zcr > 0.19f || roughness > 0.055f) return high > mid ? 6 : 3; // CH / TH
            if (previousViseme == 0 && voice > 0.12f && low > 0.58f) return 1; // PP onset

            // Broad vowel regions. These deliberately avoid language-specific text
            // recognition and operate entirely on the same live PCM stream.
            if (low > 0.68f) return mid < 0.19f ? 14 : 13; // U / O
            if (mid > 0.53f) return high > 0.24f ? 12 : 11; // I / E
            if (voice > 0.68f && low > 0.42f) return 10; // aa
            if (high > 0.34f) return 4; // DD
            if (mid > 0.36f) return 8; // nn
            return low > 0.48f ? 9 : 5; // RR / kk
        }

        private static float BandEnergy(float[] samples, int sampleRate, float minimumHz, float maximumHz)
        {
            // A compact log-spaced Goertzel bank is stable in edit mode and avoids FFT allocations.
            const int points = 6;
            double sum = 0d;
            var min = Mathf.Max(20f, minimumHz);
            var max = Mathf.Min(sampleRate * 0.48f, maximumHz);
            for (var i = 0; i < points; i++)
            {
                var t = i / (float)(points - 1);
                var frequency = min * Mathf.Pow(max / min, t);
                var coefficient = 2d * System.Math.Cos(2d * System.Math.PI * frequency / sampleRate);
                double q0 = 0d, q1 = 0d, q2 = 0d;
                for (var n = 0; n < samples.Length; n++)
                {
                    q0 = coefficient * q1 - q2 + samples[n];
                    q2 = q1;
                    q1 = q0;
                }
                sum += q1 * q1 + q2 * q2 - coefficient * q1 * q2;
            }
            return (float)(sum / points);
        }
    }
}
