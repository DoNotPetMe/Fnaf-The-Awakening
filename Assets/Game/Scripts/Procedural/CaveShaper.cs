using UnityEngine;
using Grotto.Facility;

namespace Grotto.Procedural
{
    /// <summary>
    /// Turns a layout node's position and size into actual cave.
    ///
    /// Chambers are built as stacked rings with a widest-in-the-middle profile and a
    /// flat floor, then displaced by ridged noise — the ridges read as bedding planes
    /// and solution channels, which is the difference between "a cave" and "a lumpy
    /// sphere". Built rooms skip all that and get square shotcrete walls, because the
    /// contrast between the two is most of what sells the place as a cave someone
    /// bolted a building into.
    /// </summary>
    public static class CaveShaper
    {
        /// <summary>Builds the shell for one node, choosing the right treatment for its kind.</summary>
        public static void BuildNodeShell(SurfaceBatch batch, FacilityNode node, int seed)
        {
            switch (node.Kind)
            {
                case NodeKind.Room:
                case NodeKind.Station:
                    BuildBuiltRoom(batch, node, seed);
                    break;

                case NodeKind.Adit:
                    BuildAdit(batch, node, seed);
                    break;

                case NodeKind.Crawlway:
                    BuildCrawlway(batch, node, seed);
                    break;

                case NodeKind.Shaft:
                    BuildShaft(batch, node, seed);
                    break;

                case NodeKind.Watercourse:
                case NodeKind.Sump:
                    BuildChamber(batch, node, seed, SurfaceKind.CaveRockDamp, roughness: 0.24f);
                    break;

                case NodeKind.Cavern:
                default:
                    BuildChamber(batch, node, seed, SurfaceKind.CaveRock, roughness: 0.3f);
                    BuildSpeleothems(batch, node, seed);
                    break;
            }
        }

        // ---------------------------------------------------------------------
        // Natural spaces
        // ---------------------------------------------------------------------

        /// <summary>A rock chamber: flat floor, bulging walls, an irregular ceiling.</summary>
        public static void BuildChamber(SurfaceBatch batch, FacilityNode node, int seed,
            SurfaceKind surface, float roughness = 0.28f, int segments = 24, int rings = 9)
        {
            var mb = batch.For(surface);
            mb.CurrentColor = Color.white;
            mb.UvScale = 0.35f;

            float radiusX = node.Size.x * 0.5f;
            float radiusZ = node.Size.z * 0.5f;
            float height = node.Size.y;
            var floorCentre = node.Position - new Vector3(0f, height * 0.35f, 0f);

            int[] ringStarts = new int[rings];

            for (int r = 0; r < rings; r++)
            {
                float t = r / (float)(rings - 1);

                // Widest a third of the way up; pinched at the ceiling.
                float profile = Mathf.Sin(Mathf.PI * (0.12f + t * 0.84f));
                float ringY = height * t;

                float noiseSeedY = ringY * 0.35f;

                // Vertex colour darkens with height so ceilings read as unlit voids
                // even under flat ambient light.
                float shade = Mathf.Lerp(1f, 0.55f, t * t);
                mb.CurrentColor = new Color(shade, shade, shade, 1f);

                ringStarts[r] = mb.AddRing(
                    floorCentre + new Vector3(0f, ringY, 0f),
                    Quaternion.identity,
                    radiusX * profile,
                    radiusZ * profile,
                    segments,
                    ringY * 0.3f,
                    displace: (i, u) =>
                    {
                        float angle = u * Mathf.PI * 2f;
                        var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                        var samplePoint = new Vector3(
                            Mathf.Cos(angle) * 2.2f + node.Position.x * 0.1f,
                            noiseSeedY,
                            Mathf.Sin(angle) * 2.2f + node.Position.z * 0.1f);

                        float ridge = ProcNoise.Ridged(samplePoint, 4, seed: seed);
                        float lumps = ProcNoise.Fbm(samplePoint * 2.6f, 3, seed: seed + 31);

                        float offset = (ridge - 0.5f) * roughness * radiusX
                                       + (lumps - 0.5f) * roughness * 0.6f * radiusX;

                        // Keep the floor ring tight so the walls meet the floor cleanly.
                        offset *= Mathf.SmoothStep(0.25f, 1f, t);

                        return direction * offset + Vector3.up * (lumps - 0.5f) * 0.35f;
                    });
            }

            for (int r = 0; r < rings - 1; r++)
                mb.BridgeRings(ringStarts[r], ringStarts[r + 1], segments, inward: true);

            // Floor: flat, because the player and the cast have to stand on it.
            mb.CurrentColor = new Color(0.8f, 0.78f, 0.74f, 1f);
            mb.AddDisc(floorCentre, Vector3.up, Mathf.Max(radiusX, radiusZ) * 0.99f, segments,
                Quaternion.identity);

            // Ceiling cap over the pinched top ring.
            mb.CurrentColor = new Color(0.4f, 0.4f, 0.4f, 1f);
            float capRadius = Mathf.Max(radiusX, radiusZ) * Mathf.Sin(Mathf.PI * 0.96f);
            mb.AddDisc(floorCentre + new Vector3(0f, height, 0f), Vector3.down,
                Mathf.Max(0.4f, capRadius), segments, Quaternion.identity);

            mb.CurrentColor = Color.white;
        }

