using UnityEngine;

namespace Grotto.Procedural
{
    /// <summary>
    /// The furniture of Grotto Springs.
    ///
    /// Composed from boxes and cylinders rather than sculpted, which is the honest
    /// choice for the period — an arcade cabinet, a theatre seat and a diesel genset
    /// really are boxes with details bolted on. What sells them is proportion, wear
    /// in the vertex colours, and the fact that every one of them is where the
    /// layout says the room is.
    /// </summary>
    public static class PropFactory
    {
        // =====================================================================
        // The Midway
        // =====================================================================

        /// <summary>
        /// An upright arcade cabinet. The marquee and screen go to the emissive
        /// surface, so a dead Midway still has two cabinets glowing in the dark.
        /// </summary>
        public static void ArcadeCabinet(SurfaceBatch batch, Vector3 floorPosition, Quaternion rotation,
            int seed, bool poweredOn = false)
        {
            var body = batch.For(SurfaceKind.SteelPainted);
            body.UvScale = 1.2f;

            float hue = ProcNoise.Hash(seed, 1, 0, 0);
            body.CurrentColor = Color.HSVToRGB(hue, 0.45f, Mathf.Lerp(0.25f, 0.5f, ProcNoise.Hash(seed, 2, 0, 0)));

            Vector3 At(Vector3 local) => floorPosition + rotation * local;

            // Main cabinet body.
            body.AddBox(At(new Vector3(0f, 0.85f, 0f)), new Vector3(0.68f, 1.7f, 0.78f), rotation);

            // Control panel, angled toward the player.
            body.CurrentColor = new Color(0.14f, 0.14f, 0.16f);
            body.AddBox(At(new Vector3(0f, 1.02f, -0.44f)), new Vector3(0.66f, 0.09f, 0.34f),
                rotation * Quaternion.Euler(-18f, 0f, 0f));

            // Coin door.
            body.CurrentColor = new Color(0.3f, 0.26f, 0.14f);
            body.AddBox(At(new Vector3(0f, 0.45f, -0.4f)), new Vector3(0.3f, 0.24f, 0.04f), rotation);

            // Screen bezel.
            body.CurrentColor = new Color(0.08f, 0.08f, 0.09f);
            body.AddBox(At(new Vector3(0f, 1.42f, -0.34f)), new Vector3(0.6f, 0.52f, 0.06f),
                rotation * Quaternion.Euler(12f, 0f, 0f));

            var screenSurface = poweredOn ? SurfaceKind.EmissiveCold : SurfaceKind.Glass;
            var screen = batch.For(screenSurface);
            screen.CurrentColor = poweredOn ? new Color(0.35f, 0.8f, 0.65f) : new Color(0.05f, 0.06f, 0.07f);
            screen.AddBox(At(new Vector3(0f, 1.42f, -0.38f)), new Vector3(0.5f, 0.42f, 0.02f),
                rotation * Quaternion.Euler(12f, 0f, 0f));

            // Marquee.
            var marquee = batch.For(poweredOn ? SurfaceKind.EmissiveWarm : SurfaceKind.Glass);
            marquee.CurrentColor = poweredOn ? new Color(1f, 0.7f, 0.35f) : new Color(0.25f, 0.22f, 0.2f);
            marquee.AddBox(At(new Vector3(0f, 1.82f, -0.3f)), new Vector3(0.62f, 0.2f, 0.1f), rotation);

            body.CurrentColor = Color.white;
        }

        /// <summary>A bank of cabinets along a wall.</summary>
        public static void ArcadeRow(SurfaceBatch batch, Vector3 start, Vector3 direction, int count,
            Quaternion facing, int seed)
        {
            direction = direction.normalized;
            for (int i = 0; i < count; i++)
            {
                // Two cabinets in the row still have power. Nobody knows why.
                bool lit = ProcNoise.Hash(seed, i, 7, 0) < 0.18f;
                ArcadeCabinet(batch, start + direction * (i * 0.85f), facing, seed + i * 17, lit);
            }
        }

        // =====================================================================
        // The Grand Gallery
        // =====================================================================

