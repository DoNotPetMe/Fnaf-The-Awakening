using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Grotto.Facility;
using Grotto.Procedural;

namespace Grotto.Tests
{
    /// <summary>
    /// Tests that generated geometry faces the right way.
    ///
    /// Winding is the easiest thing to get backwards in procedural geometry and the
    /// hardest to notice: a reversed face is not obviously wrong, it is *invisible*.
    /// An inside-out cavern looks exactly like a cavern that failed to generate, and
    /// an inside-out skull looks like a character with no head.
    ///
    /// So the direction of every face is asserted numerically. The convention the
    /// whole builder follows is that the front face of a triangle (v0, v1, v2) is
    /// <c>cross(v1 - v0, v2 - v0)</c>, which is what AddQuad uses to derive the normal
    /// it stores.
    /// </summary>
    public class GeometryTests
    {
        private readonly List<Mesh> _meshes = new List<Mesh>();

        [TearDown]
        public void TearDown()
        {
            foreach (var mesh in _meshes)
                if (mesh != null) Object.DestroyImmediate(mesh);
            _meshes.Clear();
        }

        private Mesh Bake(MeshBuilder builder, string name)
        {
            var mesh = builder.ToMesh(name);
            _meshes.Add(mesh);
            return mesh;
        }

        /// <summary>Geometric front-face normal, by the builder's own convention.</summary>
        private static Vector3 FaceNormal(Vector3 v0, Vector3 v1, Vector3 v2)
            => Vector3.Cross(v1 - v0, v2 - v0);

        /// <summary>
        /// Fraction of faces whose front side points away from <paramref name="centre"/>.
        /// 1 means a solid seen from outside; 0 means it is inside out.
        /// </summary>
        private static float OutwardFraction(Mesh mesh, Vector3 centre, bool radialOnly = false)
        {
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;

            int outward = 0;
            int counted = 0;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                var v0 = vertices[triangles[i]];
                var v1 = vertices[triangles[i + 1]];
                var v2 = vertices[triangles[i + 2]];

                var normal = FaceNormal(v0, v1, v2);
                if (normal.sqrMagnitude < 1e-10f) continue;     // degenerate

                var centroid = (v0 + v1 + v2) / 3f;
                var outwardDirection = centroid - centre;

                // For a tube, the caps point along the axis and would drown out the
                // wall result, so they can be excluded.
                if (radialOnly) outwardDirection.y = 0f;
                if (outwardDirection.sqrMagnitude < 1e-6f) continue;

                counted++;
                if (Vector3.Dot(normal.normalized, outwardDirection.normalized) > 0f) outward++;
            }

            return counted == 0 ? 0f : outward / (float)counted;
        }

        // =====================================================================
        // Primitives
        // =====================================================================

        [Test]
        public void Box_FacesOutward()
        {
            var builder = new MeshBuilder();
            builder.AddBox(Vector3.zero, new Vector3(2f, 3f, 4f));

            var mesh = Bake(builder, "box");

            Assert.AreEqual(12, mesh.triangles.Length / 3, "Six quads, twelve triangles.");
            Assert.AreEqual(1f, OutwardFraction(mesh, Vector3.zero), 0.001f,
                "Every face of a box must face away from its centre.");
        }

        [Test]
        public void Box_InwardFlagReversesEveryFace()
        {
            var builder = new MeshBuilder();
            builder.AddBox(Vector3.zero, Vector3.one * 4f, Quaternion.identity, inward: true);

            var mesh = Bake(builder, "box-inward");

            Assert.AreEqual(0f, OutwardFraction(mesh, Vector3.zero), 0.001f,
                "An inward box is a room seen from inside — no face may point outward.");
        }