        /// <summary>Dripstone: stalactites above, stalagmites below, the occasional column.</summary>
        public static void BuildSpeleothems(SurfaceBatch batch, FacilityNode node, int seed)
        {
            var mb = batch.For(SurfaceKind.CaveRock);
            mb.UvScale = 0.6f;

            float radiusX = node.Size.x * 0.42f;
            float radiusZ = node.Size.z * 0.42f;
            float height = node.Size.y;
            var floor = node.Position - new Vector3(0f, height * 0.35f, 0f);

            int count = Mathf.Clamp(Mathf.RoundToInt(node.Size.x * node.Size.z * 0.05f), 4, 26);

            for (int i = 0; i < count; i++)
            {
                float a = ProcNoise.Hash(i, 1, 0, seed) * Mathf.PI * 2f;
                float radial = 0.45f + ProcNoise.Hash(i, 2, 0, seed) * 0.5f;

                var groundPoint = floor + new Vector3(
                    Mathf.Cos(a) * radiusX * radial, 0f, Mathf.Sin(a) * radiusZ * radial);

                float roll = ProcNoise.Hash(i, 3, 0, seed);

                if (roll < 0.45f)
                {
                    // Stalagmite.
                    float h = Mathf.Lerp(0.4f, height * 0.3f, ProcNoise.Hash(i, 4, 0, seed));
                    float r = Mathf.Lerp(0.12f, 0.42f, ProcNoise.Hash(i, 5, 0, seed));
                    mb.CurrentColor = new Color(0.88f, 0.86f, 0.80f, 1f);
                    mb.AddCone(groundPoint, r, h, 8);
                }
                else if (roll < 0.9f)
                {
                    // Stalactite, hanging from the ceiling.
                    float h = Mathf.Lerp(0.4f, height * 0.32f, ProcNoise.Hash(i, 6, 0, seed));
                    float r = Mathf.Lerp(0.1f, 0.34f, ProcNoise.Hash(i, 7, 0, seed));
                    var ceilingPoint = groundPoint + new Vector3(0f, height * 0.92f, 0f);
                    mb.CurrentColor = new Color(0.6f, 0.58f, 0.54f, 1f);
                    mb.AddCone(ceilingPoint, r, h, 8, Quaternion.Euler(180f, 0f, 0f));
                }
                else
                {
                    // A column, where the two met some thousands of years ago.
                    float r = Mathf.Lerp(0.2f, 0.5f, ProcNoise.Hash(i, 8, 0, seed));
                    mb.CurrentColor = new Color(0.82f, 0.80f, 0.75f, 1f);
                    mb.AddCylinder(groundPoint, r, height * 0.92f, 9, topRadiusScale: 0.72f);
                }
            }

            mb.CurrentColor = Color.white;
        }

        /// <summary>Breakdown: collapsed ceiling blocks on the floor.</summary>
        public static void BuildBreakdown(SurfaceBatch batch, Vector3 centre, float spread, int count, int seed)
        {
            var mb = batch.For(SurfaceKind.CaveRock);
            mb.UvScale = 0.5f;

            for (int i = 0; i < count; i++)
            {
                var offset = new Vector3(
                    (ProcNoise.Hash(i, 11, 0, seed) - 0.5f) * spread,
                    0f,
                    (ProcNoise.Hash(i, 12, 0, seed) - 0.5f) * spread);

                float size = Mathf.Lerp(0.35f, 1.4f, ProcNoise.Hash(i, 13, 0, seed));
                var rotation = Quaternion.Euler(
                    ProcNoise.Hash(i, 14, 0, seed) * 40f - 20f,
                    ProcNoise.Hash(i, 15, 0, seed) * 360f,
                    ProcNoise.Hash(i, 16, 0, seed) * 40f - 20f);

                float shade = Mathf.Lerp(0.7f, 1f, ProcNoise.Hash(i, 17, 0, seed));
                mb.CurrentColor = new Color(shade, shade * 0.98f, shade * 0.93f, 1f);

                mb.AddBox(centre + offset + Vector3.up * size * 0.35f,
                    new Vector3(size, size * 0.7f, size * Mathf.Lerp(0.7f, 1.3f, ProcNoise.Hash(i, 18, 0, seed))),
                    rotation);
            }

            mb.CurrentColor = Color.white;
        }

