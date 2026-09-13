using System.Collections.Generic;

namespace Grotto.Core
{
    /// <summary>
    /// Deterministic xorshift32 generator.
    ///
    /// The AI never touches <see cref="UnityEngine.Random"/>. Every roll comes from a
    /// seeded <see cref="RandomSource"/> so that a night can be replayed exactly —
    /// <c>night.seed 12345</c> in the dev console reproduces a bug report frame for
    /// frame instead of "it happened once, on night 4, maybe".
    /// </summary>
    public sealed class RandomSource
    {
        private uint _state;

        public int Seed { get; }

        /// <summary>Number of values drawn. Useful when diffing two supposedly identical runs.</summary>
        public long DrawCount { get; private set; }

        public RandomSource(int seed)
        {
            Seed = seed;
            // xorshift32 locks up on a zero state.
            _state = seed == 0 ? 2463534242u : unchecked((uint)seed);
        }

        /// <summary>Derives an independent stream from this one (per-animatronic sub-streams).</summary>
        public RandomSource Fork(int salt) => new RandomSource(unchecked(Seed * 486187739 + salt * 31 + 17));

        public uint NextUInt()
        {
            DrawCount++;
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>Uniform float in [0, 1).</summary>
        public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

        /// <summary>Uniform float in [min, max).</summary>
        public float Range(float min, float max) => min + (max - min) * NextFloat();

        /// <summary>Uniform int in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            uint span = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt() % span);
        }

        /// <summary>True with probability <paramref name="probability"/> (clamped to [0,1]).</summary>
        public bool Chance(float probability)
        {
            if (probability <= 0f) return false;
            if (probability >= 1f) return true;
            return NextFloat() < probability;
        }

        public T Pick<T>(IReadOnlyList<T> items)
        {
            if (items == null || items.Count == 0) return default;
            return items[Range(0, items.Count)];
        }

        /// <summary>In-place Fisher-Yates using this stream, so shuffles stay reproducible.</summary>
        public void Shuffle<T>(IList<T> items)
        {
            if (items == null) return;
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = Range(0, i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }
    }
}
