using System.Collections.Generic;
using NUnit.Framework;
using Grotto.AI;
using Grotto.Core;

namespace Grotto.Tests
{
    /// <summary>
    /// Tests for the parts of the AI that are pure maths.
    ///
    /// The movement roll and the random source are the two places where a subtle
    /// mistake produces a game that is unfair in a way nobody can name. Determinism
    /// especially: without it, a bug report is a story rather than a reproduction.
    /// </summary>
    public class AiMathTests
    {
        [Test]
        public void Roll_LevelZeroNeverMoves()
        {
            var rng = new RandomSource(1);

            for (int i = 0; i < 2000; i++)
                Assert.IsFalse(MovementRoll.Attempt(rng, 0),
                    "An AI level of zero means the character is not in this night at all.");
        }

        [Test]
        public void Roll_LevelTwentyAlwaysMoves()
        {
            var rng = new RandomSource(2);

            for (int i = 0; i < 2000; i++)
                Assert.IsTrue(MovementRoll.Attempt(rng, 20),
                    "Level 20 should be relentless, not merely likely.");
        }

        [Test]
        public void Roll_FrequencyTracksTheLevel()
        {
            // 1d20 under the level: level 10 should land near half.
            foreach (int level in new[] { 5, 10, 15 })
            {
                var rng = new RandomSource(level * 31);
                int successes = 0;
                const int trials = 20000;

                for (int i = 0; i < trials; i++)
                    if (MovementRoll.Attempt(rng, level)) successes++;

                float observed = successes / (float)trials;
                float expected = level / 20f;

                Assert.AreEqual(expected, observed, 0.02f,
                    $"Level {level} should succeed about {expected:P0} of the time.");
            }
        }

        [Test]
        public void Roll_PressureScalesTheEffectiveLevel()
        {
            var low = new RandomSource(7);
            var high = new RandomSource(7);

            int lowCount = 0, highCount = 0;
            for (int i = 0; i < 10000; i++)
            {
                if (MovementRoll.Attempt(low, 8, pressure: 0.5f)) lowCount++;
                if (MovementRoll.Attempt(high, 8, pressure: 2f)) highCount++;
            }

            Assert.Less(lowCount, highCount, "Pressure must move the odds in the obvious direction.");
        }

        [Test]
        public void Roll_ZeroPressureStopsMovementEntirely()
        {
            var rng = new RandomSource(9);

            for (int i = 0; i < 500; i++)
                Assert.IsFalse(MovementRoll.Attempt(rng, 20, pressure: 0f));
        }

        [Test]
        public void Interval_ShortensAsTheLevelRises()
        {
            float low = MovementRoll.NextInterval(null, 8f, 1, 0f);
            float mid = MovementRoll.NextInterval(null, 8f, 10, 0f);
            float high = MovementRoll.NextInterval(null, 8f, 20, 0f);

            Assert.Greater(low, mid);
            Assert.Greater(mid, high);
            Assert.Greater(high, 0f, "An interval must never reach zero or the roll fires every frame.");
        }

        [Test]
        public void Interval_JitterStaysWithinBounds()
        {
            var rng = new RandomSource(11);

            for (int i = 0; i < 500; i++)
            {
                float interval = MovementRoll.NextInterval(rng, 10f, 10, 0.25f);
                float baseline = MovementRoll.NextInterval(null, 10f, 10, 0f);

                Assert.GreaterOrEqual(interval, baseline * 0.74f);
                Assert.LessOrEqual(interval, baseline * 1.26f);
            }
        }

        // =====================================================================
        // Determinism
        // =====================================================================

        [Test]
        public void Random_SameSeedProducesTheSameSequence()
        {
            var a = new RandomSource(12345);
            var b = new RandomSource(12345);

            for (int i = 0; i < 1000; i++)
                Assert.AreEqual(a.NextUInt(), b.NextUInt(),
                    "A seeded night has to replay exactly, or bug reports are anecdotes.");
        }

        [Test]
        public void Random_DifferentSeedsDiverge()
        {
            var a = new RandomSource(1);
            var b = new RandomSource(2);

            int differences = 0;
            for (int i = 0; i < 100; i++)
                if (a.NextUInt() != b.NextUInt()) differences++;

            Assert.Greater(differences, 90);
        }