        // ---------------------------------------------------------------------
        // Built spaces
        // ---------------------------------------------------------------------

        /// <summary>A square room lined with shotcrete — the parts people built.</summary>
        public static void BuildBuiltRoom(SurfaceBatch batch, FacilityNode node, int seed)
        {
            var mb = batch.For(SurfaceKind.Shotcrete);
            mb.UvScale = 0.5f;

            var size = node.Size;
            var centre = node.Position + new Vector3(0f, size.y * 0.5f - size.y * 0.35f, 0f);

            mb.CurrentColor = new Color(0.9f, 0.9f, 0.88f, 1f);
            mb.AddBox(centre, size, inward: true);
            mb.CurrentColor = Color.white;

            // A steel skirting rail: breaks up the flat wall and catches the light.
            var steel = batch.For(SurfaceKind.SteelRusted);
            steel.UvScale = 1f;
            float floorY = node.Position.y - size.y * 0.35f;

            steel.AddBox(new Vector3(node.Position.x, floorY + 0.06f, node.Position.z + size.z * 0.5f - 0.04f),
                new Vector3(size.x, 0.12f, 0.08f));
            steel.AddBox(new Vector3(node.Position.x, floorY + 0.06f, node.Position.z - size.z * 0.5f + 0.04f),
                new Vector3(size.x, 0.12f, 0.08f));
        }

        /// <summary>A lined walking tunnel: shotcrete arch on a flat invert.</summary>
        public static void BuildAdit(SurfaceBatch batch, FacilityNode node, int seed, int segments = 14)
        {
            var mb = batch.For(SurfaceKind.Shotcrete);
            mb.UvScale = 0.5f;

            bool alongZ = node.Size.z >= node.Size.x;
            float length = alongZ ? node.Size.z : node.Size.x;
            float radius = (alongZ ? node.Size.x : node.Size.z) * 0.5f;
            float height = node.Size.y;

            var axis = alongZ ? Vector3.forward : Vector3.right;

            // AddRing lays its ring in the local XZ plane, so the rotation has to map
            // local UP onto the tube axis. FromToRotation does exactly that;
            // LookRotation preserves forward and adjusts up, which is not the same
            // thing and only happens to work when the vectors are exactly square.
            var ringRotation = Quaternion.FromToRotation(Vector3.up, axis);

            var start = node.Position - axis * (length * 0.5f) - new Vector3(0f, height * 0.35f, 0f);

            int rings = Mathf.Max(2, Mathf.RoundToInt(length / 2.5f));
            var ringStarts = new int[rings];

            for (int r = 0; r < rings; r++)
            {
                float t = r / (float)(rings - 1);
                var centre = start + axis * (length * t) + Vector3.up * (height * 0.5f);

                ringStarts[r] = mb.AddRing(centre, ringRotation, radius, height * 0.5f, segments, t * length * 0.4f,
                    displace: (i, u) =>
                    {
                        float wobble = ProcNoise.Fbm(new Vector3(u * 6f, t * 8f, seed * 0.01f), 2, seed: seed) - 0.5f;
                        return new Vector3(wobble, wobble, wobble) * 0.06f;
                    });
            }

            for (int r = 0; r < rings - 1; r++)
                mb.BridgeRings(ringStarts[r], ringStarts[r + 1], segments, inward: true);

            // Flat invert to walk on.
            mb.CurrentColor = new Color(0.78f, 0.78f, 0.76f, 1f);
            var floorCentre = node.Position - new Vector3(0f, height * 0.35f, 0f);
            var floorSize = alongZ
                ? new Vector3(radius * 1.5f, 0.1f, length)
                : new Vector3(length, 0.1f, radius * 1.5f);
            mb.AddBox(floorCentre, floorSize);
            mb.CurrentColor = Color.white;
        }

