using BepInEx;
using HarmonyLib;
using UnityEngine;
using Photon.Pun;
using System;
using System.Linq;

namespace SelfSacrifice
{
    [BepInPlugin("mod.selfsacrifice", "SelfSacrifice", "1.0.4")]
    public class SelfSacrificePlugin : BaseUnityPlugin
    {
        public static SelfSacrificePlugin Instance { get; private set; }

        private static readonly AccessTools.FieldRef<PlayerHealthGrab, bool> colliderActiveRef = AccessTools.FieldRefAccess<PlayerHealthGrab, bool>("colliderActive");
        private static readonly AccessTools.FieldRef<PlayerHealthGrab, StaticGrabObject> staticGrabObjectRef = AccessTools.FieldRefAccess<PlayerHealthGrab, StaticGrabObject>("staticGrabObject");
        private static readonly AccessTools.FieldRef<PlayerHealthGrab, float> hideLerpRef = AccessTools.FieldRefAccess<PlayerHealthGrab, float>("hideLerp");
        private static readonly AccessTools.FieldRef<PlayerHealthGrab, Collider> physColliderRef = AccessTools.FieldRefAccess<PlayerHealthGrab, Collider>("physCollider");
        private static readonly AccessTools.FieldRef<PlayerHealth, int> healthRef = AccessTools.FieldRefAccess<PlayerHealth, int>("health");
        private static readonly AccessTools.FieldRef<PlayerHealth, int> maxHealthRef = AccessTools.FieldRefAccess<PlayerHealth, int>("maxHealth");
        private static readonly AccessTools.FieldRef<PlayerAvatar, bool> isTumblingRef = AccessTools.FieldRefAccess<PlayerAvatar, bool>("isTumbling");
        private static readonly AccessTools.FieldRef<PlayerAvatar, bool> isDisabledRef = AccessTools.FieldRefAccess<PlayerAvatar, bool>("isDisabled");
        private static readonly AccessTools.FieldRef<PlayerAvatar, string> steamIDRef = AccessTools.FieldRefAccess<PlayerAvatar, string>("steamID");

        private void Awake()
        {
            Instance = this;
            Harmony harmony = new Harmony("mod.selfsacrifice");
            harmony.PatchAll();
            Logger.LogInfo("SelfSacrifice 1.0.4 loaded");
        }

        // a patch which allows health donations at 10hp or less, and also applies some additional logic
        [HarmonyPatch(typeof(PlayerHealthGrab), "Update")]
        public class PlayerHealthGrabUpdatePatch
        {
            static bool Prefix(PlayerHealthGrab __instance, ref float ___grabbingTimer)
            {
                if (!PhotonNetwork.IsMasterClient)
                {
                    return true;
                }
                if (isTumblingRef(__instance.playerAvatar) || SemiFunc.RunIsShop() || SemiFunc.RunIsArena())
                {
                    return true;
                }

                if (hideLerpRef(__instance) > 0f)
                {
                    colliderActiveRef(__instance) = false;
                }
                else if (!isDisabledRef(__instance.playerAvatar))
                {
                    colliderActiveRef(__instance) = true;
                }

                physColliderRef(__instance).enabled = colliderActiveRef(__instance);
                __instance.transform.position = __instance.followTransform.position;
                __instance.transform.rotation = __instance.followTransform.rotation;

                if (colliderActiveRef(__instance) && staticGrabObjectRef(__instance).playerGrabbing.Count > 0)
                {
                    ___grabbingTimer += Time.deltaTime;
                    Instance.Logger.LogInfo($"Self-Sacrifice: grabbingTimer incremented to {___grabbingTimer:F4}");

                    if (___grabbingTimer >= 1f)
                    {
                        var grabbers = staticGrabObjectRef(__instance).playerGrabbing.ToList();
                        foreach (PhysGrabber grabber in grabbers)
                        {
                            if (grabber == null || grabber.playerAvatar == null)
                            {
                                Instance.Logger.LogWarning("SelfSacrifice: skipping null grabber or grabber with no avatar");
                                continue;
                            }

                            PlayerAvatar donor = grabber.playerAvatar;
                            PlayerHealth donorHealth = donor.playerHealth;
                            PlayerHealth recipientHealth = __instance.playerAvatar.playerHealth;

                            int donorCurrentHealth = healthRef(donorHealth);
                            int recipientCurrentHealth = healthRef(recipientHealth);
                            int recipientMax = maxHealthRef(recipientHealth);

                            if (recipientCurrentHealth >= recipientMax || donorCurrentHealth <= 0)
                            {
                                continue;
                            }

                            donorHealth.HurtOther(10, Vector3.zero, false, -1);
                            donor.HealedOther();

                            if (donorCurrentHealth <= 10)
                            {
                                float rng = UnityEngine.Random.Range(0f, 1f);
                                Instance.Logger.LogInfo($"Self-Sacrifice: RNG roll = {rng:F4}");

                                if (rng <= 0.01f)
                                {
                                    donorHealth.HealOther(999, true);
                                    recipientHealth.HealOther(999, true);
                                    Instance.Logger.LogInfo("Self-Sacrifice: the gods smile on you! both donor and recipient were healed to full health");
                                }
                                else if (rng <= 0.16f)
                                {
                                    recipientHealth.HurtOther(999, Vector3.zero, false, -1);
                                    Instance.Logger.LogInfo("Self-Sacrifice: donor sacrificed themselves to heal their friend, but the recipient was struck down. F in the chat");
                                }
                                else if (rng <= 0.26f)
                                {
                                    string steamID = steamIDRef(__instance.playerAvatar);
                                    int boostType = UnityEngine.Random.Range(0, 5);
                                    switch (boostType)
                                    {
                                        case 0:
                                            PunManager.instance.UpgradePlayerHealth(steamID);
                                            Instance.Logger.LogInfo("Self-Sacrifice: recipient granted bonus MAX HEALTH");
                                            break;
                                        case 1:
                                            PunManager.instance.UpgradePlayerEnergy(steamID);
                                            Instance.Logger.LogInfo("Self-Sacrifice: recipient granted bonus STAMINA");
                                            break;
                                        case 2:
                                            PunManager.instance.UpgradePlayerSprintSpeed(steamID);
                                            Instance.Logger.LogInfo("Self-Sacrifice: recipient granted bonus SPRINT SPEED");
                                            break;
                                        case 3:
                                            PunManager.instance.UpgradePlayerExtraJump(steamID);
                                            Instance.Logger.LogInfo("Self-Sacrifice: recipient granted bonus EXTRA JUMP");
                                            break;
                                        case 4:
                                            PunManager.instance.UpgradePlayerGrabStrength(steamID);
                                            Instance.Logger.LogInfo("Self-Sacrifice: recipient granted bonus GRAB STRENGTH");
                                            break;
                                    }
                                }
                                else
                                {
                                    recipientHealth.HealOther(25, true);
                                    Instance.Logger.LogInfo("Self-Sacrifice: donor sacrificed themselves and healed the recipient successfully");
                                }
                            }
                            else
                            {
                                recipientHealth.HealOther(10, true);
                                Instance.Logger.LogInfo("Self-Sacrifice: regular heal occurred (no sacrifice)");
                            }
                        }
                        ___grabbingTimer = 0f;
                        Instance.Logger.LogInfo("Self-Sacrifice: grabbingTimer1 reset to 0");
                    }
                }
                else
                {
                    if (___grabbingTimer != 0f)
                    {
                        Instance.Logger.LogInfo("Self-Sacrifice: grabbingTimer2 reset to 0");
                    }
                    ___grabbingTimer = 0f;
                }
                return false;
            }
        }
    }
}