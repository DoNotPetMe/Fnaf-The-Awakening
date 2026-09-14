using System.Collections.Generic;
using UnityEngine;
using Grotto.Core;

namespace Grotto.Procedural
{
    /// <summary>
    /// Collects generated geometry per surface and emits one renderer per material.
    ///
    /// Without this, a cavern with rock, carpet, steel and a dozen props becomes
    /// forty GameObjects and forty draw calls. Batching by surface makes it four, and
    /// keeps the scene hierarchy legible enough to actually work in.
    /// </summary>
    public sealed class SurfaceBatch
    {
        private readonly Dictionary<SurfaceKind, MeshBuilder> _builders =
            new Dictionary<SurfaceKind, MeshBuilder>(8);

        /// <summary>Gets (or starts) the builder for a surface.</summary>
        public MeshBuilder For(SurfaceKind kind)
        {
            if (_builders.TryGetValue(kind, out var builder)) return builder;

            builder = new MeshBuilder(2048);
            _builders[kind] = builder;
            return builder;
        }

        public bool IsEmpty
        {
            get
            {
                foreach (var pair in _builders)
                    if (pair.Value.VertexCount > 0) return false;
                return true;
            }
        }

        /// <summary>
        /// Bakes every non-empty surface into a child of <paramref name="parent"/>.
        /// Returns the total triangle count, which the builder logs so a designer can
        /// see immediately when a layout change has made a room expensive.
        /// </summary>
        public int Flush(Transform parent, string namePrefix, bool addColliders,
            int layer = 0, bool recalculateNormals = false, bool staticGeometry = true,
            float occlusion = 1f)
        {
            int triangles = 0;

            foreach (var pair in _builders)
            {
                var builder = pair.Value;
                if (builder.VertexCount == 0) continue;

                // Vertex occlusion before baking. Water is excluded: it is a flat plane
                // whose curvature is zero everywhere, so the pass would only cost time,
                // and the shader reads its alpha channel for something else.
                if (occlusion > 0f && pair.Key != SurfaceKind.Water)
                    builder.BakeVertexOcclusion(occlusion);

                var mesh = builder.ToMesh($"{namePrefix}_{pair.Key}", recalculateNormals);
                triangles += mesh.triangles.Length / 3;

                var go = new GameObject($"{namePrefix}_{pair.Key}");
                go.transform.SetParent(parent, worldPositionStays: false);

                // NameToLayer returns -1 for a missing layer, and assigning that throws.
                go.layer = layer >= 0 && layer < 32 ? layer : 0;

                go.AddComponent<MeshFilter>().sharedMesh = mesh;

                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = MaterialLibrary.Get(pair.Key);
                renderer.shadowCastingMode = pair.Key == SurfaceKind.Water
                    ? UnityEngine.Rendering.ShadowCastingMode.Off
                    : UnityEngine.Rendering.ShadowCastingMode.On;

                if (addColliders && pair.Key != SurfaceKind.Water && pair.Key != SurfaceKind.Glass)
                {
                    var collider = go.AddComponent<MeshCollider>();
                    collider.sharedMesh = mesh;
                }

#if UNITY_EDITOR
                if (staticGeometry) UnityEditor.GameObjectUtility.SetStaticEditorFlags(
                    go, UnityEditor.StaticEditorFlags.BatchingStatic |
                        UnityEditor.StaticEditorFlags.OccluderStatic |
                        UnityEditor.StaticEditorFlags.OccludeeStatic |
                        UnityEditor.StaticEditorFlags.ContributeGI);
#endif
            }

            GLog.Verbose(LogChannel.Procedural, $"{namePrefix}: {triangles} triangles across {_builders.Count} surfaces.");
            return triangles;
        }

        public void Clear() => _builders.Clear();
    }
}