        /// <summary>Rows of tip-up theatre seating, raked toward the stage.</summary>
        public static void TheatreSeating(SurfaceBatch batch, Vector3 frontCentre, Quaternion rotation,
            int rows, int seatsPerRow, int seed)
        {
            var frame = batch.For(SurfaceKind.SteelRusted);
            var fabric = batch.For(SurfaceKind.AnimatronicFabric);

            const float seatWidth = 0.56f;
            const float rowDepth = 0.85f;
            const float rake = 0.16f;

            for (int r = 0; r < rows; r++)
            {
                float z = r * rowDepth;
                float y = r * rake;

                for (int s = 0; s < seatsPerRow; s++)
                {
                    float x = (s - (seatsPerRow - 1) * 0.5f) * seatWidth;

                    // A few seats are missing. It has been thirty years.
                    if (ProcNoise.Hash(r, s, 3, seed) < 0.08f) continue;

                    Vector3 At(Vector3 local) => frontCentre + rotation * (new Vector3(x, y, z) + local);

                    frame.CurrentColor = new Color(0.3f, 0.3f, 0.32f);
                    frame.AddBox(At(new Vector3(0f, 0.2f, 0f)), new Vector3(0.06f, 0.4f, 0.06f), rotation);
                    frame.AddBox(At(new Vector3(0f, 0.2f, 0.4f)), new Vector3(0.06f, 0.4f, 0.06f), rotation);

                    float wear = Mathf.Lerp(0.6f, 1f, ProcNoise.Hash(r, s, 5, seed));
                    fabric.CurrentColor = new Color(0.36f * wear, 0.13f * wear, 0.16f * wear);

                    // Seat pan, tipped up the way empty theatre seats sit.
                    fabric.AddBox(At(new Vector3(0f, 0.46f, 0.16f)), new Vector3(seatWidth - 0.08f, 0.36f, 0.08f),
                        rotation * Quaternion.Euler(12f, 0f, 0f));
                    // Back.
                    fabric.AddBox(At(new Vector3(0f, 0.62f, 0.36f)), new Vector3(seatWidth - 0.08f, 0.5f, 0.09f),
                        rotation * Quaternion.Euler(-8f, 0f, 0f));
                }
            }

            frame.CurrentColor = Color.white;
            fabric.CurrentColor = Color.white;
        }

        /// <summary>The show stage: a boarded platform with a steel apron and footlight trough.</summary>
        public static void StagePlatform(SurfaceBatch batch, Vector3 centre, Vector3 size, Quaternion rotation)
        {
            var deck = batch.For(SurfaceKind.Decking);
            deck.UvScale = 0.7f;
            deck.CurrentColor = new Color(0.85f, 0.8f, 0.72f);
            deck.AddBox(centre + Vector3.up * (size.y * 0.5f), size, rotation);

            var trim = batch.For(SurfaceKind.SteelRusted);
            trim.CurrentColor = new Color(0.45f, 0.4f, 0.34f);
            trim.AddBox(centre + rotation * new Vector3(0f, size.y, -size.z * 0.5f),
                new Vector3(size.x, 0.14f, 0.1f), rotation);

            // Footlights along the apron.
            var lights = batch.For(SurfaceKind.EmissiveWarm);
            lights.CurrentColor = new Color(1f, 0.62f, 0.28f);
            int count = Mathf.Max(3, Mathf.RoundToInt(size.x / 1.1f));
            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0.5f : i / (float)(count - 1);
                var local = new Vector3(Mathf.Lerp(-size.x * 0.45f, size.x * 0.45f, t), size.y + 0.06f, -size.z * 0.5f + 0.14f);
                lights.AddBox(centre + rotation * local, new Vector3(0.16f, 0.1f, 0.16f), rotation);
            }

            deck.CurrentColor = Color.white;
            trim.CurrentColor = Color.white;
            lights.CurrentColor = Color.white;
        }

        // =====================================================================
        // Spring terrace
        // =====================================================================

        /// <summary>A travertine rimstone pool, still holding warm water.</summary>
        public static void SpringPool(SurfaceBatch batch, Vector3 centre, float radius, int seed)
        {
            var stone = batch.For(SurfaceKind.CaveRockDamp);
            stone.UvScale = 0.5f;

            // Concentric rimstone dams, which is how travertine terraces actually form.
            int tiers = 3;
            for (int t = 0; t < tiers; t++)
            {
                float r = radius * (1f - t * 0.22f);
                float h = 0.14f + t * 0.04f;
                float shade = Mathf.Lerp(0.95f, 0.7f, t / (float)tiers);
                stone.CurrentColor = new Color(shade, shade * 0.97f, shade * 0.9f);
                stone.AddCylinder(centre + Vector3.up * (t * 0.1f), r, h, 16, topRadiusScale: 0.94f);
            }

            var water = batch.For(SurfaceKind.Water);
            water.CurrentColor = new Color(0.12f, 0.32f, 0.34f, 0.85f);
            water.AddDisc(centre + Vector3.up * (tiers * 0.1f + 0.1f), Vector3.up,
                radius * 0.62f, 18, Quaternion.identity);

            stone.CurrentColor = Color.white;
            water.CurrentColor = Color.white;
        }