        /// <summary>A squeeze. Small, rough and unlined — nobody was meant to be in here.</summary>
        public static void BuildCrawlway(SurfaceBatch batch, FacilityNode node, int seed, int segments = 10)
        {
            var mb = batch.For(SurfaceKind.CaveRock);
            mb.UvScale = 0.8f;
            mb.CurrentColor = new Color(0.5f, 0.49f, 0.46f, 1f);

            bool alongZ = node.Size.z >= node.Size.x;
            float length = alongZ ? node.Size.z : node.Size.x;
            var axis = alongZ ? Vector3.forward : Vector3.right;
            var ringRotation = Quaternion.FromToRotation(Vector3.up, axis);

            float radiusH = (alongZ ? node.Size.x : node.Size.z) * 0.5f;
            float radiusV = node.Size.y * 0.5f;

            int rings = Mathf.Max(3, Mathf.RoundToInt(length / 1.8f));
            var ringStarts = new int[rings];
            var start = node.Position - axis * (length * 0.5f);

            for (int r = 0; r < rings; r++)
            {
                float t = r / (float)(rings - 1);

                // A crawlway that keeps the same bore is a pipe, not a cave. Pinch it.
                float pinch = 0.75f + ProcNoise.Fbm(new Vector3(t * 4f, seed * 0.01f, 0f), 2, seed: seed) * 0.5f;
                var centre = start + axis * (length * t);

                ringStarts[r] = mb.AddRing(centre, ringRotation, radiusH * pinch, radiusV * pinch, segments, t * length,
                    displace: (i, u) =>
                    {
                        float n = ProcNoise.Ridged(new Vector3(u * 5f, t * 7f, 3f), 3, seed: seed + 5) - 0.5f;
                        return new Vector3(n, n, n) * 0.16f;
                    });
            }

            for (int r = 0; r < rings - 1; r++)
                mb.BridgeRings(ringStarts[r], ringStarts[r + 1], segments, inward: true);

            mb.CurrentColor = Color.white;
        }

        /// <summary>A vertical shaft, walls only — open top and bottom so it reads as a connection.</summary>
        public static void BuildShaft(SurfaceBatch batch, FacilityNode node, int seed, int segments = 14)
        {
            var mb = batch.For(SurfaceKind.CaveRock);
            mb.UvScale = 0.5f;
            mb.CurrentColor = new Color(0.55f, 0.54f, 0.5f, 1f);

            float radius = Mathf.Max(node.Size.x, node.Size.z) * 0.5f;
            float height = node.Size.y;
            var bottom = node.Position - new Vector3(0f, height * 0.5f, 0f);

            int rings = Mathf.Max(3, Mathf.RoundToInt(height / 2.2f));
            var ringStarts = new int[rings];

            for (int r = 0; r < rings; r++)
            {
                float t = r / (float)(rings - 1);
                ringStarts[r] = mb.AddRing(bottom + Vector3.up * (height * t), Quaternion.identity,
                    radius, radius, segments, t * height * 0.4f,
                    displace: (i, u) =>
                    {
                        float angle = u * Mathf.PI * 2f;
                        float n = ProcNoise.Ridged(new Vector3(Mathf.Cos(angle) * 2f, t * 6f, Mathf.Sin(angle) * 2f),
                            3, seed: seed) - 0.5f;
                        return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * n * radius * 0.3f;
                    });
            }

            for (int r = 0; r < rings - 1; r++)
                mb.BridgeRings(ringStarts[r], ringStarts[r + 1], segments, inward: true);

            mb.CurrentColor = Color.white;
        }

        /// <summary>Bores a rough tunnel between two points. Used for the links between nodes.</summary>
        public static void BuildConnector(SurfaceBatch batch, Vector3 from, Vector3 to,
            float radius, int seed, SurfaceKind surface = SurfaceKind.CaveRock, int segments = 10)
        {
            var direction = to - from;
            float length = direction.magnitude;
            if (length < 0.5f) return;

            var mb = batch.For(surface);
            mb.UvScale = 0.6f;
            mb.CurrentColor = new Color(0.52f, 0.51f, 0.48f, 1f);

            var axis = direction / length;

            // Same reasoning as BuildAdit: the ring plane must be square to the axis,
            // and connectors run at arbitrary angles where LookRotation would skew it.
            var ringRotation = Quaternion.FromToRotation(Vector3.up, axis);

            int rings = Mathf.Max(2, Mathf.RoundToInt(length / 3f));
            var ringStarts = new int[rings];

            for (int r = 0; r < rings; r++)
            {
                float t = r / (float)(rings - 1);
                var centre = Vector3.Lerp(from, to, t);
                float wobble = 0.85f + ProcNoise.Fbm(new Vector3(t * 5f, seed * 0.01f, 1f), 2, seed: seed) * 0.4f;

                ringStarts[r] = mb.AddRing(centre, ringRotation, radius * wobble, radius * wobble,
                    segments, t * length * 0.4f);
            }

            for (int r = 0; r < rings - 1; r++)
                mb.BridgeRings(ringStarts[r], ringStarts[r + 1], segments, inward: true);

            mb.CurrentColor = Color.white;
        }
    }
}
