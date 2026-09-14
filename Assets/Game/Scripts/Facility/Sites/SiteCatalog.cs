using System;
using System.Collections.Generic;
using UnityEngine;
using Grotto.Core;

namespace Grotto.Facility
{
    /// <summary>
    /// Every site the game ships, and the one place that knows how to load one.
    ///
    /// A site is authored as a static <c>Populate(FacilityLayout)</c> method rather
    /// than as a checked-in .asset, for three reasons that matter in practice:
    ///
    ///   * it is diffable, so a map change shows up in review as a list of moved
    ///     nodes rather than as a wall of changed YAML guids;
    ///   * the edit-mode tests can build any site with no scene and no Resources
    ///     folder, which is what lets the navigation tests run on all three;
    ///   * a designer who *does* want an inspector can still bake one out with
    ///     <c>Tools &gt; Grotto &gt; Rebuild Settings Assets</c>, and if the baked
    ///     asset exists in Resources it wins.
    ///
    /// The last point is the important one: code is the source of truth, an asset is
    /// an override. That ordering means a fresh clone works with no setup, and a
    /// tweaked asset is never silently ignored.
    /// </summary>
    public static class SiteCatalog
    {
        /// <summary>Fills a blank layout with one site's contents.</summary>
        public delegate void Populator(FacilityLayout layout);

        public sealed class Entry
        {
            public readonly string Id;
            public readonly string ResourceName;
            public readonly Populator Populate;

            public Entry(string id, string resourceName, Populator populate)
            {
                Id = id;
                ResourceName = resourceName;
                Populate = populate;
            }
        }

        /// <summary>
        /// Shipping order, which is also unlock order and the order of the picker.
        /// </summary>
        private static readonly Entry[] Entries =
        {
            new Entry("grotto", "FacilityLayout_GrottoSprings", GrottoSpringsLayout.Populate),
            new Entry(HollowmereLayout.SiteId, "FacilityLayout_Hollowmere", HollowmereLayout.Populate),
            new Entry(SablefieldLayout.SiteId, "FacilityLayout_Sablefield", SablefieldLayout.Populate)
        };

        /// <summary>The site a fresh profile starts on.</summary>
        public const string DefaultSiteId = "grotto";

        private static readonly Dictionary<string, FacilityLayout> Cache =
            new Dictionary<string, FacilityLayout>(StringComparer.OrdinalIgnoreCase);

        public static int Count => Entries.Length;

        public static IEnumerable<string> Ids
        {
            get
            {
                for (int i = 0; i < Entries.Length; i++) yield return Entries[i].Id;
            }
        }

        public static Entry EntryAt(int index) => Entries[Mathf.Clamp(index, 0, Entries.Length - 1)];

        public static Entry Find(string siteId)
        {
            if (string.IsNullOrWhiteSpace(siteId)) return null;
            for (int i = 0; i < Entries.Length; i++)
                if (string.Equals(Entries[i].Id, siteId, StringComparison.OrdinalIgnoreCase))
                    return Entries[i];
            return null;
        }

        public static int IndexOf(string siteId)
        {
            for (int i = 0; i < Entries.Length; i++)
                if (string.Equals(Entries[i].Id, siteId, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        /// <summary>
        /// The layout for a site, built once and cached.
        ///
        /// An unknown id falls back to the default site with a warning rather than
        /// throwing: a profile written by a newer build, or one hand-edited to a typo,
        /// should drop the player into the grotto, not into a null reference.
        /// </summary>
        public static FacilityLayout Load(string siteId)
        {
            var entry = Find(siteId);
            if (entry == null)
            {
                if (!string.IsNullOrWhiteSpace(siteId))
                {
                    GLog.Warn(LogChannel.Facility,
                        $"Unknown site '{siteId}'; falling back to {DefaultSiteId}.");
                }
                entry = Find(DefaultSiteId);
            }

            if (Cache.TryGetValue(entry.Id, out var cached) && cached != null) return cached;

            var layout = Resources.Load<FacilityLayout>(entry.ResourceName);
            if (layout == null)
            {
                layout = ScriptableObject.CreateInstance<FacilityLayout>();
                layout.name = entry.ResourceName;
                entry.Populate(layout);
            }

            Cache[entry.Id] = layout;
            return layout;
        }

        /// <summary>Builds a fresh, uncached copy. For the editor baker and the tests.</summary>
        public static FacilityLayout Build(string siteId)
        {
            var entry = Find(siteId) ?? Find(DefaultSiteId);
            var layout = ScriptableObject.CreateInstance<FacilityLayout>();
            layout.name = entry.ResourceName;
            entry.Populate(layout);
            return layout;
        }

        /// <summary>Every site, in shipping order. Loads all three, so use it for menus, not for a tick.</summary>
        public static List<FacilityLayout> LoadAll()
        {
            var all = new List<FacilityLayout>(Entries.Length);
            for (int i = 0; i < Entries.Length; i++) all.Add(Load(Entries[i].Id));
            return all;
        }

        /// <summary>
        /// The site the profile last chose, clamped to something the player has
        /// actually unlocked. A profile that names a locked site — reset progress,
        /// edited file, shared save — quietly reverts rather than granting access.
        /// </summary>
        public static string SelectedSiteId(SaveData save)
        {
            if (save == null) return DefaultSiteId;

            string wanted = string.IsNullOrWhiteSpace(save.selectedSiteId)
                ? DefaultSiteId
                : save.selectedSiteId;

            var entry = Find(wanted);
            if (entry == null) return DefaultSiteId;

            return IsUnlocked(Load(entry.Id), save) ? entry.Id : DefaultSiteId;
        }

        /// <summary>
        /// Sites unlock on nights *cleared*, counted across every site. Finishing
        /// night three in the grotto opens Hollowmere whether or not you then went
        /// back and replayed night one.
        /// </summary>
        public static int NightsCleared(SaveData save)
        {
            if (save == null) return 0;

            int cleared = 0;
            for (int i = 0; i < save.nightRecords.Count; i++)
                if (save.nightRecords[i].completed) cleared++;
            return cleared;
        }

        public static bool IsUnlocked(FacilityLayout layout, SaveData save)
        {
            if (layout == null) return false;
            if (layout.unlockAfterNights <= 0) return true;
            return NightsCleared(save) >= layout.unlockAfterNights;
        }

        /// <summary>How many more nights this site needs. Zero when it is already open.</summary>
        public static int NightsRemaining(FacilityLayout layout, SaveData save)
        {
            if (layout == null) return 0;
            return Mathf.Max(0, layout.unlockAfterNights - NightsCleared(save));
        }

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => Cache.Clear();
#endif
    }
}
