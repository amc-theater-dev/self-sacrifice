using BepInEx;
using HarmonyLib;
using UnityEngine;
using Photon.Pun;
using System.Linq;

namespace SelfSacrifice
{
    [BepInPlugin("mod.selfsacrifice", "SelfSacrifice", "1.0.7")]
    public class SelfSacrificePlugin : BaseUnityPlugin
    {
        public static SelfSacrificePlugin Instance { get; private set; }

        private static readonly AccessTools.FieldRef<PlayerHealthGrab, StaticGrabObject> staticGrabObjectRef = AccessTools.FieldRefAccess<PlayerHealthGrab, StaticGrabObject>("staticGrabObject");
        private static readonly AccessTools.FieldRef<PlayerHealth, int> healthRef = AccessTools.FieldRefAccess<PlayerHealth, int>("health");
        private static readonly AccessTools.FieldRef<PlayerHealth, int> maxHealthRef = AccessTools.FieldRefAccess<PlayerHealth, int>("maxHealth");
        private static readonly AccessTools.FieldRef<PlayerHealthGrab, float> grabbingTimerRef = AccessTools.FieldRefAccess<PlayerHealthGrab, float>("grabbingTimer");
        private static readonly AccessTools.FieldRef<PlayerAvatar, string> steamIDRef = AccessTools.FieldRefAccess<PlayerAvatar, string>("steamID");

        private void Awake()
        {
            Instance = this;
            Harmony harmony = new Harmony("mod.selfsacrifice");
            harmony.PatchAll();
            Logger.LogInfo("SelfSacrifice 1.0.7 loaded");
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
                        Instance.Logger.LogInfo("Self-Sacrifice: grabbingTimer reset to 0");
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

                    int donorCurrent = healthRef(donorHealth);
                    int recipientCurrent = healthRef(recipientHealth);
                    int recipientMax = maxHealthRef(recipientHealth);

                    if (recipientCurrent >= recipientMax || donorCurrent <= 0) continue;

                    donorHealth.HurtOther(10, Vector3.zero, false, -1);
                    donor.HealedOther();

                    // if the donor is at 10hp or less, we execute an rng roll which executes one of a few possible outcomes
                    if (donorCurrent <= 10)
                    {
                        float rng = UnityEngine.Random.Range(0f, 1f);
                        Instance.Logger.LogInfo($"Self-Sacrifice: RNG roll = {rng:F4}");

                        if (rng <= 0.01f)
                        {
                            donorHealth.HealOther(999, true);
                            recipientHealth.HealOther(999, true);
                            Instance.Logger.LogInfo("Self-Sacrifice: the gods smile on you! both donor and recipient healed to full");
                        }
                        else if (rng <= 0.16f)
                        {
                            recipientHealth.HurtOther(999, Vector3.zero, false, -1);
                            Instance.Logger.LogInfo("Self-Sacrifice: recipient struck down during sacrifice. F in chat.");
                        }
                        else if (rng <= 0.26f)
                        {
                            string steamID = steamIDRef(__instance.playerAvatar);
                            int boostType = UnityEngine.Random.Range(0, 5);
                            switch (boostType)
                            {
                                case 0:
                                    PunManager.instance.UpgradePlayerHealth(steamID);
                                    Instance.Logger.LogInfo("Self Sacrifice: recipient granted bonus MAX HEALTH");
                                    break;
                                case 1:
                                    PunManager.instance.UpgradePlayerEnergy(steamID);
                                    Instance.Logger.LogInfo("Self Sacrifice: recipient granted bonus STAMINA");
                                    break;
                                case 2:
                                    PunManager.instance.UpgradePlayerSprintSpeed(steamID);
                                    Instance.Logger.LogInfo("Self Sacrifice: recipient granted bonus SPRINT SPEED");
                                    break;
                                case 3:
                                    PunManager.instance.UpgradePlayerExtraJump(steamID);
                                    Instance.Logger.LogInfo("Self Sacrifice: recipient granted bonus EXTRA JUMP");
                                    break;
                                case 4:
                                    PunManager.instance.UpgradePlayerGrabStrength(steamID);
                                    Instance.Logger.LogInfo("Self Sacrifice: recipient granted bonus GRAB STRENGTH");
                                    break;
                            }
                        }
                        else
                        {
                            recipientHealth.HealOther(25, true);
                            Instance.Logger.LogInfo("Self-Sacrifice: donor healed recipient successfully");
                        }
                    }
                    else
                    {
                        recipientHealth.HealOther(10, true);
                        Instance.Logger.LogInfo("Self-Sacrifice: regular heal occurred (no sacrifice)");
                    }
                }
                grabbingTimerRef(__instance) = 0f;
                Instance.Logger.LogInfo("Self-Sacrifice: grabbingTimer reset to 0");
            }
        }
    }
}