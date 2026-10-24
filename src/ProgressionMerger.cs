// Copyright (c) 2026 Skylar Reed. Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace SharedSweeps
{
    /// <summary>
    /// Gives every crewmate the best of what anyone in the crew owns.
    /// </summary>
    /// <remarks>
    /// For each category being shared, each item ends up at the highest level any player has.
    /// Max-merging is idempotent and order-independent, so it's safe to run repeatedly: once
    /// everyone matches, a sweep changes nothing. Lower levels (from a refund, for example) are
    /// never pushed onto other players.
    /// </remarks>
    internal sealed class ProgressionMerger
    {
        /// <summary>Initializes a new instance of the <see cref="ProgressionMerger"/> class that shares everything.</summary>
        public ProgressionMerger()
        {
            ShareTools = true;
            ShareContainers = true;
            ShareTree = true;
            IncludeAbsentCrewmates = true;
        }

        /// <summary>Gets or sets a value indicating whether owned tools (brushes, brooms, vacuums...) are shared.</summary>
        public bool ShareTools { get; set; }

        /// <summary>Gets or sets a value indicating whether each tool's dust-container tier is shared.</summary>
        public bool ShareContainers { get; set; }

        /// <summary>Gets or sets a value indicating whether upgrade-tree levels are shared.</summary>
        public bool ShareTree { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether crewmates who have played on this Moon before,
        /// but aren't online, are included.
        /// </summary>
        public bool IncludeAbsentCrewmates { get; set; }

        /// <summary>Runs one sharing pass over every player.</summary>
        /// <param name="world">The game's <c>MoonWorld</c> instance.</param>
        /// <returns>The number of players whose upgrades changed.</returns>
        public int Sweep(object world)
        {
            List<string> players = GameApi.GetPresentPlayers();
            if (IncludeAbsentCrewmates)
            {
                foreach (string playerId in GameApi.GetSavedPlayers(world))
                {
                    if (!players.Contains(playerId))
                    {
                        players.Add(playerId);
                    }
                }
            }

            if (players.Count < 2)
            {
                return 0; // Solo play with no crew history: nothing to share.
            }

            var original = new Dictionary<string, PlayerProgress>();
            var best = new PlayerProgress();
            foreach (string playerId in players)
            {
                PlayerProgress progress = GameApi.ReadProgress(world, playerId);
                original[playerId] = progress;
                best.InventoryTier = Math.Max(best.InventoryTier, progress.InventoryTier);
                RaiseTo(best.Tools, progress.Tools);
                RaiseTo(best.Containers, progress.Containers);
                RaiseTo(best.Tree, progress.Tree);
            }

            var changedPlayers = new List<string>();
            foreach (string playerId in players)
            {
                PlayerProgress current = original[playerId];
                PlayerProgress updated = current.Clone();
                int toolsAdded = 0;
                int containersRaised = 0;
                int treeLevelsRaised = 0;

                if (ShareTools)
                {
                    toolsAdded = RaiseTo(updated.Tools, best.Tools);
                    updated.InventoryTier = Math.Max(updated.InventoryTier, best.InventoryTier);
                }

                if (ShareContainers)
                {
                    containersRaised = RaiseTo(updated.Containers, best.Containers);
                }

                if (ShareTree)
                {
                    treeLevelsRaised = RaiseTo(updated.Tree, best.Tree);
                }

                if (updated.HasSameValues(current))
                {
                    continue;
                }

                GameApi.WriteProgress(world, playerId, updated);
                changedPlayers.Add(playerId);
                Plugin.Log.LogInfo(string.Format(
                    "Shared with {0}: {1} tool(s), {2} container upgrade(s), {3} tree upgrade(s).",
                    GameApi.DescribePlayer(playerId), toolsAdded, containersRaised, treeLevelsRaised));
            }

            if (changedPlayers.Count > 0)
            {
                GameApi.NotifyProgressChanged(world, changedPlayers);
            }

            return changedPlayers.Count;
        }

        /// <summary>Raises each entry in <paramref name="target"/> to at least its value in <paramref name="source"/>.</summary>
        /// <param name="target">The levels to raise. Missing keys are added.</param>
        /// <param name="source">The levels to raise them to.</param>
        /// <returns>The number of entries that were added or raised.</returns>
        private static int RaiseTo(SortedDictionary<string, int> target, SortedDictionary<string, int> source)
        {
            int raised = 0;
            foreach (KeyValuePair<string, int> entry in source)
            {
                int currentLevel;
                if (target.TryGetValue(entry.Key, out currentLevel) && currentLevel >= entry.Value)
                {
                    continue;
                }

                target[entry.Key] = entry.Value;
                raised++;
            }

            return raised;
        }
    }
}
