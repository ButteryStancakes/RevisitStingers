using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using HarmonyLib;
using System.Collections;
using UnityEngine;                                           

namespace RevisitStingers
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        const string PLUGIN_GUID = "butterystancakes.lethalcompany.revisitstingers", PLUGIN_NAME = "Revisit Stingers", PLUGIN_VERSION = "1.1.0";
        internal static ConfigEntry<bool> configInterruptMusic;
        internal static new ManualLogSource Logger;

        void Awake()
        {
            Logger = base.Logger;

            configInterruptMusic = Config.Bind(
                "Misc",
                "InterruptMusic",
                false,
                "If an ambient music track is playing outdoors, the stinger has a (somewhat low) random chance to play as a musical transition upon entering the building.");

            new Harmony(PLUGIN_GUID).PatchAll();

            Logger.LogInfo($"{PLUGIN_NAME} v{PLUGIN_VERSION} loaded");
        }
    }

    [HarmonyPatch]
    class RevisitStingersPatches
    {
        [HarmonyPatch(typeof(EntranceTeleport), "TeleportPlayer")]
        [HarmonyPrefix]
        static void EntranceTeleportPreTeleportPlayer(EntranceTeleport __instance, ref bool ___checkedForFirstTime)
        {
            if (ReplayStinger.beenInsideThisRound)
                return;

            ReplayStinger.beenInsideThisRound = true;

            if (!___checkedForFirstTime && __instance.firstTimeAudio != null && ES3.Load($"PlayedDungeonEntrance{__instance.dungeonFlowId}", "LCGeneralSaveData", false) && (!StartOfRound.Instance.isChallengeFile || !ES3.Load("FinishedChallenge", "LCChallengeFile", false)))
            {
                try
                {
                    if (ReplayStinger.StingerShouldReplay())
                        __instance.StartCoroutine(ReplayStinger.DelayedStinger(__instance.firstTimeAudio));
                }
                catch (System.Exception e)
                {
                    Plugin.Logger.LogError("Ran into an error attempting to replay stinger - this is likely due to an incompatibility with modded content");
                    Plugin.Logger.LogError(e.Message);
                }
            }
        }

        [HarmonyPatch(typeof(StartOfRound), "SetShipReadyToLand")]
        [HarmonyPostfix]
        static void StartOfRoundPostSetShipReadyToLand()
        {
            ReplayStinger.beenInsideThisRound = false;
        }

        [HarmonyPatch(typeof(StartOfRound), "Awake")]
        [HarmonyPostfix]
        static void StartOfRoundPostAwake()
        {
            ReplayStinger.beenInsideThisRound = false;
        }
    }

    internal class ReplayStinger
    {
        internal static bool beenInsideThisRound;

        const float INTERRUPT_CHANCE = 0.25f;
        // 6.25% is basically just a magic number /shrug
        // adamance has a 5.36% chance to interior swap, and stingers feel quite fitting there.
        // next smallest chance is titan at 18.69%, which is definitely too frequent
        const float MAX_RARITY = 0.0625f;

        internal static bool StingerShouldReplay()
        {
            if (Plugin.configInterruptMusic.Value && SoundManager.Instance.musicSource.isPlaying && SoundManager.Instance.musicSource.time > 5f && Random.value <= INTERRUPT_CHANCE)
                return true;

            if (StartOfRound.Instance.currentLevel.dungeonFlowTypes?.Length < 2)
                return false;

            // get the rarity of the current level
            int currentWeight = 0;
            float totalWeights = 0f; 
            foreach (IntWithRarity interior in StartOfRound.Instance.currentLevel.dungeonFlowTypes)
            {
                // weight of the current interior
                if (RoundManager.Instance.dungeonFlowTypes[interior.id].dungeonFlow == RoundManager.Instance.dungeonGenerator.Generator.DungeonFlow)
                    currentWeight = interior.rarity;
                // add up all the weights to get percentage
                totalWeights += interior.rarity;
            }

            if (currentWeight > 0 && (currentWeight / totalWeights) <= MAX_RARITY)
                return true;

            return false;
        }

        internal static IEnumerator DelayedStinger(AudioClip stinger)
        {
            yield return new WaitForSeconds(0.6f);
            HUDManager.Instance.UIAudio.PlayOneShot(stinger);
        }
    }
}