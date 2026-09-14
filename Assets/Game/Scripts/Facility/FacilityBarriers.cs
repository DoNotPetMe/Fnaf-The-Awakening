namespace Grotto.Facility
{
    /// <summary>
    /// Barrier ids, which are properties of the *station* rather than of a site.
    ///
    /// Every site has the same control room fittings — two blast doors and a grate —
    /// even though the nodes behind them differ. Keeping the ids here rather than on
    /// one site's layout is what lets the scene builder, the station controller and
    /// the dev console stay site-agnostic.
    /// </summary>
    public static class FacilityBarriers
    {
        public const string DoorNorth = "DOOR_N";
        public const string DoorSouth = "DOOR_S";
        public const string SumpGrate = "GRATE";
    }
}
