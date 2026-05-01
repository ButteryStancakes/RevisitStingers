using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections;
using UnityEngine;

namespace RevisitStingers
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    [BepInDependency(GUID_LOBBY_COMPATIBILITY, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(GUID_DAWN_LIB, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        internal const string PLUGIN_GUID = "butterystancakes.lethalcompany.revisitstingers", PLUGIN_NAME = "Revisit Stingers", PLUGIN_VERSION = "1.3.1";
        internal static ConfigEntry<float> configMaxRarity, configInterruptMusic, configFallbackChance;
        internal static new ManualLogSource Logger;

        const string GUID_LOBBY_COMPATIBILITY = "BMX.LobbyCompatibility";
        const string GUID_DAWN_LIB = "com.github.teamxiaolan.dawnlib";

        internal static bool TRUST_CHECKED_FOR_FIRST_TIME = true;

        void Awake()
        {
            Logger = base.Logger;

            if (Chainloader.PluginInfos.ContainsKey(GUID_LOBBY_COMPATIBILITY))
            {
                Logger.LogInfo("CROSS-COMPATIBILITY - Lobby Compatibility detected");
                LobbyCompatibility.Init();
            }

            if (Chainloader.PluginInfos.ContainsKey(GUID_DAWN_LIB))
            {
                Logger.LogInfo("CROSS-COMPATIBILITY - DawnLib detected");
                TRUST_CHECKED_FOR_FIRST_TIME = false;
            }

            AcceptableValueRange<float> percentage = new(0f, 1f);
            string chanceHint = " (0 = never, 1 = guaranteed, or anything in between - 0.5 = 50% chance)";

            configMaxRarity = Config.Bind(
                "Misc",
                "MaxRarity",
                0.25f,
                new ConfigDescription("The highest spawn chance (0.25 = 25%) an interior can have on a specific moon for it to be considered \"rare\". Rare interiors will always play the stinger.", percentage));

            configInterruptMusic = Config.Bind(
                "Misc",
                "InterruptMusic",
                0f,
                new ConfigDescription("The percentage chance for the stinger to play if you enter the building while an ambient music track is playing." + chanceHint, percentage));

            configFallbackChance = Config.Bind(
                "Misc",
                "FallbackChance",
                0f,
                new ConfigDescription("The percentage chance that the stinger will still play, even if you are in a common interior and there is no music playing on the surface." + chanceHint, percentage));

            new Harmony(PLUGIN_GUID).PatchAll();

            Logger.LogInfo($"{PLUGIN_NAME} v{PLUGIN_VERSION} loaded");
        }
    }

    [HarmonyPatch]
    class RevisitStingersPatches
    {
        [HarmonyPatch(typeof(EntranceTeleport), nameof(EntranceTeleport.TeleportPlayer))]
        [HarmonyPrefix]
        static void EntranceTeleport_Pre_TeleportPlayer(EntranceTeleport __instance)
        {
            if (ReplayStinger.beenInsideThisRound || !__instance.FindExitPoint())
                return;

            ReplayStinger.beenInsideThisRound = true;

            if (!__instance.isEntranceToBuilding)
                return;

            // don't interfere with vanilla behavior
            if ((__instance.checkedForFirstTime && Plugin.TRUST_CHECKED_FOR_FIRST_TIME) || !ES3.Load($"PlayedDungeonEntrance{RoundManager.Instance.currentDungeonType}", "LCGeneralSaveData", false))
                return;

            try
            {
                AudioClip stinger = RoundManager.Instance.dungeonFlowTypes[RoundManager.Instance.currentDungeonType].firstTimeAudio;
                if (stinger != null && ReplayStinger.StingerShouldReplay())
                    __instance.StartCoroutine(ReplayStinger.DelayedStinger(stinger));
            }
            catch (System.Exception e)
            {
                Plugin.Logger.LogWarning($"Ran into an error attempting to replay stinger - this is likely due to an incompatibility with modded content\n{e}");
            }
        }

        [HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.SetShipReadyToLand))]
        [HarmonyPostfix]
        static void StartOfRound_Post_SetShipReadyToLand()
        {
            ReplayStinger.beenInsideThisRound = false;
        }

        [HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.Awake))]
        [HarmonyPostfix]
        static void StartOfRound_Post_Awake()
        {
            ReplayStinger.beenInsideThisRound = false;
        }

        [HarmonyPatch(typeof(ShipTeleporter), nameof(ShipTeleporter.TeleportPlayerOutWithInverseTeleporter))]
        [HarmonyPostfix]
        static void ShipTeleporter_Post_TeleportPlayerOutWithInverseTeleporter(int playerObj)
        {
            if (StartOfRound.Instance.allPlayerScripts[playerObj] != GameNetworkManager.Instance.localPlayerController || ReplayStinger.beenInsideThisRound)
                return;

            ReplayStinger.beenInsideThisRound = true;

            try
            {
                AudioClip stinger = RoundManager.Instance.dungeonFlowTypes[RoundManager.Instance.currentDungeonType].firstTimeAudio;
                if (stinger != null && (ReplayStinger.StingerShouldReplay() || !ES3.Load($"PlayedDungeonEntrance{RoundManager.Instance.currentDungeonType}", "LCGeneralSaveData", false)))
                {
                    StartOfRound.Instance.StartCoroutine(ReplayStinger.DelayedStinger(stinger)); // in case inverse is stored
                    if (ReplayStinger.IsVanillaDungeon())
                        ES3.Save($"PlayedDungeonEntrance{RoundManager.Instance.currentDungeonType}", true, "LCGeneralSaveData");
                }
            }
            catch (System.Exception e)
            {
                Plugin.Logger.LogWarning($"Ran into an error attempting to replay stinger - this is likely due to an incompatibility with modded content\n{e}");
            }
        }
    }

    internal class ReplayStinger
    {
        internal static bool beenInsideThisRound;

        internal static bool StingerShouldReplay()
        {
            if (Plugin.configInterruptMusic.Value > 0f && SoundManager.Instance.musicSource.isPlaying && Random.value <= Plugin.configInterruptMusic.Value)
                return true;

            // do rarity calculations, as long as current moon supports multiple interiors
            // challenge moons are seeded, so skip if the player has attempted it before
            if (StartOfRound.Instance.currentLevel.dungeonFlowTypes?.Length > 1 && (!StartOfRound.Instance.isChallengeFile || !ES3.Load("FinishedChallenge", "LCChallengeFile", false)))
            {
                // get the rarity of the current level
                int currentWeight = 0;
                float totalWeights = 0f;
                foreach (IntWithRarity interior in StartOfRound.Instance.currentLevel.dungeonFlowTypes)
                {
                    // weight of the current interior
                    if (interior.id == RoundManager.Instance.currentDungeonType)
                        currentWeight = interior.rarity;
                    // add up all the weights to get percentage
                    totalWeights += interior.rarity;
                }

                if (currentWeight > 0 && (currentWeight / totalWeights) <= Plugin.configMaxRarity.Value)
                    return true;
            }

            return Plugin.configFallbackChance.Value > 0f && Random.value <= Plugin.configFallbackChance.Value;
        }

        internal static bool IsVanillaDungeon()
        {
            return RoundManager.Instance.currentDungeonType == 0 || RoundManager.Instance.currentDungeonType == 1 || RoundManager.Instance.currentDungeonType == 4;
        }

        internal static IEnumerator DelayedStinger(AudioClip stinger)
        {
            yield return new WaitForSeconds(0.6f);
            HUDManager.Instance.UIAudio.PlayOneShot(stinger);
        }
    }
}