        // =====================================================================
        // Machinery
        // =====================================================================

        /// <summary>The 8kW Lister genset on its skid, with the day tank beside it.</summary>
        public static void Genset(SurfaceBatch batch, Vector3 floorPosition, Quaternion rotation)
        {
            var steel = batch.For(SurfaceKind.SteelPainted);
            var rust = batch.For(SurfaceKind.SteelRusted);

            Vector3 At(Vector3 local) => floorPosition + rotation * local;

            // Skid.
            rust.CurrentColor = new Color(0.3f, 0.28f, 0.26f);
            rust.AddBox(At(new Vector3(0f, 0.08f, 0f)), new Vector3(2.3f, 0.16f, 1.1f), rotation);

            // Engine block.
            steel.CurrentColor = new Color(0.22f, 0.34f, 0.26f);
            steel.AddBox(At(new Vector3(-0.5f, 0.55f, 0f)), new Vector3(1.1f, 0.8f, 0.85f), rotation);

            // Alternator.
            steel.CurrentColor = new Color(0.35f, 0.33f, 0.3f);
            steel.AddCylinder(At(new Vector3(0.55f, 0.5f, 0f)), 0.32f, 0.9f, 12,
                rotation * Quaternion.Euler(0f, 0f, 90f));

            // Exhaust up into the rock.
            rust.CurrentColor = new Color(0.36f, 0.2f, 0.12f);
            rust.AddCylinder(At(new Vector3(-0.5f, 0.95f, 0.3f)), 0.09f, 1.8f, 8, rotation);

            // Day tank.
            steel.CurrentColor = new Color(0.4f, 0.38f, 0.32f);
            steel.AddCylinder(At(new Vector3(0f, 0.35f, -1.1f)), 0.42f, 1.1f, 14,
                rotation * Quaternion.Euler(90f, 0f, 0f));

            // Control panel with its one green lamp.
            steel.CurrentColor = new Color(0.18f, 0.2f, 0.2f);
            steel.AddBox(At(new Vector3(0.9f, 1.1f, 0f)), new Vector3(0.1f, 0.5f, 0.44f), rotation);

            var lamp = batch.For(SurfaceKind.EmissiveCold);
            lamp.CurrentColor = new Color(0.4f, 1f, 0.5f);
            lamp.AddBox(At(new Vector3(0.95f, 1.22f, 0f)), new Vector3(0.03f, 0.06f, 0.06f), rotation);

            steel.CurrentColor = Color.white;
            rust.CurrentColor = Color.white;
            lamp.CurrentColor = Color.white;
        }

        /// <summary>The sump pump: a vertical column pump with its discharge run.</summary>
        public static void SumpPump(SurfaceBatch batch, Vector3 floorPosition, Quaternion rotation)
        {
            var steel = batch.For(SurfaceKind.SteelRusted);
            Vector3 At(Vector3 local) => floorPosition + rotation * local;

            steel.CurrentColor = new Color(0.42f, 0.34f, 0.26f);
            steel.AddCylinder(At(new Vector3(0f, 0f, 0f)), 0.26f, 2.4f, 12, rotation);       // column
            steel.AddCylinder(At(new Vector3(0f, 2.4f, 0f)), 0.4f, 0.45f, 12, rotation);     // motor
            steel.AddBox(At(new Vector3(0f, 2.9f, 0f)), new Vector3(0.5f, 0.12f, 0.5f), rotation);

            // Discharge pipe, elbowed away into the wall.
            steel.AddCylinder(At(new Vector3(0f, 2.2f, 0f)), 0.12f, 1.6f, 8,
                rotation * Quaternion.Euler(0f, 0f, 90f));

            steel.CurrentColor = Color.white;
        }

