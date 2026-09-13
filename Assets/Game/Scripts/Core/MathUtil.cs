using UnityEngine;

namespace Grotto.Core
{
    public static class MathUtil
    {
        /// <summary>Maps <paramref name="value"/> from one range to another, clamped.</summary>
        public static float Remap(float value, float inMin, float inMax, float outMin, float outMax)
        {
            if (Mathf.Approximately(inMax, inMin)) return outMin;
            float t = Mathf.Clamp01((value - inMin) / (inMax - inMin));
            return Mathf.Lerp(outMin, outMax, t);
        }

        public static float Remap01(float value, float inMin, float inMax) => Remap(value, inMin, inMax, 0f, 1f);

        /// <summary>
        /// Frame-rate independent approach of <paramref name="current"/> toward
        /// <paramref name="target"/>.
        ///
        /// Plain <c>Lerp(a, b, k * dt)</c> silently changes behaviour with frame rate;
        /// this uses the exponential form so a 30 fps and a 144 fps machine converge at
        /// the same real-world rate. <paramref name="lambda"/> is the decay constant —
        /// higher is snappier.
        /// </summary>
        public static float ExpDecay(float current, float target, float lambda, float deltaTime)
        {
            return target + (current - target) * Mathf.Exp(-lambda * deltaTime);
        }

        public static Vector3 ExpDecay(Vector3 current, Vector3 target, float lambda, float deltaTime)
        {
            float k = Mathf.Exp(-lambda * deltaTime);
            return target + (current - target) * k;
        }

        /// <summary>Moves an angle toward a target the short way round, in degrees per second.</summary>
        public static float MoveTowardsAngle(float current, float target, float maxDelta)
            => Mathf.MoveTowardsAngle(current, target, maxDelta);

        /// <summary>Smooth 0..1 ramp with zero derivative at both ends.</summary>
        public static float SmoothStep01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Converts a per-second probability into a probability for a step of
        /// <paramref name="deltaTime"/>. Prevents "AI is twice as aggressive at 120 fps".
        /// </summary>
        public static float ProbabilityForStep(float probabilityPerSecond, float deltaTime)
        {
            probabilityPerSecond = Mathf.Clamp01(probabilityPerSecond);
            if (probabilityPerSecond <= 0f) return 0f;
            if (probabilityPerSecond >= 1f) return 1f;
            return 1f - Mathf.Pow(1f - probabilityPerSecond, Mathf.Max(0f, deltaTime));
        }

        /// <summary>Formats a night hour (0..6) the way the station clock shows it.</summary>
        public static string FormatHour(int hour)
        {
            int display = hour <= 0 ? 12 : hour;
            return display + " AM";
        }
    }
}
