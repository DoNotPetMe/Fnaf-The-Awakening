using System.Collections.Generic;
using UnityEngine;

namespace Grotto.Procedural
{
    /// <summary>
    /// Accumulates vertices and triangles, then bakes a <see cref="Mesh"/>.
    ///
    /// Everything visible in Grotto Springs is built through this — the cave shells,
    /// the props and the cast. Generating geometry rather than importing it keeps the
    /// repository text-only and diffable, makes the map's dimensions literally the
    /// same numbers the AI navigates by, and means a designer changing a room's size
    /// in the layout asset gets new geometry rather than a mismatch.
    ///
    /// Vertex colours carry per-surface variation (wear, damp, mineral staining) so
    /// one material can serve a whole cavern without a texture set.
    /// </summary>
    public sealed class MeshBuilder
    {
        private readonly List<Vector3> _vertices;
        private readonly List<Vector3> _normals;
        private readonly List<Vector2> _uvs;
        private readonly List<Color> _colors;
        private readonly List<int> _triangles;

        public MeshBuilder(int vertexCapacity = 512)
        {
            _vertices = new List<Vector3>(vertexCapacity);
            _normals = new List<Vector3>(vertexCapacity);
            _uvs = new List<Vector2>(vertexCapacity);
            _colors = new List<Color>(vertexCapacity);
            _triangles = new List<int>(vertexCapacity * 3);
        }

        public int VertexCount => _vertices.Count;
        public int TriangleCount => _triangles.Count / 3;

        /// <summary>Tint applied to every vertex added from now on.</summary>
        public Color CurrentColor { get; set; } = Color.white;

        /// <summary>World-space units per UV tile. Keeps texel density consistent across sizes.</summary>
        public float UvScale { get; set; } = 1f;

        public void Clear()
        {
            _vertices.Clear();
            _normals.Clear();
            _uvs.Clear();
            _colors.Clear();
            _triangles.Clear();
        }

        public int AddVertex(Vector3 position, Vector3 normal, Vector2 uv)
        {
            _vertices.Add(position);
            _normals.Add(normal);
            _uvs.Add(uv);
            _colors.Add(CurrentColor);
            return _vertices.Count - 1;
        }

        public void AddTriangle(int a, int b, int c)
        {
            _triangles.Add(a);
            _triangles.Add(b);
            _triangles.Add(c);
        }

        /// <summary>Two triangles over four corners, wound a-b-c / a-c-d.</summary>
        public void AddQuad(int a, int b, int c, int d)
        {
            AddTriangle(a, b, c);
            AddTriangle(a, c, d);
        }

        /// <summary>
        /// A flat quad from four corners in winding order. UVs are derived from the
        /// quad's own extents so texel density does not stretch on long walls.
        /// </summary>
        public void AddQuad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3? normalOverride = null)
        {
            var normal = normalOverride ?? Vector3.Cross(p1 - p0, p3 - p0).normalized;

            float width = Vector3.Distance(p0, p1) * UvScale;
            float height = Vector3.Distance(p0, p3) * UvScale;

            int a = AddVertex(p0, normal, new Vector2(0f, 0f));
            int b = AddVertex(p1, normal, new Vector2(width, 0f));
            int c = AddVertex(p2, normal, new Vector2(width, height));
            int d = AddVertex(p3, normal, new Vector2(0f, height));

            AddQuad(a, b, c, d);
        }

        /// <summary>
        /// An axis-aligned-then-rotated box. <paramref name="inward"/> flips the winding
        /// and normals so the box can be used as a room shell seen from inside.
        /// </summary>
        public void AddBox(Vector3 center, Vector3 size, Quaternion rotation, bool inward = false)
        {
            Vector3 h = size * 0.5f;

            // 8 corners in local space.
            Vector3 c000 = new Vector3(-h.x, -h.y, -h.z);
            Vector3 c100 = new Vector3(h.x, -h.y, -h.z);
            Vector3 c110 = new Vector3(h.x, h.y, -h.z);
            Vector3 c010 = new Vector3(-h.x, h.y, -h.z);
            Vector3 c001 = new Vector3(-h.x, -h.y, h.z);
            Vector3 c101 = new Vector3(h.x, -h.y, h.z);
            Vector3 c111 = new Vector3(h.x, h.y, h.z);
            Vector3 c011 = new Vector3(-h.x, h.y, h.z);

            Vector3 T(Vector3 p) => center + rotation * p;

            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                if (inward) AddQuad(T(d), T(c), T(b), T(a));
                else AddQuad(T(a), T(b), T(c), T(d));
            }

