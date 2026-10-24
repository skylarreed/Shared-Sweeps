// Copyright (c) 2026 Skylar Reed. Licensed under the MIT License.
//
// Written in C# 5 syntax on purpose, so build.bat can fall back to the compiler that ships with Windows.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SharedSweeps
{
    /// <summary>
    /// Reflection bindings to the progression and crew code in Moon Sweepers
    /// (<c>Assembly-CSharp.dll</c>, namespace <c>MoonCleaners</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything the mod needs is private or internal in the game, so each member is looked up by
    /// name once, in <see cref="Bind"/>. If a game update renames something, <see cref="Bind"/>
    /// returns <see langword="false"/> and <see cref="BindError"/> names the missing member, and
    /// the mod disables itself instead of crashing the game.
    /// </para>
    /// <para>
    /// Player ids follow the game's convention: <c>"local"</c> is the host (or the solo player),
    /// and crewmates are <c>"steam-&lt;SteamID&gt;"</c>.
    /// </para>
    /// </remarks>
    internal static class GameApi
    {
        private const BindingFlags AnyVisibility = BindingFlags.Public | BindingFlags.NonPublic;

        // MoonWorld/UpgradeState and its fields.
        private static Type s_upgradeStateType;
        private static FieldInfo s_stateInventoryTier;
        private static FieldInfo s_stateTools;
        private static FieldInfo s_stateContainers;
        private static FieldInfo s_stateTree;

        // MoonWorld members.
        private static MethodInfo s_playerUpgradeState;     // private UpgradeState PlayerUpgradeState(string player)
        private static MethodInfo s_setPlayerUpgradeState;  // private void SetPlayerUpgradeState(string player, UpgradeState state)
        private static MethodInfo s_upgradePlayers;         // private IEnumerable<string> UpgradePlayers()
        private static MethodInfo s_saveProgress;           // private Task SaveProgress(bool showIndicator)
        private static MethodInfo s_getTreeVersion;         // public int TreeVersion { get; private set; }
        private static MethodInfo s_setTreeVersion;
        private static FieldInfo s_upgradesChanged;         // event Action<string> UpgradesChanged (backing field)
        private static MethodInfo s_playerName;             // public static string PlayerName(string player); optional

        // MoonCrew members.
        private static MethodInfo s_crewJoined;             // static bool Joined: true on a crewmate's machine
        private static MethodInfo s_crewLocalPlayerId;      // static string LocalPlayerId: "local" on the host
        private static MethodInfo s_crewPlayerId;           // static string PlayerId(ulong steamId)

        // MoonSteamLobby members.
        private static MethodInfo s_lobbyInLobby;           // static bool InLobby
        private static MethodInfo s_lobbyMembers;           // static IReadOnlyList<Member> Members
        private static FieldInfo s_memberSteamId;           // Member.id (ulong)
        private static FieldInfo s_memberIsLocal;           // Member.local (bool)

        /// <summary>Gets the game's <c>MoonWorld</c> type.</summary>
        public static Type WorldType { get; private set; }

        /// <summary>Gets <c>MoonWorld.Update()</c>, the game's per-frame world tick.</summary>
        public static MethodInfo WorldUpdateMethod { get; private set; }

        /// <summary>Gets <c>MoonWorld.CommitUpgrade(string)</c>, which every tool, container and tree purchase ends in.</summary>
        public static MethodInfo CommitUpgradeMethod { get; private set; }

        /// <summary>Gets a description of the member that couldn't be found, if <see cref="Bind"/> failed.</summary>
        public static string BindError { get; private set; }

        /// <summary>Looks up every game member the mod uses.</summary>
        /// <returns>
        /// <see langword="true"/> if everything was found; otherwise, <see langword="false"/>,
        /// with the reason in <see cref="BindError"/>.
        /// </returns>
        public static bool Bind()
        {
            try
            {
                WorldType = Require(AccessTools.TypeByName("MoonCleaners.MoonWorld"), "type MoonCleaners.MoonWorld");
                s_upgradeStateType = Require(WorldType.GetNestedType("UpgradeState", AnyVisibility), "type MoonWorld/UpgradeState");
                Type crewType = Require(AccessTools.TypeByName("MoonCleaners.MoonCrew"), "type MoonCleaners.MoonCrew");
                Type lobbyType = Require(AccessTools.TypeByName("MoonCleaners.MoonSteamLobby"), "type MoonCleaners.MoonSteamLobby");
                Type memberType = Require(lobbyType.GetNestedType("Member", AnyVisibility), "type MoonSteamLobby/Member");

                WorldUpdateMethod = Require(AccessTools.Method(WorldType, "Update", Type.EmptyTypes), "MoonWorld.Update()");
                CommitUpgradeMethod = Require(AccessTools.Method(WorldType, "CommitUpgrade", new[] { typeof(string) }), "MoonWorld.CommitUpgrade(string)");
                s_playerUpgradeState = Require(AccessTools.Method(WorldType, "PlayerUpgradeState", new[] { typeof(string) }), "MoonWorld.PlayerUpgradeState(string)");
                s_setPlayerUpgradeState = Require(AccessTools.Method(WorldType, "SetPlayerUpgradeState", new[] { typeof(string), s_upgradeStateType }), "MoonWorld.SetPlayerUpgradeState(string, UpgradeState)");
                s_upgradePlayers = Require(AccessTools.Method(WorldType, "UpgradePlayers", Type.EmptyTypes), "MoonWorld.UpgradePlayers()");
                s_saveProgress = Require(AccessTools.Method(WorldType, "SaveProgress", new[] { typeof(bool) }), "MoonWorld.SaveProgress(bool)");
                s_getTreeVersion = Require(AccessTools.PropertyGetter(WorldType, "TreeVersion"), "MoonWorld.TreeVersion getter");
                s_setTreeVersion = Require(AccessTools.PropertySetter(WorldType, "TreeVersion"), "MoonWorld.TreeVersion setter");
                s_upgradesChanged = Require(AccessTools.Field(WorldType, "UpgradesChanged"), "MoonWorld.UpgradesChanged");
                s_playerName = AccessTools.Method(WorldType, "PlayerName", new[] { typeof(string) }); // Optional: only used in log messages.

                s_stateInventoryTier = Require(AccessTools.Field(s_upgradeStateType, "inventoryTier"), "UpgradeState.inventoryTier");
                s_stateTools = Require(AccessTools.Field(s_upgradeStateType, "tools"), "UpgradeState.tools");
                s_stateContainers = Require(AccessTools.Field(s_upgradeStateType, "containers"), "UpgradeState.containers");
                s_stateTree = Require(AccessTools.Field(s_upgradeStateType, "tree"), "UpgradeState.tree");

                s_crewJoined = Require(AccessTools.PropertyGetter(crewType, "Joined"), "MoonCrew.Joined");
                s_crewLocalPlayerId = Require(AccessTools.PropertyGetter(crewType, "LocalPlayerId"), "MoonCrew.LocalPlayerId");
                s_crewPlayerId = Require(AccessTools.Method(crewType, "PlayerId", new[] { typeof(ulong) }), "MoonCrew.PlayerId(ulong)");

                s_lobbyInLobby = Require(AccessTools.PropertyGetter(lobbyType, "InLobby"), "MoonSteamLobby.InLobby");
                s_lobbyMembers = Require(AccessTools.PropertyGetter(lobbyType, "Members"), "MoonSteamLobby.Members");
                s_memberSteamId = Require(AccessTools.Field(memberType, "id"), "MoonSteamLobby/Member.id");
                s_memberIsLocal = Require(AccessTools.Field(memberType, "local"), "MoonSteamLobby/Member.local");
                return true;
            }
            catch (Exception ex)
            {
                // Any lookup failure (usually MissingMemberException from Require) disables the mod
                // cleanly instead of letting the exception stop BepInEx from loading it.
                BindError = ex.Message;
                return false;
            }
        }

        /// <summary>Determines whether this machine decides everyone's upgrades.</summary>
        /// <returns>
        /// <see langword="true"/> on the host and in solo play; <see langword="false"/> on a
        /// crewmate's machine, which follows the host.
        /// </returns>
        public static bool IsAuthority()
        {
            return !(bool)s_crewJoined.Invoke(null, null);
        }

        /// <summary>Gets the id of the player on this machine.</summary>
        /// <returns><c>"local"</c> on the host or in solo play; otherwise <c>"steam-&lt;SteamID&gt;"</c>.</returns>
        public static string GetLocalPlayerId()
        {
            return (string)s_crewLocalPlayerId.Invoke(null, null);
        }

        /// <summary>Gets the ids of everyone in the current crew.</summary>
        /// <returns>The local player first, then every other lobby member. Outside a lobby, only the local player.</returns>
        public static List<string> GetPresentPlayers()
        {
            var players = new List<string>();
            string localId = GetLocalPlayerId();
            if (!string.IsNullOrEmpty(localId))
            {
                players.Add(localId);
            }

            if (!(bool)s_lobbyInLobby.Invoke(null, null))
            {
                return players;
            }

            var members = s_lobbyMembers.Invoke(null, null) as IEnumerable;
            if (members == null)
            {
                return players;
            }

            foreach (object member in members)
            {
                if (member == null || (bool)s_memberIsLocal.GetValue(member))
                {
                    continue;
                }

                var steamId = (ulong)s_memberSteamId.GetValue(member);
                if (steamId == 0)
                {
                    continue;
                }

                var playerId = s_crewPlayerId.Invoke(null, new object[] { steamId }) as string;
                if (!string.IsNullOrEmpty(playerId) && !players.Contains(playerId))
                {
                    players.Add(playerId);
                }
            }

            return players;
        }

        /// <summary>Gets every player the world has saved progression for.</summary>
        /// <param name="world">The game's <c>MoonWorld</c> instance.</param>
        /// <returns>The ids of current and past crewmates, including ones who are offline.</returns>
        public static List<string> GetSavedPlayers(object world)
        {
            var players = new List<string>();
            var ids = s_upgradePlayers.Invoke(world, null) as IEnumerable<string>;
            if (ids == null)
            {
                return players;
            }

            foreach (string id in ids)
            {
                if (!string.IsNullOrEmpty(id))
                {
                    players.Add(id);
                }
            }

            return players;
        }

        /// <summary>Reads a copy of one player's upgrades.</summary>
        /// <param name="world">The game's <c>MoonWorld</c> instance.</param>
        /// <param name="playerId">The player to read.</param>
        /// <returns>A detached copy; an empty one if the player has no saved progression.</returns>
        public static PlayerProgress ReadProgress(object world, string playerId)
        {
            object state = s_playerUpgradeState.Invoke(world, new object[] { playerId });
            var progress = new PlayerProgress();
            progress.InventoryTier = (int)s_stateInventoryTier.GetValue(state);
            PlayerProgress.CopyEntries((IDictionary<string, int>)s_stateTools.GetValue(state), progress.Tools);
            PlayerProgress.CopyEntries((IDictionary<string, int>)s_stateContainers.GetValue(state), progress.Containers);
            PlayerProgress.CopyEntries((IDictionary<string, int>)s_stateTree.GetValue(state), progress.Tree);
            return progress;
        }

        /// <summary>Replaces one player's upgrades, creating their entry if they have none yet.</summary>
        /// <param name="world">The game's <c>MoonWorld</c> instance.</param>
        /// <param name="playerId">The player to update.</param>
        /// <param name="progress">The values to store.</param>
        /// <remarks>Call <see cref="NotifyProgressChanged"/> afterwards so the game refreshes and saves.</remarks>
        public static void WriteProgress(object world, string playerId, PlayerProgress progress)
        {
            // The game's own constructor creates the ordinal-sorted dictionaries the game expects.
            object state = Activator.CreateInstance(s_upgradeStateType, true);
            s_stateInventoryTier.SetValue(state, progress.InventoryTier);
            PlayerProgress.CopyEntries(progress.Tools, (IDictionary<string, int>)s_stateTools.GetValue(state));
            PlayerProgress.CopyEntries(progress.Containers, (IDictionary<string, int>)s_stateContainers.GetValue(state));
            PlayerProgress.CopyEntries(progress.Tree, (IDictionary<string, int>)s_stateTree.GetValue(state));
            s_setPlayerUpgradeState.Invoke(world, new object[] { playerId, state });
        }

        /// <summary>
        /// Refreshes the game's UI and saves, the same way the game does after it applies upgrades
        /// received from a host.
        /// </summary>
        /// <param name="world">The game's <c>MoonWorld</c> instance.</param>
        /// <param name="changedPlayers">The players whose upgrades were written.</param>
        public static void NotifyProgressChanged(object world, IEnumerable<string> changedPlayers)
        {
            int treeVersion = (int)s_getTreeVersion.Invoke(world, null);
            s_setTreeVersion.Invoke(world, new object[] { treeVersion + 1 });

            var upgradesChanged = s_upgradesChanged.GetValue(world) as Action<string>;
            if (upgradesChanged != null)
            {
                foreach (string playerId in changedPlayers)
                {
                    try
                    {
                        upgradesChanged(playerId);
                    }
                    catch (Exception ex)
                    {
                        // One broken UI listener in the game shouldn't stop the others or the save.
                        Plugin.Log.LogWarning("A game UpgradesChanged handler threw: " + ex.Message);
                    }
                }
            }

            s_saveProgress.Invoke(world, new object[] { false });
        }

        /// <summary>Gets a readable name for a player, for log messages.</summary>
        /// <param name="playerId">The player's id.</param>
        /// <returns>"Name (id)" when the game knows the player's name; otherwise just the id.</returns>
        public static string DescribePlayer(string playerId)
        {
            if (s_playerName == null)
            {
                return playerId;
            }

            try
            {
                var name = s_playerName.Invoke(null, new object[] { playerId }) as string;
                if (!string.IsNullOrEmpty(name) && name != playerId)
                {
                    return name + " (" + playerId + ")";
                }
            }
            catch (Exception)
            {
                // The name is only cosmetic, and this runs in the middle of a sharing pass,
                // so any failure falls back to the id rather than aborting the pass.
            }

            return playerId;
        }

        private static T Require<T>(T member, string description) where T : class
        {
            if (member == null)
            {
                throw new MissingMemberException("Moon Sweepers has changed: could not find " + description + ".");
            }

            return member;
        }
    }
}