        /// <summary>A run of pipes along a wall or ceiling.</summary>
        public static void PipeRun(SurfaceBatch batch, Vector3 from, Vector3 to, int pipeCount,
            float radius, float spacing, Vector3 spreadAxis)
        {
            var steel = batch.For(SurfaceKind.SteelRusted);
            var direction = to - from;
            float length = direction.magnitude;
            if (length < 0.1f) return;

            var rotation = Quaternion.FromToRotation(Vector3.up, direction / length);
            spreadAxis = spreadAxis.normalized;

            for (int i = 0; i < pipeCount; i++)
            {
                float offset = (i - (pipeCount - 1) * 0.5f) * spacing;
                float shade = Mathf.Lerp(0.7f, 1.05f, ProcNoise.Hash(i, 3, 1, 12));
                steel.CurrentColor = new Color(0.42f * shade, 0.32f * shade, 0.24f * shade);

                steel.AddCylinder(from + spreadAxis * offset, radius, length, 8, rotation, caps: false);
            }

            steel.CurrentColor = Color.white;
        }

        /// <summary>Steel catwalk with handrails, for the crossing and the generator bay.</summary>
        public static void Catwalk(SurfaceBatch batch, Vector3 from, Vector3 to, float width)
        {
            var deck = batch.For(SurfaceKind.Decking);
            var steel = batch.For(SurfaceKind.SteelRusted);

            var direction = to - from;
            float length = direction.magnitude;
            if (length < 0.2f) return;

            var flat = new Vector3(direction.x, 0f, direction.z);
            var rotation = flat.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(flat.normalized, Vector3.up)
                : Quaternion.identity;

            var centre = (from + to) * 0.5f;

            deck.UvScale = 0.8f;
            deck.CurrentColor = new Color(0.7f, 0.62f, 0.5f);
            deck.AddBox(centre, new Vector3(width, 0.1f, length), rotation);

            steel.CurrentColor = new Color(0.4f, 0.35f, 0.3f);
            for (int side = -1; side <= 1; side += 2)
            {
                var railOffset = rotation * new Vector3(side * width * 0.5f, 0f, 0f);

                // Top rail.
                steel.AddBox(centre + railOffset + Vector3.up * 1.0f,
                    new Vector3(0.05f, 0.05f, length), rotation);

                // Stanchions.
                int posts = Mathf.Max(2, Mathf.RoundToInt(length / 1.6f));
                for (int p = 0; p < posts; p++)
                {
                    float t = posts == 1 ? 0.5f : p / (float)(posts - 1);
                    var along = Vector3.Lerp(from, to, t);
                    steel.AddBox(along + railOffset + Vector3.up * 0.5f,
                        new Vector3(0.05f, 1.0f, 0.05f), rotation);
                }
            }

            deck.CurrentColor = Color.white;
            steel.CurrentColor = Color.white;
        }

        // =====================================================================
        // Station and service rooms
        // =====================================================================

        /// <summary>The control desk the player sits at: worktop, returns, and a monitor bank.</summary>
        public static void ControlDesk(SurfaceBatch batch, Vector3 floorPosition, Quaternion rotation)
        {
            var steel = batch.For(SurfaceKind.SteelPainted);
            var deck = batch.For(SurfaceKind.Decking);

            Vector3 At(Vector3 local) => floorPosition + rotation * local;

            deck.UvScale = 1.2f;
            deck.CurrentColor = new Color(0.55f, 0.45f, 0.34f);
            deck.AddBox(At(new Vector3(0f, 0.74f, 0f)), new Vector3(2.6f, 0.06f, 0.8f), rotation);

            steel.CurrentColor = new Color(0.26f, 0.28f, 0.28f);
            steel.AddBox(At(new Vector3(-1.15f, 0.37f, 0f)), new Vector3(0.1f, 0.74f, 0.7f), rotation);
            steel.AddBox(At(new Vector3(1.15f, 0.37f, 0f)), new Vector3(0.1f, 0.74f, 0.7f), rotation);
            steel.AddBox(At(new Vector3(0f, 0.3f, 0.35f)), new Vector3(2.3f, 0.6f, 0.08f), rotation);

            // Monitor bank on the far side of the desk.
            steel.CurrentColor = new Color(0.16f, 0.17f, 0.18f);
            for (int i = -1; i <= 1; i++)
            {
                steel.AddBox(At(new Vector3(i * 0.72f, 1.12f, 0.3f)),
                    new Vector3(0.66f, 0.56f, 0.5f), rotation * Quaternion.Euler(-8f, -i * 12f, 0f));
            }

            var glass = batch.For(SurfaceKind.Glass);
            glass.CurrentColor = new Color(0.06f, 0.08f, 0.08f, 0.95f);
            for (int i = -1; i <= 1; i++)
            {
                glass.AddBox(At(new Vector3(i * 0.72f, 1.12f, 0.06f)),
                    new Vector3(0.54f, 0.44f, 0.03f), rotation * Quaternion.Euler(-8f, -i * 12f, 0f));
            }

            steel.CurrentColor = Color.white;
            deck.CurrentColor = Color.white;
            glass.CurrentColor = Color.white;
        }