            Face(c001, c101, c111, c011);   // +Z
            Face(c100, c000, c010, c110);   // -Z
            Face(c101, c100, c110, c111);   // +X
            Face(c000, c001, c011, c010);   // -X
            Face(c010, c011, c111, c110);   // +Y
            Face(c000, c100, c101, c001);   // -Y
        }

        public void AddBox(Vector3 center, Vector3 size, bool inward = false)
            => AddBox(center, size, Quaternion.identity, inward);

        /// <summary>
        /// A ring of vertices around <paramref name="center"/> on the plane defined by
        /// <paramref name="rotation"/>. Returns the index of the first vertex. Pair with
        /// <see cref="BridgeRings"/> to make tubes, cones and lathes.
        /// </summary>
        public int AddRing(Vector3 center, Quaternion rotation, float radiusX, float radiusZ,
            int segments, float v, System.Func<int, float, Vector3> displace = null)
        {
            int first = _vertices.Count;

            for (int i = 0; i < segments; i++)
            {
                float t = i / (float)segments;
                float angle = t * Mathf.PI * 2f;
                var local = new Vector3(Mathf.Cos(angle) * radiusX, 0f, Mathf.Sin(angle) * radiusZ);

                var normal = rotation * new Vector3(local.x, 0f, local.z).normalized;
                var position = center + rotation * local;

                if (displace != null) position += displace(i, t);

                AddVertex(position, normal, new Vector2(t * Mathf.PI * 2f * radiusX * UvScale, v));
            }

            return first;
        }

        /// <summary>
        /// Stitches two equally sized rings into a tube wall.
        /// <paramref name="inward"/> faces the surface toward the tube's axis.
        /// </summary>
        public void BridgeRings(int ringA, int ringB, int segments, bool inward = false)
        {
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;

                int a0 = ringA + i, a1 = ringA + next;
                int b0 = ringB + i, b1 = ringB + next;

                if (inward)
                {
                    AddTriangle(a0, b0, b1);
                    AddTriangle(a0, b1, a1);
                }
                else
                {
                    AddTriangle(a0, b1, b0);
                    AddTriangle(a0, a1, b1);
                }
            }
        }

        /// <summary>A capped cylinder along local +Y.</summary>
        public void AddCylinder(Vector3 baseCenter, float radius, float height, int segments = 12,
            Quaternion? rotation = null, float topRadiusScale = 1f, bool caps = true)
        {
            var rot = rotation ?? Quaternion.identity;
            var top = baseCenter + rot * (Vector3.up * height);

            int lower = AddRing(baseCenter, rot, radius, radius, segments, 0f);
            int upper = AddRing(top, rot, radius * topRadiusScale, radius * topRadiusScale, segments, height * UvScale);
            BridgeRings(lower, upper, segments);

            if (!caps) return;

            AddDisc(top, rot * Vector3.up, radius * topRadiusScale, segments, rot);
            AddDisc(baseCenter, rot * Vector3.down, radius, segments, rot, flip: true);
        }

        /// <summary>A cone from a base ring to a single apex.</summary>
        public void AddCone(Vector3 baseCenter, float radius, float height, int segments = 10,
            Quaternion? rotation = null)
        {
            var rot = rotation ?? Quaternion.identity;
            var apexPosition = baseCenter + rot * (Vector3.up * height);

            int ring = AddRing(baseCenter, rot, radius, radius, segments, 0f);

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                var normal = (_normals[ring + i] + rot * Vector3.up).normalized;
                int apex = AddVertex(apexPosition, normal, new Vector2(i / (float)segments, 1f));
                AddTriangle(ring + i, apex, ring + next);
            }

            AddDisc(baseCenter, rot * Vector3.down, radius, segments, rot, flip: true);
        }

        /// <summary>A filled circle, used for cylinder caps.</summary>
        public void AddDisc(Vector3 center, Vector3 normal, float radius, int segments,
            Quaternion rotation, bool flip = false)
        {
            int centreIndex = AddVertex(center, normal, new Vector2(0.5f, 0.5f));

            int first = _vertices.Count;
            for (int i = 0; i < segments; i++)
            {
                float t = i / (float)segments;
                float angle = t * Mathf.PI * 2f;
                var local = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                AddVertex(center + rotation * local,
                    normal,
                    new Vector2(0.5f + Mathf.Cos(angle) * 0.5f, 0.5f + Mathf.Sin(angle) * 0.5f));
            }

            for (int i = 0; i < segments; i++)
            {
                int a = first + i;
                int b = first + (i + 1) % segments;
                if (flip) AddTriangle(centreIndex, b, a);
                else AddTriangle(centreIndex, a, b);
            }
        }

        /// <summary>
        /// A UV sphere from stacked rings. Used for eyes, joints and skull domes —
        /// the places on an animatronic where a box would read as a box.
        /// </summary>
        public void AddSphere(Vector3 centre, float radius, int segments = 12, int rings = 8,
            Vector3? scale = null)
        {
            var s = scale ?? Vector3.one;
            var ringStarts = new int[rings];

            for (int r = 0; r < rings; r++)
            {
                // Skip the poles; they are capped with fans below.
                float phi = Mathf.PI * (r + 1) / (rings + 1);
                float y = Mathf.Cos(phi) * radius * s.y;
                float ringRadius = Mathf.Sin(phi) * radius;

                ringStarts[r] = AddRing(centre + new Vector3(0f, y, 0f), Quaternion.identity,
                    ringRadius * s.x, ringRadius * s.z, segments, r / (float)rings);
            }

            for (int r = 0; r < rings - 1; r++)
                BridgeRings(ringStarts[r + 1], ringStarts[r], segments);

            int top = AddVertex(centre + new Vector3(0f, radius * s.y, 0f), Vector3.up, new Vector2(0.5f, 1f));
            int bottom = AddVertex(centre - new Vector3(0f, radius * s.y, 0f), Vector3.down, new Vector2(0.5f, 0f));

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                AddTriangle(top, ringStarts[0] + next, ringStarts[0] + i);
                AddTriangle(bottom, ringStarts[rings - 1] + i, ringStarts[rings - 1] + next);
            }
        }

        /// <summary>A box with its corners pulled in — reads as moulded plastic, not a crate.</summary>
        public void AddRoundedBox(Vector3 centre, Vector3 size, Quaternion rotation, float bevel = 0.08f)
        {
            bevel = Mathf.Clamp(bevel, 0f, 0.45f);
            var inner = size * (1f - bevel);

            AddBox(centre, new Vector3(size.x, inner.y, inner.z), rotation);
            AddBox(centre, new Vector3(inner.x, size.y, inner.z), rotation);
            AddBox(centre, new Vector3(inner.x, inner.y, size.z), rotation);
        }

        /// <summary>Appends another builder's geometry, offset and rotated.</summary>
        public void Append(MeshBuilder other, Vector3 offset, Quaternion rotation, Vector3 scale)
        {
            int baseIndex = _vertices.Count;

            for (int i = 0; i < other._vertices.Count; i++)
            {
                var p = Vector3.Scale(other._vertices[i], scale);
                _vertices.Add(offset + rotation * p);
                _normals.Add(rotation * other._normals[i]);
                _uvs.Add(other._uvs[i]);
                _colors.Add(other._colors[i]);
            }

            for (int i = 0; i < other._triangles.Count; i++)
                _triangles.Add(baseIndex + other._triangles[i]);
        }

        /// <summary>
        /// Bakes the mesh. Uses a 32-bit index buffer when the geometry needs it — cave
        /// shells comfortably exceed 65k vertices and silently wrapping would be worse
        /// than the small memory cost.
        /// </summary>
        public Mesh ToMesh(string meshName, bool recalculateNormals = false)
        {
            var mesh = new Mesh { name = meshName };

            if (_vertices.Count > 65000)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            mesh.SetColors(_colors);
            mesh.SetTriangles(_triangles, 0);

            if (recalculateNormals) mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(markNoLongerReadable: false);

            return mesh;
        }
    }
}