        [Test]
        public void Random_ForkedStreamsAreIndependentButReproducible()
        {
            var parentA = new RandomSource(99);
            var parentB = new RandomSource(99);

            var childA = parentA.Fork(3);
            var childB = parentB.Fork(3);

            // Same salt, same parent seed: identical.
            for (int i = 0; i < 100; i++)
                Assert.AreEqual(childA.NextUInt(), childB.NextUInt());

            // Different salt: independent — so retuning one character does not
            // reshuffle everybody else's rolls.
            var other = new RandomSource(99).Fork(4);
            var again = new RandomSource(99).Fork(3);

            int differences = 0;
            for (int i = 0; i < 100; i++)
                if (other.NextUInt() != again.NextUInt()) differences++;

            Assert.Greater(differences, 80);
        }

        [Test]
        public void Random_ZeroSeedDoesNotLockUp()
        {
            // xorshift32 is stuck at zero forever; the constructor has to handle it.
            var rng = new RandomSource(0);

            var seen = new HashSet<uint>();
            for (int i = 0; i < 100; i++) seen.Add(rng.NextUInt());

            Assert.Greater(seen.Count, 90, "A zero seed must not degenerate.");
        }

        [Test]
        public void Random_RangeStaysInBounds()
        {
            var rng = new RandomSource(42);

            for (int i = 0; i < 10000; i++)
            {
                int value = rng.Range(3, 9);
                Assert.GreaterOrEqual(value, 3);
                Assert.Less(value, 9, "Range is exclusive at the top, like the rest of C#.");

                float f = rng.Range(-2.5f, 7.5f);
                Assert.GreaterOrEqual(f, -2.5f);
                Assert.LessOrEqual(f, 7.5f);
            }
        }

        [Test]
        public void Random_ShuffleIsAPermutation()
        {
            var rng = new RandomSource(5);
            var items = new List<int>();
            for (int i = 0; i < 50; i++) items.Add(i);

            rng.Shuffle(items);

            Assert.AreEqual(50, items.Count);
            for (int i = 0; i < 50; i++)
                Assert.IsTrue(items.Contains(i), $"Shuffle lost {i}.");
        }

        [Test]
        public void Random_DrawCountTracksUsage()
        {
            var rng = new RandomSource(17);
            Assert.AreEqual(0, rng.DrawCount);

            for (int i = 0; i < 10; i++) rng.NextFloat();
            Assert.AreEqual(10, rng.DrawCount,
                "The draw count is what lets two supposedly identical runs be diffed.");
        }

        // =====================================================================
        // Supporting maths
        // =====================================================================

        [Test]
        public void ProbabilityForStep_IsFrameRateIndependent()
        {
            // The same per-second probability must produce the same per-second outcome
            // whether it is evaluated once or sixty times.
            const float perSecond = 0.5f;

            float survivingCoarse = 1f - MathUtil.ProbabilityForStep(perSecond, 1f);

            float survivingFine = 1f;
            for (int i = 0; i < 60; i++)
                survivingFine *= 1f - MathUtil.ProbabilityForStep(perSecond, 1f / 60f);

            Assert.AreEqual(survivingCoarse, survivingFine, 0.001f);
        }

        [Test]
        public void ExpDecay_ConvergesAtTheSameRateAtAnyFrameRate()
        {
            const float target = 10f;
            const float lambda = 3f;

            float coarse = 0f;
            coarse = MathUtil.ExpDecay(coarse, target, lambda, 1f);

            float fine = 0f;
            for (int i = 0; i < 100; i++) fine = MathUtil.ExpDecay(fine, target, lambda, 0.01f);

            Assert.AreEqual(coarse, fine, 0.001f,
                "This is the whole reason ExpDecay exists instead of Lerp(a, b, k * dt).");
        }

        [Test]
        public void FormatHour_ReadsLikeAClock()
        {
            Assert.AreEqual("12 AM", MathUtil.FormatHour(0));
            Assert.AreEqual("1 AM", MathUtil.FormatHour(1));
            Assert.AreEqual("6 AM", MathUtil.FormatHour(6));
        }
    }
}