        /// <summary>A bank of staff lockers.</summary>
        public static void LockerBank(SurfaceBatch batch, Vector3 floorPosition, Quaternion rotation, int count)
        {
            var steel = batch.For(SurfaceKind.SteelPainted);

            for (int i = 0; i < count; i++)
            {
                float x = (i - (count - 1) * 0.5f) * 0.42f;
                float shade = Mathf.Lerp(0.75f, 1f, ProcNoise.Hash(i, 9, 2, 4));
                steel.CurrentColor = new Color(0.28f * shade, 0.36f * shade, 0.33f * shade);

                var body = floorPosition + rotation * new Vector3(x, 0.9f, 0f);
                steel.AddBox(body, new Vector3(0.4f, 1.8f, 0.45f), rotation);

                // One door standing open.
                if (ProcNoise.Hash(i, 10, 2, 4) < 0.2f)
                {
                    steel.CurrentColor = new Color(0.24f, 0.3f, 0.28f);
                    steel.AddBox(floorPosition + rotation * new Vector3(x - 0.2f, 0.9f, -0.42f),
                        new Vector3(0.04f, 1.7f, 0.4f), rotation * Quaternion.Euler(0f, 55f, 0f));
                }
            }

            steel.CurrentColor = Color.white;
        }

        /// <summary>Workshop bench with an empty endoskeleton cradle above it.</summary>
        public static void Workbench(SurfaceBatch batch, Vector3 floorPosition, Quaternion rotation, bool occupied)
        {
            var steel = batch.For(SurfaceKind.SteelRusted);
            Vector3 At(Vector3 local) => floorPosition + rotation * local;

            steel.CurrentColor = new Color(0.36f, 0.32f, 0.28f);
            steel.AddBox(At(new Vector3(0f, 0.88f, 0f)), new Vector3(2.4f, 0.08f, 0.75f), rotation);
            steel.AddBox(At(new Vector3(-1.05f, 0.44f, 0f)), new Vector3(0.08f, 0.88f, 0.65f), rotation);
            steel.AddBox(At(new Vector3(1.05f, 0.44f, 0f)), new Vector3(0.08f, 0.88f, 0.65f), rotation);

            // Cradle: two uprights and a yoke.
            steel.CurrentColor = new Color(0.3f, 0.3f, 0.32f);
            steel.AddBox(At(new Vector3(-0.6f, 1.5f, 0f)), new Vector3(0.07f, 1.2f, 0.07f), rotation);
            steel.AddBox(At(new Vector3(0.6f, 1.5f, 0f)), new Vector3(0.07f, 1.2f, 0.07f), rotation);
            steel.AddBox(At(new Vector3(0f, 2.06f, 0f)), new Vector3(1.3f, 0.08f, 0.1f), rotation);

            if (occupied)
            {
                // A spare frame hanging in the cradle. Not one of tonight's five.
                steel.CurrentColor = new Color(0.5f, 0.5f, 0.52f);
                steel.AddCylinder(At(new Vector3(0f, 1.0f, 0f)), 0.12f, 0.9f, 8, rotation);
                steel.AddBox(At(new Vector3(0f, 1.95f, 0f)), new Vector3(0.26f, 0.24f, 0.3f), rotation);
            }

            steel.CurrentColor = Color.white;
        }

