// Copyright (c) 2026 Skylar Reed. Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace SharedSweeps
{
    /// <summary>
    /// A detached copy of one player's upgrade state (the game's <c>MoonWorld.UpgradeState</c>).
    /// </summary>
    /// <remarks>
    /// The mod reads the game's state into this type, merges copies, and writes the result back.
    /// It never holds a reference to the game's own objects, so editing a copy can't affect the game
    /// until <see cref="GameApi.WriteProgress"/> is called.
    /// </remarks>
    internal sealed class PlayerProgress
    {
        /// <summary>Initializes a new, empty instance of the <see cref="PlayerProgress"/> class.</summary>
        public PlayerProgress()
        {
            Tools = new SortedDictionary<string, int>(StringComparer.Ordinal);
            Containers = new SortedDictionary<string, int>(StringComparer.Ordinal);
            Tree = new SortedDictionary<string, int>(StringComparer.Ordinal);
        }

        /// <summary>Gets or sets the player's inventory (quick-slot) tier.</summary>
        public int InventoryTier { get; set; }

        /// <summary>Gets the owned tools, keyed by tool id. The value is the tool's level (0 = owned).</summary>
        public SortedDictionary<string, int> Tools { get; private set; }

        /// <summary>Gets the dust-container tier of each tool, keyed by tool id.</summary>
        public SortedDictionary<string, int> Containers { get; private set; }

        /// <summary>Gets the upgrade-tree levels, keyed by tree node id.</summary>
        public SortedDictionary<string, int> Tree { get; private set; }

        /// <summary>Creates a deep copy of this instance.</summary>
        /// <returns>A new <see cref="PlayerProgress"/> with the same values.</returns>
        public PlayerProgress Clone()
        {
            var copy = new PlayerProgress();
            copy.InventoryTier = InventoryTier;
            CopyEntries(Tools, copy.Tools);
            CopyEntries(Containers, copy.Containers);
            CopyEntries(Tree, copy.Tree);
            return copy;
        }

        /// <summary>Determines whether this instance holds exactly the same values as another.</summary>
        /// <param name="other">The instance to compare with.</param>
        /// <returns><see langword="true"/> if every value matches; otherwise, <see langword="false"/>.</returns>
        public bool HasSameValues(PlayerProgress other)
        {
            return InventoryTier == other.InventoryTier
                && HaveSameEntries(Tools, other.Tools)
                && HaveSameEntries(Containers, other.Containers)
                && HaveSameEntries(Tree, other.Tree);
        }

        /// <summary>Replaces the contents of <paramref name="destination"/> with those of <paramref name="source"/>.</summary>
        /// <param name="source">The entries to copy. A <see langword="null"/> source leaves the destination empty.</param>
        /// <param name="destination">The dictionary to overwrite.</param>
        internal static void CopyEntries(IDictionary<string, int> source, IDictionary<string, int> destination)
        {
            destination.Clear();
            if (source == null)
            {
                return;
            }

            foreach (KeyValuePair<string, int> entry in source)
            {
                destination[entry.Key] = entry.Value;
            }
        }

        private static bool HaveSameEntries(SortedDictionary<string, int> first, SortedDictionary<string, int> second)
        {
            if (first.Count != second.Count)
            {
                return false;
            }

            foreach (KeyValuePair<string, int> entry in first)
            {
                int otherValue;
                if (!second.TryGetValue(entry.Key, out otherValue) || otherValue != entry.Value)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
