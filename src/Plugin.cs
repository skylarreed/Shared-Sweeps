// Copyright (c) 2026 Skylar Reed. Licensed under the MIT License.

using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SharedSweeps
{
    /// <summary>
    /// Shared Sweeps: when anyone in a Moon Sweepers crew buys a tool, container upgrade or
    /// upgrade-tree level, every crewmate gets it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Moon Sweepers' co-op is host-authoritative. The host's <c>MoonWorld</c> stores every crewmate's
    /// upgrades and regularly sends that state to crewmates, replacing their local copy. The crew's
    /// money is already one shared wallet, but each player normally has to buy every item separately.
    /// </para>
    /// <para>
    /// This plugin runs on the host. After every purchase, and on a short timer that also catches
    /// crewmates joining, it gives every player the best of what the crew owns
    /// (<see cref="ProgressionMerger"/>). The game's own sync then delivers the result, so
    /// crewmates don't need the mod.
    /// </para>
    /// </remarks>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        /// <summary>The BepInEx plugin id. Also names the config file, so it must never change.</summary>
        public const string PluginGuid = "com.skylarreed.moonsweepers.sharedprogression";

        /// <summary>The display name used in the BepInEx log.</summary>
        public const string PluginName = "Shared Sweeps";

        /// <summary>The plugin version.</summary>
        public const string PluginVersion = "1.0.0";

        private const float MinimumSweepSeconds = 0.5f;
        private const float RetryAfterErrorSeconds = 30f;

        private static ConfigEntry<bool> s_enabled;
        private static ConfigEntry<float> s_sweepSeconds;
        private static ProgressionMerger s_merger;
        private static bool s_sweepRequested = true;
        private static float s_nextSweepTime;
        private static bool s_hasAnnouncedSharing;

        /// <summary>Gets the plugin's log, for the other classes in the mod.</summary>
        internal static ManualLogSource Log { get; private set; }

        private void Awake()
        {
            Log = Logger;
            BindConfig();

            if (!GameApi.Bind())
            {
                Log.LogError("Disabled. " + GameApi.BindError + " The mod needs updating for this version of the game.");
                return;
            }

            var harmony = new Harmony(PluginGuid);
            harmony.Patch(GameApi.WorldUpdateMethod, postfix: CreateHook("AfterWorldUpdate"));
            harmony.Patch(GameApi.CommitUpgradeMethod, postfix: CreateHook("AfterCommitUpgrade"));
            Log.LogInfo(PluginName + " " + PluginVersion + " loaded. Host the crew to share tools and upgrades with everyone.");
        }

        private void BindConfig()
        {
            s_enabled = Config.Bind(
                "General", "Enabled", true,
                "Share progression between crewmates. Only the host's setting matters.");
            s_sweepSeconds = Config.Bind(
                "General", "SweepSeconds", 3f,
                "How often the host re-checks for crewmates who are missing shared unlocks (e.g. someone just joined). Purchases are shared immediately regardless.");
            ConfigEntry<bool> shareTools = Config.Bind(
                "Share", "Tools", true,
                "Share owned cleaning tools (toothbrush, brushes, brooms, vacuums...).");
            ConfigEntry<bool> shareContainers = Config.Bind(
                "Share", "ContainerUpgrades", true,
                "Share each tool's dust-container tier.");
            ConfigEntry<bool> shareTree = Config.Bind(
                "Share", "UpgradeTree", true,
                "Share upgrade-tree levels bought at the workshop terminal.");
            ConfigEntry<bool> includeAbsent = Config.Bind(
                "Share", "IncludeAbsentCrewmates", true,
                "Also share with (and from) crewmates who have played on this Moon before but are not online right now.");

            s_merger = new ProgressionMerger();
            s_merger.ShareTools = shareTools.Value;
            s_merger.ShareContainers = shareContainers.Value;
            s_merger.ShareTree = shareTree.Value;
            s_merger.IncludeAbsentCrewmates = includeAbsent.Value;

            // Settings edited while the game runs apply on the next pass.
            shareTools.SettingChanged += delegate { s_merger.ShareTools = shareTools.Value; s_sweepRequested = true; };
            shareContainers.SettingChanged += delegate { s_merger.ShareContainers = shareContainers.Value; s_sweepRequested = true; };
            shareTree.SettingChanged += delegate { s_merger.ShareTree = shareTree.Value; s_sweepRequested = true; };
            includeAbsent.SettingChanged += delegate { s_merger.IncludeAbsentCrewmates = includeAbsent.Value; s_sweepRequested = true; };
        }

        private static HarmonyMethod CreateHook(string methodName)
        {
            return new HarmonyMethod(typeof(Plugin).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic));
        }

        /// <summary>Harmony postfix on <c>MoonWorld.CommitUpgrade</c>: a purchase was made, so share it right away.</summary>
        private static void AfterCommitUpgrade()
        {
            s_sweepRequested = true;
        }

        /// <summary>Harmony postfix on <c>MoonWorld.Update</c>: runs a sharing pass when one is due.</summary>
        /// <param name="__instance">
        /// The game's <c>MoonWorld</c>. Harmony requires this exact parameter name.
        /// </param>
        private static void AfterWorldUpdate(Component __instance)
        {
            if (!s_enabled.Value)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (!s_sweepRequested && now < s_nextSweepTime)
            {
                return;
            }

            s_sweepRequested = false;
            s_nextSweepTime = now + Mathf.Max(MinimumSweepSeconds, s_sweepSeconds.Value);

            try
            {
                if (!GameApi.IsAuthority())
                {
                    return; // On a crewmate's machine the host decides.
                }

                int changedPlayers = s_merger.Sweep(__instance);
                if (changedPlayers > 0 && !s_hasAnnouncedSharing)
                {
                    s_hasAnnouncedSharing = true;
                    Log.LogInfo("Crew progression is now shared. Crewmates receive it through the game's own sync.");
                }
            }
            catch (Exception ex)
            {
                // Never let a failure here break the game's world tick; back off and try again later.
                Exception cause = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                Log.LogError("Sharing pass failed, retrying in " + RetryAfterErrorSeconds + " s: " + cause);
                s_nextSweepTime = now + RetryAfterErrorSeconds;
            }
        }
    }
}