        /// <summary>Shelving for the gift grotto, stacked with polished stone.</summary>
        public static void ShelfUnit(SurfaceBatch batch, Vector3 floorPosition, Quaternion rotation, int seed)
        {
            var wood = batch.For(SurfaceKind.Decking);
            var stone = batch.For(SurfaceKind.CaveRock);

            wood.UvScale = 1.4f;
            wood.CurrentColor = new Color(0.5f, 0.4f, 0.3f);

            for (int shelf = 0; shelf < 4; shelf++)
            {
                float y = 0.35f + shelf * 0.45f;
                wood.AddBox(floorPosition + rotation * new Vector3(0f, y, 0f),
                    new Vector3(1.6f, 0.05f, 0.44f), rotation);

                for (int i = 0; i < 6; i++)
                {
                    if (ProcNoise.Hash(shelf, i, 1, seed) < 0.35f) continue;

                    float x = (i - 2.5f) * 0.26f;
                    float size = Mathf.Lerp(0.07f, 0.16f, ProcNoise.Hash(shelf, i, 2, seed));
                    float hue = ProcNoise.Hash(shelf, i, 3, seed);
                    stone.CurrentColor = Color.HSVToRGB(hue * 0.15f + 0.05f, 0.35f, 0.8f);
                    stone.AddBox(floorPosition + rotation * new Vector3(x, y + size * 0.5f + 0.03f, 0f),
                        Vector3.one * size,
                        rotation * Quaternion.Euler(0f, ProcNoise.Hash(shelf, i, 4, seed) * 90f, 0f));
                }
            }

            wood.CurrentColor = new Color(0.42f, 0.34f, 0.26f);
            wood.AddBox(floorPosition + rotation * new Vector3(-0.8f, 1.0f, 0f), new Vector3(0.06f, 2f, 0.44f), rotation);
            wood.AddBox(floorPosition + rotation * new Vector3(0.8f, 1.0f, 0f), new Vector3(0.06f, 2f, 0.44f), rotation);

            wood.CurrentColor = Color.white;
            stone.CurrentColor = Color.white;
        }

        /// <summary>Stacked crates and drums.</summary>
        public static void Clutter(SurfaceBatch batch, Vector3 centre, float spread, int count, int seed)
        {
            var wood = batch.For(SurfaceKind.Decking);
            var steel = batch.For(SurfaceKind.SteelRusted);

            for (int i = 0; i < count; i++)
            {
                var offset = new Vector3(
                    (ProcNoise.Hash(i, 21, 0, seed) - 0.5f) * spread,
                    0f,
                    (ProcNoise.Hash(i, 22, 0, seed) - 0.5f) * spread);

                float yaw = ProcNoise.Hash(i, 23, 0, seed) * 360f;

                if (ProcNoise.Hash(i, 24, 0, seed) < 0.55f)
                {
                    float s = Mathf.Lerp(0.45f, 0.8f, ProcNoise.Hash(i, 25, 0, seed));
                    wood.UvScale = 1.5f;
                    wood.CurrentColor = new Color(0.46f, 0.38f, 0.28f);
                    wood.AddBox(centre + offset + Vector3.up * (s * 0.5f), Vector3.one * s,
                        Quaternion.Euler(0f, yaw, 0f));
                }
                else
                {
                    steel.CurrentColor = new Color(0.38f, 0.26f, 0.18f);
                    steel.AddCylinder(centre + offset, 0.29f, 0.88f, 12, Quaternion.Euler(0f, yaw, 0f));
                }
            }

            wood.CurrentColor = Color.white;
            steel.CurrentColor = Color.white;
        }

        /// <summary>Turnstiles at the ticket grotto, chained shut since 1993.</summary>
        public static void Turnstiles(SurfaceBatch batch, Vector3 floorPosition, Quaternion rotation, int count)
        {
            var steel = batch.For(SurfaceKind.SteelRusted);
            steel.CurrentColor = new Color(0.44f, 0.4f, 0.36f);

            for (int i = 0; i < count; i++)
            {
                float x = (i - (count - 1) * 0.5f) * 1.1f;
                var basePoint = floorPosition + rotation * new Vector3(x, 0f, 0f);

                steel.AddCylinder(basePoint, 0.16f, 1.0f, 10, rotation);
                steel.AddBox(basePoint + rotation * new Vector3(0f, 1.0f, 0f),
                    new Vector3(0.3f, 0.14f, 0.3f), rotation);

                // Three arms at 120 degrees.
                for (int arm = 0; arm < 3; arm++)
                {
                    var armRotation = rotation * Quaternion.Euler(0f, arm * 120f + 20f, 0f);
                    steel.AddCylinder(basePoint + rotation * new Vector3(0f, 0.98f, 0f),
                        0.035f, 0.55f, 6, armRotation * Quaternion.Euler(90f, 0f, 0f));
                }
            }

            steel.CurrentColor = Color.white;
        }
    }
}
