using UnityEngine;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.Procedural
{
    /// <summary>
    /// Builds the whole cave from a <see cref="FacilityLayout"/>: shells, connecting
    /// tunnels, props, fixed lighting and the water table.
    ///
    /// Geometry only. Gameplay components — doors, cameras, floodlights, the station,
    /// the cast — are attached afterwards by the editor's scene builder. Keeping the
    /// split means this can be re-run to regenerate the art after a layout change
    /// without disturbing anything a designer has placed by hand.
    /// </summary>
    public static class FacilityBuilder
    {
        public const string GeometryRootName = "Facility Geometry";

        public static GameObject Build(FacilityLayout layout, Transform parent, int seed = 1337)
        {
            if (layout == null)
            {
                GLog.Error(LogChannel.Procedural, "FacilityBuilder was given no layout.");
                return null;
            }

            var graph = layout.BuildGraph();
            int facilityLayer = LayerMask.NameToLayer("Facility");
            if (facilityLayer < 0) facilityLayer = 0;

            var root = new GameObject(GeometryRootName);
            if (parent != null) root.transform.SetParent(parent, worldPositionStays: false);

            int totalTriangles = 0;

            // ---- Node shells and dressing ------------------------------------
            foreach (var node in graph.Nodes)
            {
                var go = new GameObject($"Node_{node.Id}");
                go.transform.SetParent(root.transform, worldPositionStays: false);
                go.transform.position = node.Position;

                // Geometry is generated around the origin and positioned by the
                // transform, so a node can be moved in the layout without rebuilding.
                var local = CloneAtOrigin(node);
                var batch = new SurfaceBatch();

                int nodeSeed = seed ^ (node.Id.Key.GetHashCode() & 0x7FFFFFFF);
                CaveShaper.BuildNodeShell(batch, local, nodeSeed);
                Dress(batch, node, nodeSeed);

                totalTriangles += batch.Flush(go.transform, node.Id.Key, addColliders: true,
                    layer: facilityLayer, recalculateNormals: true);
            }

            // ---- Connecting tunnels -------------------------------------------
            var connectors = new GameObject("Connectors");
            connectors.transform.SetParent(root.transform, worldPositionStays: false);

            var connectorBatch = new SurfaceBatch();
            var links = graph.Links;
            for (int i = 0; i < links.Count; i++)
            {
                var link = links[i];
                var a = graph.Node(link.A);
                var b = graph.Node(link.B);
                if (a == null || b == null) continue;

                float radius = ConnectorRadius(link);

                // Bore wall-to-wall, along the floor. The previous version started the
                // tube at a fraction of the node's largest dimension and at the node's
                // *centre* height, which for the control room meant a 1.5 metre tube
                // beginning over a metre inside the room at eye level — it filled the
                // player's entire view on the first frame.
                var horizontal = new Vector3(b.Position.x - a.Position.x, 0f, b.Position.z - a.Position.z);
                if (horizontal.sqrMagnitude < 0.01f) continue;   // stacked vertically; a shaft, not a bore

                float span = horizontal.magnitude;
                horizontal /= span;

                var from = WallPoint(a, horizontal, radius);
                var to = WallPoint(b, -horizontal, radius);

                // If the two spaces already meet, there is no gap left to bore and any
                // tube drawn here would be inside one of the rooms.
                float gap = Vector3.Dot(to - from, horizontal);
                if (gap < radius) continue;

                var surface = link.MinWater > 0f ? SurfaceKind.CaveRockDamp : SurfaceKind.CaveRock;
                CaveShaper.BuildConnector(connectorBatch, from, to, radius, seed + i * 31, surface);
            }

            totalTriangles += connectorBatch.Flush(connectors.transform, "Connector",
                addColliders: true, layer: facilityLayer, recalculateNormals: true);

            // ---- Lighting -------------------------------------------------------
            BuildFixedLighting(root.transform, graph);

            // ---- Water table ----------------------------------------------------
            BuildWaterTable(root.transform, graph);

            GLog.Info(LogChannel.Procedural,
                $"Built {layout.siteName}: {graph.NodeCount} nodes, {totalTriangles} triangles.");

            return root;
        }

        /// <summary>
        /// Where a horizontal bore leaves a node: through the side wall, at floor
        /// level. CaveShaper puts a node's floor at 35% of its height below centre.
        /// </summary>
        private static Vector3 WallPoint(FacilityNode node, Vector3 horizontal, float tunnelRadius)
        {
            var half = node.Size * 0.5f;

            // Distance from the centre to the box wall along this heading.
            float alongX = Mathf.Abs(horizontal.x) > 1e-4f ? half.x / Mathf.Abs(horizontal.x) : float.MaxValue;
            float alongZ = Mathf.Abs(horizontal.z) > 1e-4f ? half.z / Mathf.Abs(horizontal.z) : float.MaxValue;
            float toWall = Mathf.Min(alongX, alongZ);

            float floorY = node.Position.y - node.Size.y * 0.35f;

            var mouth = new Vector3(node.Position.x, floorY + tunnelRadius * 0.95f, node.Position.z);
            return mouth + horizontal * toWall;
        }

        private static float ConnectorRadius(FacilityLink link)
        {
            if ((link.Allowed & TraversalMask.Walk) != 0) return 1.5f;
            if ((link.Allowed & TraversalMask.Swim) != 0) return 1.3f;
            if ((link.Allowed & TraversalMask.Climb) != 0) return 1.1f;
            return 0.75f;     // crawl and burrow
        }

        private static FacilityNode CloneAtOrigin(FacilityNode node) => new FacilityNode
        {
            Id = node.Id,
            DisplayName = node.DisplayName,
            Kind = node.Kind,
            Zone = node.Zone,
            Position = Vector3.zero,
            Size = node.Size,
            HasCamera = node.HasCamera,
            HasAmbientLight = node.HasAmbientLight,
            StationCoupling = node.StationCoupling
        };

        // =====================================================================
        // Per-room dressing, in node-local space
        // =====================================================================

        private static void Dress(SurfaceBatch batch, FacilityNode node, int seed)
        {
            float floorY = -node.Size.y * 0.35f;
            var floor = new Vector3(0f, floorY, 0f);

            switch (node.Id.Key)
            {
                case "STATION":
                    PropFactory.ControlDesk(batch, floor + new Vector3(0f, 0f, 1.6f), Quaternion.identity);
                    PropFactory.PipeRun(batch,
                        floor + new Vector3(-3f, node.Size.y * 0.85f, -3.5f),
                        floor + new Vector3(-3f, node.Size.y * 0.85f, 3.5f),
                        3, 0.07f, 0.2f, Vector3.right);
                    PropFactory.Clutter(batch, floor + new Vector3(2.4f, 0f, -2.6f), 1.6f, 3, seed);
                    break;

                case "ADIT_N":
                case "ADIT_S":
                    PropFactory.PipeRun(batch,
                        floor + new Vector3(-1.3f, node.Size.y * 0.8f, -node.Size.z * 0.45f),
                        floor + new Vector3(-1.3f, node.Size.y * 0.8f, node.Size.z * 0.45f),
                        4, 0.06f, 0.16f, Vector3.right);
                    break;

                case "SUMP":
                    PropFactory.SumpPump(batch, floor + new Vector3(1.4f, 0f, 0f), Quaternion.identity);
                    CaveShaper.BuildBreakdown(batch, floor + new Vector3(-1.5f, 0f, -1f), 3f, 6, seed);
                    break;

                case "GEN":
                    PropFactory.Genset(batch, floor + new Vector3(-1.4f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
                    PropFactory.Clutter(batch, floor + new Vector3(2.6f, 0f, 2f), 2f, 5, seed);
                    break;

                case "WORKSHOP":
                    PropFactory.Workbench(batch, floor + new Vector3(0f, 0f, 3f), Quaternion.identity, occupied: false);
                    PropFactory.Workbench(batch, floor + new Vector3(-3f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f), occupied: true);
                    PropFactory.Workbench(batch, floor + new Vector3(3f, 0f, 0f), Quaternion.Euler(0f, -90f, 0f), occupied: false);
                    PropFactory.Clutter(batch, floor + new Vector3(0f, 0f, -3f), 3f, 6, seed);
                    break;

                case "LOCKER":
                    PropFactory.LockerBank(batch, floor + new Vector3(0f, 0f, 2.4f), Quaternion.identity, 6);
                    PropFactory.LockerBank(batch, floor + new Vector3(0f, 0f, -2.4f), Quaternion.Euler(0f, 180f, 0f), 6);
                    break;

                case "LOBBY":
                    PropFactory.Turnstiles(batch, floor + new Vector3(0f, 0f, -2f), Quaternion.identity, 4);
                    PropFactory.Clutter(batch, floor + new Vector3(-5f, 0f, 3f), 3f, 4, seed);
                    break;

                case "GIFT":
                    PropFactory.ShelfUnit(batch, floor + new Vector3(-2.4f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f), seed);
                    PropFactory.ShelfUnit(batch, floor + new Vector3(2.4f, 0f, 0f), Quaternion.Euler(0f, -90f, 0f), seed + 5);
                    PropFactory.ShelfUnit(batch, floor + new Vector3(0f, 0f, 2.8f), Quaternion.identity, seed + 9);
                    break;

                case "MIDWAY":
                    PropFactory.ArcadeRow(batch, floor + new Vector3(-6f, 0f, 4.2f), Vector3.right, 7,
                        Quaternion.Euler(0f, 180f, 0f), seed);
                    PropFactory.ArcadeRow(batch, floor + new Vector3(-4f, 0f, -4.2f), Vector3.right, 5,
                        Quaternion.identity, seed + 40);
                    break;

                case "DINE":
                    PropFactory.SpringPool(batch, floor + new Vector3(-3.5f, 0f, 2f), 2.6f, seed);
                    PropFactory.SpringPool(batch, floor + new Vector3(2.5f, 0f, -1.5f), 2.0f, seed + 3);
                    PropFactory.SpringPool(batch, floor + new Vector3(4.5f, 0f, 3.5f), 1.5f, seed + 7);
                    break;

                case "GRAND":
                    PropFactory.TheatreSeating(batch, floor + new Vector3(2f, 0f, -2f),
                        Quaternion.Euler(0f, 90f, 0f), rows: 6, seatsPerRow: 10, seed: seed);
                    CaveShaper.BuildBreakdown(batch, floor + new Vector3(-8f, 0f, 6f), 4f, 5, seed);
                    break;

                case "STAGE":
                    PropFactory.StagePlatform(batch, floor, new Vector3(9f, 0.8f, 6f),
                        Quaternion.Euler(0f, 90f, 0f));
                    break;

                case "XING":
                    PropFactory.Catwalk(batch,
                        floor + new Vector3(0f, 0.4f, -node.Size.z * 0.5f),
                        floor + new Vector3(0f, 0.4f, node.Size.z * 0.5f), 2.4f);
                    break;

                case "RIVER":
                    CaveShaper.BuildBreakdown(batch, floor, 6f, 9, seed);
                    break;

                case "DEEP":
                    CaveShaper.BuildBreakdown(batch, floor, 12f, 18, seed);
                    PropFactory.Clutter(batch, floor + new Vector3(4f, 0f, -3f), 4f, 5, seed + 11);
                    break;

                case "INCLINE":
                    PropFactory.Catwalk(batch,
                        floor + new Vector3(0f, 0f, -node.Size.z * 0.4f),
                        floor + new Vector3(0f, 2.5f, node.Size.z * 0.4f), 1.8f);
                    break;
            }
        }

        // =====================================================================
        // Lighting and water
        // =====================================================================

        private static void BuildFixedLighting(Transform parent, FacilityGraph graph)
        {
            var root = new GameObject("Fixed Lighting");
            root.transform.SetParent(parent, worldPositionStays: false);

            foreach (var node in graph.Nodes)
            {
                if (!node.HasAmbientLight) continue;

                var go = new GameObject($"Light_{node.Id}");
                go.transform.SetParent(root.transform, worldPositionStays: false);
                go.transform.position = node.Position + Vector3.up * (node.Size.y * 0.28f);

                bool isStation = node.Kind == NodeKind.Station;

                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = Mathf.Max(node.Size.x, node.Size.z) * (isStation ? 1.8f : 1.1f);

                // Emergency circuit: sodium, dim, and losing the argument with the dark.
                // The station is the exception — the player has to be able to read the
                // room they spend the entire game sitting in.
                light.color = new Color(1f, 0.82f, 0.58f);
                light.intensity = isStation ? 4.2f : 0.9f;
                light.shadows = LightShadows.Soft;
                light.shadowStrength = isStation ? 0.6f : 0.85f;
                light.renderMode = LightRenderMode.ForcePixel;

                if (!isStation) continue;

                // A second, colder fill behind the desk, so the room reads as a space
                // with depth instead of one bulb in a brown fog.
                var fillObject = new GameObject("Light_STATION_Fill");
                fillObject.transform.SetParent(root.transform, worldPositionStays: false);
                fillObject.transform.position = node.Position + new Vector3(0f, node.Size.y * 0.2f, -node.Size.z * 0.3f);

                var stationFill = fillObject.AddComponent<Light>();
                stationFill.type = LightType.Point;
                stationFill.range = Mathf.Max(node.Size.x, node.Size.z) * 1.2f;
                stationFill.color = new Color(0.62f, 0.74f, 0.9f);
                stationFill.intensity = 1.1f;
                stationFill.shadows = LightShadows.None;
            }

            // One very dim fill so the unlit caverns read as black rather than as void.
            var fill = new GameObject("Ambient Fill");
            fill.transform.SetParent(root.transform, worldPositionStays: false);
            fill.transform.rotation = Quaternion.Euler(62f, 34f, 0f);

            var directional = fill.AddComponent<Light>();
            directional.type = LightType.Directional;
            directional.color = new Color(0.4f, 0.5f, 0.62f);
            directional.intensity = 0.035f;
            directional.shadows = LightShadows.None;
        }

        private static void BuildWaterTable(Transform parent, FacilityGraph graph)
        {
            // Bracket the cave so level 0 is below the deepest floor and level 1 is
            // the control room going under.
            float lowest = float.MaxValue;
            float stationFloor = 0f;
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;

            foreach (var node in graph.Nodes)
            {
                float floor = node.Position.y - node.Size.y * 0.35f;
                lowest = Mathf.Min(lowest, floor);

                if (node.Kind == NodeKind.Station) stationFloor = floor;

                minX = Mathf.Min(minX, node.Position.x - node.Size.x);
                maxX = Mathf.Max(maxX, node.Position.x + node.Size.x);
                minZ = Mathf.Min(minZ, node.Position.z - node.Size.z);
                maxZ = Mathf.Max(maxZ, node.Position.z + node.Size.z);
            }

            var go = new GameObject("Water Table");
            go.transform.SetParent(parent, worldPositionStays: false);

            var builder = new MeshBuilder(8);
            builder.UvScale = 0.25f;
            builder.CurrentColor = new Color(0.1f, 0.28f, 0.3f, 0.88f);

            float halfX = (maxX - minX) * 0.5f + 6f;
            float halfZ = (maxZ - minZ) * 0.5f + 6f;
            var centre = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f);

            builder.AddQuad(
                new Vector3(-halfX, 0f, -halfZ),
                new Vector3(halfX, 0f, -halfZ),
                new Vector3(halfX, 0f, halfZ),
                new Vector3(-halfX, 0f, halfZ),
                Vector3.up);

            var surface = new GameObject("Surface");
            surface.transform.SetParent(go.transform, worldPositionStays: false);
            surface.transform.localPosition = new Vector3(centre.x, 0f, centre.z);

            int waterLayer = LayerMask.NameToLayer("WaterSurface");
            surface.layer = waterLayer >= 0 ? waterLayer : 0;

            surface.AddComponent<MeshFilter>().sharedMesh = builder.ToMesh("WaterTable");
            var renderer = surface.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = MaterialLibrary.Get(SurfaceKind.Water);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var driver = go.AddComponent<WaterLevelDriver>();
            driver.Configure(lowest - 1.5f, stationFloor + 0.1f);
        }
    }
}
