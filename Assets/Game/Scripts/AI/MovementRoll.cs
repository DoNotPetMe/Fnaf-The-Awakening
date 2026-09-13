using UnityEngine;
using Grotto.Core;

namespace Grotto.AI
{
    /// <summary>
    /// The movement opportunity roll.
    ///
    /// Keeps the genre's twenty-sided-die feel — every few seconds a character rolls
    /// 1..20 and moves if the roll comes in at or under its AI level — but routes the
    /// situational pressure through a single multiplier instead of scattering special
    /// cases through the behaviours. That makes the whole thing a pure function, which
    /// means it is unit tested rather than hoped about.
    /// </summary>
    public static class MovementRoll
    {
        public const int MaxAiLevel = 20;

        /// <summary>
        /// Rolls for one movement opportunity.
        /// <paramref name="pressure"/> multiplies the effective AI level: 1 is neutral,
        /// above 1 makes the character bolder, below 1 holds it back.
        /// </summary>
        public static bool Attempt(RandomSource rng, int aiLevel, float pressure = 1f)
        {
            if (rng == null || aiLevel <= 0) return false;

            float effective = Mathf.Clamp(aiLevel * Mathf.Max(0f, pressure), 0f, MaxAiLevel);
            if (effective <= 0f) return false;

            // 1..20 inclusive, succeeding on <= effective. A level of 20 with any
            // positive pressure is a certainty, which is what level 20 should mean.
            int roll = rng.Range(1, MaxAiLevel + 1);
            return roll <= effective;
        }

        /// <summary>
        /// Seconds until the next opportunity. Higher AI levels get opportunities more
        /// often as well as succeeding more, so level 20 is relentless rather than
        /// merely lucky.
        /// </summary>
        public static float NextInterval(RandomSource rng, float baseInterval, int aiLevel, float jitter)
        {
            float levelScale = Mathf.Lerp(1.35f, 0.55f, Mathf.Clamp01(aiLevel / (float)MaxAiLevel));
            float interval = Mathf.Max(0.5f, baseInterval * levelScale);

            if (rng != null && jitter > 0f)
                interval *= 1f + rng.Range(-jitter, jitter);

            return Mathf.Max(0.4f, interval);
        }

        /// <summary>
        /// Chance in 0..1 that a given attempt succeeds. Used by the debug overlay to
        /// show "what are the odds right now" without consuming a roll.
        /// </summary>
        public static float SuccessChance(int aiLevel, float pressure = 1f)
        {
            float effective = Mathf.Clamp(aiLevel * Mathf.Max(0f, pressure), 0f, MaxAiLevel);
            return Mathf.Clamp01(Mathf.Floor(effective) / MaxAiLevel);
        }
    }
}