        [Test]
        public void Box_StoredNormalsAgreeWithTheWinding()
        {
            var builder = new MeshBuilder();
            builder.AddBox(Vector3.zero, Vector3.one * 2f);

            var mesh = Bake(builder, "box-normals");
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var triangles = mesh.triangles;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                var geometric = FaceNormal(vertices[triangles[i]],
                    vertices[triangles[i + 1]], vertices[triangles[i + 2]]).normalized;

                var stored = normals[triangles[i]].normalized;

                Assert.Greater(Vector3.Dot(geometric, stored), 0.9f,
                    "A stored normal that disagrees with the winding lights the surface backwards.");
            }
        }

        [Test]
        public void Cylinder_WallFacesOutward()
        {
            var builder = new MeshBuilder();
            builder.AddCylinder(Vector3.zero, radius: 1f, height: 4f, segments: 16);

            var mesh = Bake(builder, "cylinder");

            // Measure against the axis, so the caps do not dilute the wall result.
            Assert.AreEqual(1f, OutwardFraction(mesh, new Vector3(0f, 2f, 0f), radialOnly: true), 0.02f,
                "A cylinder is a solid; its wall must face away from its axis.");
        }

        [Test]
        public void Cone_FacesOutward()
        {
            var builder = new MeshBuilder();
            builder.AddCone(Vector3.zero, radius: 1f, height: 3f, segments: 12);

            var mesh = Bake(builder, "cone");

            Assert.Greater(OutwardFraction(mesh, new Vector3(0f, 0.75f, 0f)), 0.95f);
        }

        [Test]
        public void Sphere_FacesOutward()
        {
            var builder = new MeshBuilder();
            builder.AddSphere(Vector3.zero, radius: 1.5f, segments: 16, rings: 10);

            var mesh = Bake(builder, "sphere");

            Assert.AreEqual(1f, OutwardFraction(mesh, Vector3.zero), 0.001f,
                "Eyes, joints and skulls are spheres; an inverted one is an invisible head.");
        }

        [Test]
        public void Disc_FacesTheNormalItWasGiven()
        {
            foreach (var normal in new[] { Vector3.up, Vector3.down })
            {
                var builder = new MeshBuilder();
                builder.AddDisc(Vector3.zero, normal, radius: 1f, segments: 12,
                    rotation: Quaternion.identity);

                var mesh = Bake(builder, "disc-" + normal);
                var vertices = mesh.vertices;
                var triangles = mesh.triangles;

                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var face = FaceNormal(vertices[triangles[i]],
                        vertices[triangles[i + 1]], vertices[triangles[i + 2]]);

                    Assert.Greater(Vector3.Dot(face.normalized, normal), 0.9f,
                        $"A disc asked to face {normal} must face {normal}.");
                }
            }
        }

        [Test]
        public void Disc_FlipReversesIt()
        {
            var builder = new MeshBuilder();
            builder.AddDisc(Vector3.zero, Vector3.up, 1f, 12, Quaternion.identity, flip: true);

            var mesh = Bake(builder, "disc-flipped");
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;

            var face = FaceNormal(vertices[triangles[0]], vertices[triangles[1]], vertices[triangles[2]]);
            Assert.Less(Vector3.Dot(face.normalized, Vector3.up), -0.9f);
        }

        // =====================================================================
        // The cave
        // =====================================================================

        [Test]
        public void Chamber_FacesInward()
        {
            var batch = new SurfaceBatch();
            var node = new FacilityNode
            {
                Id = new Grotto.Core.NodeId("TEST"),
                DisplayName = "Test chamber",
                Kind = NodeKind.Cavern,
                Position = Vector3.zero,
                Size = new Vector3(14f, 8f, 14f)
            };

            CaveShaper.BuildChamber(batch, node, seed: 1, SurfaceKind.CaveRock);

            var mesh = Bake(batch.For(SurfaceKind.CaveRock), "chamber");
            Assert.Greater(mesh.triangles.Length, 0, "The chamber generated no geometry at all.");

            // A room is seen from inside, so its walls must face the axis. Speleothems
            // are excluded by building the shell alone.
            float outward = OutwardFraction(mesh, Vector3.zero, radialOnly: true);
            Assert.Less(outward, 0.1f,
                "A cavern must face inward, or the player stands inside an invisible room.");
        }

        [Test]
        public void Adit_FacesInward()
        {
            var batch = new SurfaceBatch();
            var node = new FacilityNode
            {
                Id = new Grotto.Core.NodeId("TESTADIT"),
                DisplayName = "Test adit",
                Kind = NodeKind.Adit,
                Position = Vector3.zero,
                Size = new Vector3(3.4f, 3f, 14f)
            };

            CaveShaper.BuildAdit(batch, node, seed: 1);

            var mesh = Bake(batch.For(SurfaceKind.Shotcrete), "adit");
            Assert.Greater(mesh.triangles.Length, 0);

            // The invert slab is a solid box inside the tube and legitimately faces
            // outward, so a clear majority rather than all of it.
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;

            int inward = 0, counted = 0;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var v0 = vertices[triangles[i]];
                var v1 = vertices[triangles[i + 1]];
                var v2 = vertices[triangles[i + 2]];

                var centroid = (v0 + v1 + v2) / 3f;

                // Distance from the tunnel's own axis, which runs along Z.
                var radial = new Vector3(centroid.x, centroid.y, 0f);
                if (radial.sqrMagnitude < 0.25f) continue;

                counted++;
                if (Vector3.Dot(FaceNormal(v0, v1, v2).normalized, radial.normalized) < 0f) inward++;
            }

            Assert.Greater(inward / (float)counted, 0.7f,
                "Most of a tunnel's surface is its bore, and a bore faces inward.");
        }

        // =====================================================================
        // Characters
        // =====================================================================

        [Test]
        public void AnimatronicFactory_BuildsARiggedModel()
        {
            var spec = new AnimatronicModelSpec { species = Species.Bear, height = 2f, seed = 1 };
            var model = AnimatronicFactory.Build(spec, "TestBear");

            try
            {
                var rig = model.GetComponent<AnimatronicRig>();
                Assert.IsNotNull(rig, "Every generated character needs a rig for the servo animator.");

                foreach (var (bone, label) in new[]
                         {
                             (rig.Hips, "Hips"), (rig.Chest, "Chest"), (rig.Head, "Head"),
                             (rig.Jaw, "Jaw"), (rig.HandLeft, "Hand.L"), (rig.FootRight, "Foot.R")
                         })
                {
                    Assert.IsNotNull(bone, $"{label} bone is missing.");
                }

                Assert.IsNotNull(rig.EyeRenderers);
                Assert.AreEqual(2, rig.EyeRenderers.Length);

                var renderers = model.GetComponentsInChildren<MeshRenderer>();
                Assert.Greater(renderers.Length, 8, "A character should be more than a handful of parts.");

                // The jaw has to hang below the head or the chatter animation is
                // rotating the wrong thing.
                Assert.Less(rig.Jaw.localPosition.y, 0.01f);

                // And the whole thing should stand roughly its requested height.
                var bounds = new Bounds(model.transform.position, Vector3.zero);
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);

                Assert.Greater(bounds.size.y, spec.height * 0.6f,
                    $"The build is {bounds.size.y:0.00}m tall but was specified at {spec.height:0.00}m.");
            }
            finally
            {
                Object.DestroyImmediate(model);
            }
        }

        [Test]
        public void AnimatronicFactory_BuildsEverySpecies()
        {
            foreach (Species species in System.Enum.GetValues(typeof(Species)))
            {
                var spec = new AnimatronicModelSpec { species = species, seed = (int)species + 1 };
                var model = AnimatronicFactory.Build(spec, species.ToString());

                try
                {
                    Assert.IsNotNull(model.GetComponent<AnimatronicRig>(), $"{species} produced no rig.");
                    Assert.Greater(model.GetComponentsInChildren<MeshRenderer>().Length, 5,
                        $"{species} produced almost no geometry.");
                }
                finally
                {
                    Object.DestroyImmediate(model);
                }
            }
        }

        // =====================================================================
        // Surfaces
        // =====================================================================

        [Test]
        public void TextureFactory_ProducesVariedSurfaces()
        {
            var texture = TextureFactory.Limestone(size: 64);

            try
            {
                Assert.AreEqual(64, texture.width);

                // A generator bug usually shows up as a flat fill, so check there is
                // actually variation rather than just that it produced something.
                var pixels = texture.GetPixels();
                float min = 1f, max = 0f;

                foreach (var pixel in pixels)
                {
                    min = Mathf.Min(min, pixel.grayscale);
                    max = Mathf.Max(max, pixel.grayscale);
                }

                Assert.Greater(max - min, 0.05f, "The limestone came out as a flat fill.");
                Assert.Less(max, 1.01f);
                Assert.Greater(min, -0.01f);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void ProcNoise_IsDeterministicAndBounded()
        {
            for (int i = 0; i < 200; i++)
            {
                var point = new Vector3(i * 0.37f, i * 0.11f, i * 0.73f);

                float a = ProcNoise.Fbm(point, seed: 4);
                float b = ProcNoise.Fbm(point, seed: 4);

                Assert.AreEqual(a, b, 1e-6f, "Geometry must regenerate identically on every machine.");
                Assert.GreaterOrEqual(a, 0f);
                Assert.LessOrEqual(a, 1f);

                float ridged = ProcNoise.Ridged(point, seed: 4);
                Assert.GreaterOrEqual(ridged, 0f);
                Assert.LessOrEqual(ridged, 1f);
            }
        }
    }
}
