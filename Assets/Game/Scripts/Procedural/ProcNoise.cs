using UnityEngine;

namespace Grotto.Procedural
{
    /// <summary>
    /// Deterministic hash-based noise.
    ///
    /// Rolled by hand rather than using <see cref="Mathf.PerlinNoise"/> for two
    /// reasons: Unity's implementation is 2D only and is not guaranteed stable across
    /// versions, and every surface in this cave needs to regenerate identically from a
    /// seed on any machine or the geometry stops matching the collision.
    /// </summary>
    public static class ProcNoise
    {
        /// <summary>Integer hash to a float in [0,1). Cheap, well-mixed enough for surfaces.</summary>
        public static float Hash(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + z * 1442695040 + seed * 2246822519);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        /// <summary>Trilinearly interpolated value noise.</summary>
        public static float Value3D(Vector3 p, int seed)
        {
            int xi = Mathf.FloorToInt(p.x);
            int yi = Mathf.FloorToInt(p.y);
            int zi = Mathf.FloorToInt(p.z);

            float xf = Fade(p.x - xi);
            float yf = Fade(p.y - yi);
            float zf = Fade(p.z - zi);

            float c000 = Hash(xi, yi, zi, seed);
            float c100 = Hash(xi + 1, yi, zi, seed);
            float c010 = Hash(xi, yi + 1, zi, seed);
            float c110 = Hash(xi + 1, yi + 1, zi, seed);
            float c001 = Hash(xi, yi, zi + 1, seed);
            float c101 = Hash(xi + 1, yi, zi + 1, seed);
            float c011 = Hash(xi, yi + 1, zi + 1, seed);
            float c111 = Hash(xi + 1, yi + 1, zi + 1, seed);

            float x00 = Mathf.Lerp(c000, c100, xf);
            float x10 = Mathf.Lerp(c010, c110, xf);
            float x01 = Mathf.Lerp(c001, c101, xf);
            float x11 = Mathf.Lerp(c011, c111, xf);

            return Mathf.Lerp(Mathf.Lerp(x00, x10, yf), Mathf.Lerp(x01, x11, yf), zf);
        }

        public static float Value2D(Vector2 p, int seed) => Value3D(new Vector3(p.x, p.y, 0.5f), seed);

        /// <summary>Summed octaves. Returns roughly 0..1.</summary>
        public static float Fbm(Vector3 p, int octaves = 4, float lacunarity = 2.03f, float gain = 0.5f, int seed = 0)
        {
            float sum = 0f;
            float amplitude = 1f;
            float total = 0f;

            for (int i = 0; i < octaves; i++)
            {
                sum += Value3D(p, seed + i * 7919) * amplitude;
                total += amplitude;
                p *= lacunarity;
                amplitude *= gain;
            }

            return total <= 0f ? 0f : sum / total;
        }

        /// <summary>
        /// Ridged noise. The sharp creases read as bedding planes and solution channels,
        /// which is what makes a generated surface look like limestone instead of
        /// like lumpy clay.
        /// </summary>
        public static float Ridged(Vector3 p, int octaves = 4, float lacunarity = 2.1f, float gain = 0.5f, int seed = 0)
        {
            float sum = 0f;
            float amplitude = 1f;
            float total = 0f;

            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - Mathf.Abs(Value3D(p, seed + i * 6151) * 2f - 1f);
                sum += n * n * amplitude;
                total += amplitude;
                p *= lacunarity;
                amplitude *= gain;
            }

            return total <= 0f ? 0f : sum / total;
        }

        /// <summary>
        /// Cellular (Worley) distance field, approximated on a 3x3x3 neighbourhood.
        /// Used for breakdown blocks and the pitting on old shotcrete.
        /// </summary>
        public static float Cellular(Vector3 p, int seed = 0)
        {
            int xi = Mathf.FloorToInt(p.x);
            int yi = Mathf.FloorToInt(p.y);
            int zi = Mathf.FloorToInt(p.z);

            float nearest = float.MaxValue;

            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                int cx = xi + dx, cy = yi + dy, cz = zi + dz;
                var feature = new Vector3(
                    cx + Hash(cx, cy, cz, seed),
                    cy + Hash(cx, cy, cz, seed + 1),
                    cz + Hash(cx, cy, cz, seed + 2));

                float distance = (feature - p).sqrMagnitude;
                if (distance < nearest) nearest = distance;
            }

            return Mathf.Clamp01(Mathf.Sqrt(nearest));
        }
    }
}
