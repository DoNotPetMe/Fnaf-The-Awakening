using System;
using UnityEngine;

namespace Grotto.Facility
{
    /// <summary>
    /// The water levels at which routes open and close.
    ///
    /// Per-site rather than global constants, because the thresholds *are* the
    /// difficulty of a site's water axis. A show cave sits balanced around the
    /// middle; a hydroelectric station starts high and has almost no dry band at
    /// all; a grain terminal is dry enough that the swimming route barely exists.
    /// Same systems, different shape.
    /// </summary>
    [Serializable]
    public struct WaterGates
    {
        [Tooltip("At or below this, a burrower can tunnel through the sump floor.")]
        [Range(0f, 1f)] public float diggable;

        [Tooltip("At or below this, a burrower can still enter and leave the basin.")]
        [Range(0f, 1f)] public float wadeable;

        [Tooltip("At or above this, the deep watercourse can be swum.")]
        [Range(0f, 1f)] public float swimmable;

        [Tooltip("Above this, walking routes over the water are lost.")]
        [Range(0f, 1f)] public float drowned;

        public static WaterGates Default => new WaterGates
        {
            diggable = 0.35f,
            wadeable = 0.50f,
            swimmable = 0.55f,
            drowned = 0.75f
        };

        /// <summary>
        /// The band where neither route exists. A site with no gap is relentless; a
        /// site with a wide one is forgiving. Negative means the two overlap, which
        /// the validator reports as a design error.
        /// </summary>
        public float SafeBand => swimmable - wadeable;

        public WaterGates Sanitised()
        {
            var gates = this;
            gates.diggable = Mathf.Clamp01(gates.diggable);
            gates.wadeable = Mathf.Clamp(gates.wadeable, gates.diggable, 1f);
            gates.swimmable = Mathf.Clamp01(gates.swimmable);
            gates.drowned = Mathf.Clamp(gates.drowned, gates.swimmable, 1f);
            return gates;
        }

        public override string ToString()
            => $"dig<={diggable:0.00} wade<={wadeable:0.00} swim>={swimmable:0.00} drown>{drowned:0.00}";
    }

    /// <summary>
    /// Which node in a layout plays which structural role.
    ///
    /// Everything that used to hard-code "ADIT_N" or "SUMP" reads this instead, which
    /// is the single change that makes a second site possible at all. A layout that
    /// leaves a role blank simply does not have that feature.
    /// </summary>
    [Serializable]
    public struct SiteWiring
    {
        [Tooltip("Node behind the north blast door.")]
        public string northApproach;

        [Tooltip("Node behind the south blast door.")]
        public string southApproach;

        [Tooltip("Node below the station, behind the grate.")]
        public string sump;

        [Tooltip("Node above the station. Has no door; light is the only answer.")]
        public string chase;

        [Tooltip("Where the generator lives, for machinery audio.")]
        public string generatorBay;

        [Tooltip("The deep, camera-less node the geophones exist to cover.")]
        public string deepGallery;
    }

    /// <summary>
    /// Which family of surfaces a site is built from. The geometry builder picks its
    /// materials from this, so a hydroelectric station reads as poured concrete and
    /// painted steel rather than as limestone with the labels changed.
    /// </summary>
    public enum SitePalette
    {
        /// <summary>Show cave: warm limestone, speleothems, damp staining.</summary>
        Limestone,
        /// <summary>Hydroelectric: board-marked concrete, wet steel, tile.</summary>
        Concrete,
        /// <summary>Grain terminal: galvanised steel, timber, dust.</summary>
        Steel
    }
}
