using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using Photon.Pun;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace SelfSacrifice
{
    [BepInPlugin("mod.selfsacrifice", "SelfSacrifice", "1.1.5")]
    public class SelfSacrificePlugin : BaseUnityPlugin
    {
        public static SelfSacrificePlugin Instance { get; private set; }
        public static ManualLogSource LogInstance { get; private set; }

        private static readonly AccessTools.FieldRef<PlayerHealthGrab, StaticGrabObject> staticGrabObjectRef = AccessTools.FieldRefAccess<PlayerHealthGrab, StaticGrabObject>("staticGrabObject");
        private static readonly AccessTools.FieldRef<PlayerHealth, int> healthRef = AccessTools.FieldRefAccess<PlayerHealth, int>("health");
        private static readonly AccessTools.FieldRef<PlayerHealth, int> maxHealthRef = AccessTools.FieldRefAccess<PlayerHealth, int>("maxHealth");
        private static readonly AccessTools.FieldRef<PlayerHealthGrab, float> grabbingTimerRef = AccessTools.FieldRefAccess<PlayerHealthGrab, float>("grabbingTimer");
        private static readonly AccessTools.FieldRef<PlayerAvatar, string> steamIDRef = AccessTools.FieldRefAccess<PlayerAvatar, string>("steamID");

        private static List<string> SuccessfulHealChats = new List<string>();
        private static List<string> FailedHealChats = new List<string>();
        private static List<string> RareHealChats = new List<string>();

        private void Awake()
        {
            Instance = this;
            LogInstance = Logger;
            Harmony harmony = new Harmony("mod.selfsacrifice");
            harmony.PatchAll();
            LoadHealMessages();
            Logger.LogInfo("SelfSacrifice 1.1.5 loaded");
        }

        // load heal chats from an embedded text file
        private void LoadHealMessages()
        {
            SuccessfulHealChats = new List<string>();
            FailedHealChats = new List<string>();
            RareHealChats = new List<string>();

            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                string resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(name => name.EndsWith("HealChats.txt"));

                if (resourceName == null)
                {
                    Logger.LogWarning("HealChats.txt embedded resource not found");
                    return;
                }

                // use a ResourceStream to load text file
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                using (StreamReader reader = new StreamReader(stream))
                {
                    List<string> currentList = SuccessfulHealChats;

                    while (!reader.EndOfStream)
                    {
                        string line = reader.ReadLine().Trim();
                        if (string.IsNullOrEmpty(line))
                            continue;

                        if (line.StartsWith("#"))
                        {
                            if (line.Contains("SuccessfulHealChats"))
                                currentList = SuccessfulHealChats;
                            else if (line.Contains("FailedHealChats"))
                                currentList = FailedHealChats;
                            else if (line.Contains("RareHealChats"))
                                currentList = RareHealChats;

                            continue;
                        }

                        currentList.Add(line);
                    }
                }

                Logger.LogInfo($"loaded {SuccessfulHealChats.Count} successful chats, {FailedHealChats.Count} failed chats, {RareHealChats.Count} rare chats");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"failed to load HealChats.txt: {ex.Message}");
            }
        }

        // we call this as a coroutine to inflict a delayed kill on the recipient of a failed heal
        public IEnumerator DelayedKill(PlayerAvatar recipient, float delay)
        {
            yield return new WaitForSeconds(delay);
            recipient.playerHealth.HurtOther(999, Vector3.zero, false, -1);
        }

        // ensures that every PlayerAvatar has a PossessChatHandler component
        [HarmonyPatch(typeof(PlayerAvatar), "Start")]
        public class PlayerAvatarStartPatch
        {
            static void Postfix(PlayerAvatar __instance)
            {
                if (__instance.GetComponent<PossessChatHandler>() == null)
                {
                    __instance.gameObject.AddComponent<PossessChatHandler>();
                }
            }
        }

        // handles forced sending of chat messages via recipient.GetComponent<PhotonView>()
        public class PossessChatHandler : MonoBehaviourPun
        {
            [PunRPC]
            public void ForcePossessChat(string message, float typingSpeed, float delay)
            {
                if (ChatManager.instance != null)
                {
                    ChatManager.instance.PossessChatScheduleStart(2);
                    ChatManager.instance.PossessChat(ChatManager.PossessChatID.SelfDestruct, message, typingSpeed, Color.yellow, delay, true, 2, null);
                    ChatManager.instance.PossessChatScheduleEnd();
                }
            }
        }

        // a patch which allows health donations at 10hp or less, and also applies some additional logic
        [HarmonyPatch(typeof(PlayerHealthGrab), "Update")]
        public class PlayerHealthGrabPatch
        {
            static void Postfix(PlayerHealthGrab __instance)
            {
                if (!PhotonNetwork.IsMasterClient) return;

                var grabbers = staticGrabObjectRef(__instance).playerGrabbing.ToList();
                float timer = grabbingTimerRef(__instance);

                if (grabbers == null || grabbers.Count == 0)
                {
                    if (timer != 0f)
                    {
                        grabbingTimerRef(__instance) = 0f;
                    }
                    return;
                }

                grabbingTimerRef(__instance) = timer + Time.deltaTime;
                if (grabbingTimerRef(__instance) < 1f) return;

                foreach (var grabber in grabbers)
                {
                    if (grabber == null || grabber.playerAvatar == null || grabber.playerAvatar.playerHealth == null) continue;

                    var donor = grabber.playerAvatar;
                    var donorHealth = donor.playerHealth;
                    var recipientHealth = __instance.playerAvatar.playerHealth;
                    var recipient = __instance.playerAvatar;

                    int donorCurrent = healthRef(donorHealth);
                    int recipientCurrent = healthRef(recipientHealth);
                    int recipientMax = maxHealthRef(recipientHealth);

                    if (recipientCurrent >= recipientMax) continue;

                    // if the donor is at 10hp or less, we execute an rng roll which executes one of a few possible outcomes
                    if (donorCurrent <= 10)
                    {
                        float rng = UnityEngine.Random.Range(0f, 1f);
                        Instance.Logger.LogInfo($"Self-Sacrifice: RNG roll = {rng:F4}");
                        // 1% chance to heal both players to full
                        if (rng <= 0.01f)
                        {
                            recipientHealth.HealOther(999, true);
                            donorHealth.HealOther(999, true);
                            donor.HealedOther();
                            string msg = RareHealChats[UnityEngine.Random.Range(0, RareHealChats.Count)];
                            recipient.GetComponent<PhotonView>().RPC("ForcePossessChat", recipient.GetComponent<PhotonView>().Owner, msg, 3f, 0f);
                            recipient.OverridePupilSize(3f, 4, 1f, 1f, 15f, 0.3f, 3f);
                            recipient.playerHealth.EyeMaterialOverride(PlayerHealth.EyeOverrideState.Love, 5f, 10);
                            Instance.Logger.LogInfo("Self-Sacrifice: super-rare RNG roll achieved, both players healed to full");
                        }
                        // 20% chance to kill the recipient (in addition to the donor)
                        else if (rng <= 0.21f)
                        {
                            donorHealth.HurtOther(10, Vector3.zero, false, -1);
                            recipient.StartCoroutine(Instance.DelayedKill(recipient, 5f));
                            string msg = FailedHealChats[UnityEngine.Random.Range(0, FailedHealChats.Count)];
                            recipient.GetComponent<PhotonView>().RPC("ForcePossessChat", recipient.GetComponent<PhotonView>().Owner, msg, 3f, 0f);
                            recipient.OverridePupilSize(4f, 4, 1f, 1f, 15f, 0.3f, 3f);
                            recipient.playerHealth.EyeMaterialOverride(PlayerHealth.EyeOverrideState.Red, 5f, 10);
                            Instance.Logger.LogInfo("Self-Sacrifice: recipient struck down during sacrifice. F in chat");
                        }
                        // 10% chance for the recipient to receive a permanent +1 boost to a random stat
                        else if (rng <= 0.31f)
                        {
                            string steamID = steamIDRef(recipient);
                            int boostType = UnityEngine.Random.Range(0, 6);
                            string msg = "";
                            switch (boostType)
                            {
                                case 0:
                                    PunManager.instance.UpgradePlayerHealth(steamID);
                                    msg = "I feel healthy as a horse";
                                    Instance.Logger.LogInfo("Self-Sacrifice: recipient granted bonus MAX HEALTH");
                                    break;
                                case 1:
                                    PunManager.instance.UpgradePlayerEnergy(steamID);
                                    msg = "I could run a marathon right now";
                                    Instance.Logger.LogInfo("Self-Sacrifice: recipient granted bonus STAMINA");
                                    break;
                                case 2:
                                    PunManager.instance.UpgradePlayerSprintSpeed(steamID);
                                    msg = "I am speeeeeeeeeeed";
                                    Instance.Logger.LogInfo("Self-Sacrifice: recipient granted bonus SPRINT SPEED");
                                    break;
                                case 3:
                                    PunManager.instance.UpgradePlayerExtraJump(steamID);
                                    msg = "boing boing";
                                    Instance.Logger.LogInfo("Self-Sacrifice: recipient granted bonus EXTRA JUMP");
                                    break;
                                case 4:
                                    PunManager.instance.UpgradePlayerGrabStrength(steamID);
                                    msg = "strength blessing activate";
                                    Instance.Logger.LogInfo("Self-Sacrifice: recipient granted bonus GRAB STRENGTH");
                                    break;
                                case 5:
                                    PunManager.instance.UpgradePlayerTumbleLaunch(steamID);
                                    msg = "launch blessing activate";
                                    Instance.Logger.LogInfo("Self-Sacrifice: recipient granted bonus TUMBLE LAUNCH");
                                    break;
                            }
                            donorHealth.HurtOther(10, Vector3.zero, false, -1);
                            recipient.GetComponent<PhotonView>().RPC("ForcePossessChat", recipient.GetComponent<PhotonView>().Owner, msg, 3f, 0f);
                            recipient.OverridePupilSize(3f, 4, 1f, 1f, 15f, 0.3f, 3f);
                            recipient.playerHealth.EyeMaterialOverride(PlayerHealth.EyeOverrideState.Love, 5f, 10);
                        }
                        // 69% chance to heal the recipient for 25hp
                        else
                        {
                            recipientHealth.HealOther(25, true);
                            donorHealth.HurtOther(10, Vector3.zero, false, -1);
                            donor.HealedOther();
                            string msg = SuccessfulHealChats[UnityEngine.Random.Range(0, SuccessfulHealChats.Count)];
                            recipient.GetComponent<PhotonView>().RPC("ForcePossessChat", recipient.GetComponent<PhotonView>().Owner, msg, 3f, 0f);
                            Instance.Logger.LogInfo("Self-Sacrifice: donor healed recipient successfully");
                        }
                    }
                    // fallback to standard heal mechanics
                    else
                    {
                        recipientHealth.HealOther(10, true);
                        donorHealth.HurtOther(10, Vector3.zero, false, -1);
                        donor.HealedOther();
                    }
                }
                grabbingTimerRef(__instance) = 0f;
            }
        }
    }
}