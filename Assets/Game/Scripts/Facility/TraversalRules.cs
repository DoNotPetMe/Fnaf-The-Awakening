namespace Grotto.Facility
{
    /// <summary>
    /// The part of the traversal rule that depends only on the link, the body and the
    /// water — no scene, no components, no runtime state.
    ///
    /// Pulled out as a pure function so the edit-mode tests exercise the *same* code
    /// the AI uses, rather than a reimplementation of it that can quietly drift. The
    /// remaining conditions — a shut door, a lit node — need live objects and stay in
    /// <see cref="FacilityRuntime.CanTraverse"/>, which calls this first.
    /// </summary>
    public static class TraversalRules
    {
        /// <summary>
        /// Whether a body with <paramref name="capability"/> could use this link at
        /// <paramref name="waterLevel"/>, ignoring doors and lighting.
        /// </summary>
        public static bool IsPermitted(FacilityLink link, TraversalMask capability, float waterLevel)
        {
            if (link == null) return false;
            if ((link.Allowed & capability) == 0) return false;
            if (waterLevel < link.MinWater) return false;
            if (waterLevel > link.MaxWater) return false;
            return true;
        }

        /// <summary>A filter for <see cref="FacilityGraph"/> queries at a given water level.</summary>
        public static FacilityGraph.LinkFilter Filter(TraversalMask capability, float waterLevel)
            => (link, from, to) => IsPermitted(link, capability, waterLevel);
    }
}
