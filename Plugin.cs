using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using HarmonyLib;
using System.Collections;
using System.Linq;
using UnityEngine;                                           

namespace RevisitStingers
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        const string PLUGIN_GUID = "butterystancakes.lethalcompany.revisitstingers", PLUGIN_NAME = "Revisit Stingers", PLUGIN_VERSION = "1.0.2";
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

        internal static bool StingerShouldReplay()
        {
            if (Plugin.configInterruptMusic.Value && SoundManager.Instance.musicSource.isPlaying && SoundManager.Instance.musicSource.time > 5f && Random.value <= 0.25f)
                return true;

            IntWithRarity interior = StartOfRound.Instance.currentLevel.dungeonFlowTypes.FirstOrDefault(intWithRarity => RoundManager.Instance.dungeonFlowTypes[intWithRarity.id].dungeonFlow == RoundManager.Instance.dungeonGenerator.Generator.DungeonFlow);

            if (interior != null && interior.rarity < 20)
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