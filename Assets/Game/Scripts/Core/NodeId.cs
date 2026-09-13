using System;

namespace Grotto.Core
{
    /// <summary>
    /// Identifier for a location in the facility graph.
    ///
    /// A struct wrapping an interned string: cheap to copy and compare, readable in
    /// the inspector and the dev console (<c>ai.move vesper CRAWL_A</c>), and it
    /// survives layout edits that would shuffle an enum's numeric values.
    /// </summary>
    [Serializable]
    public readonly struct NodeId : IEquatable<NodeId>
    {
        public static readonly NodeId None = default;

        private readonly string _key;

        public NodeId(string key)
        {
            _key = string.IsNullOrWhiteSpace(key) ? null : string.Intern(key.Trim().ToUpperInvariant());
        }

        public string Key => _key ?? string.Empty;
        public bool IsValid => _key != null;

        public bool Equals(NodeId other) => string.Equals(_key, other._key, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is NodeId other && Equals(other);
        public override int GetHashCode() => _key?.GetHashCode() ?? 0;
        public override string ToString() => _key ?? "<none>";

        public static bool operator ==(NodeId a, NodeId b) => a.Equals(b);
        public static bool operator !=(NodeId a, NodeId b) => !a.Equals(b);

        public static implicit operator NodeId(string key) => new NodeId(key);
    }
}
