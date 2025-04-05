using BepInEx;
using HarmonyLib;
using UnityEngine;
using Photon.Pun;

namespace CustomHealthTransfer
{
    [BepInPlugin("mod.selfsacrifice", "SelfSacrifice", "1.0.1")]
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
            Logger.LogInfo("SelfSacrifice 1.0.1 loaded");
        }

        // a patch which allows health donations at 10hp or less, and also applies some additional logic
        [HarmonyPatch(typeof(PlayerHealthGrab), "Update")]
        public class PlayerHealthGrabUpdatePatch
        {
            static bool Prefix(PlayerHealthGrab __instance, ref float ___grabbingTimer)
            {
                if (!PhotonNetwork.IsMasterClient)
                    return true;

                // this is mostly taken direct from the game's PlayerHealthGrab.Update method
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

                if (colliderActiveRef(__instance))
                {
                    if (staticGrabObjectRef(__instance).playerGrabbing.Count > 0)
                    {
                        ___grabbingTimer += Time.deltaTime;

                        foreach (PhysGrabber grabber in staticGrabObjectRef(__instance).playerGrabbing)
                        {
                            if (___grabbingTimer >= 1f)
                            {
                                PlayerAvatar donor = grabber.playerAvatar;
                                PlayerHealth donorHealth = donor.playerHealth;
                                PlayerHealth recipientHealth = __instance.playerAvatar.playerHealth;

                                int donorCurrentHealth = healthRef(donorHealth);
                                int recipientCurrentHealth = healthRef(recipientHealth);
                                int recipientMax = maxHealthRef(recipientHealth);

                                if (recipientCurrentHealth < recipientMax && donorCurrentHealth > 0)
                                {
                                    donorHealth.HurtOther(10, Vector3.zero, false, -1);
                                    donor.HealedOther();

                                    // a RNG roll is performed if donor has 10 health or less - 84% chance to heal 25hp, 15% chance to kill recipient, 10% chance to give recipient +1 random stat boost, 1% chance to heal both parties to full health
                                    if (donorCurrentHealth <= 10)
                                    {
                                        float rng = Random.Range(0f, 1f);
                                        Instance.Logger.LogInfo($"Self-Sacrifice: RNG roll = {rng:F4}");

                                        if (rng <= 0.01f)
                                        {
                                            donorHealth.HealOther(999, true);
                                            recipientHealth.HealOther(999, true);
                                            Instance.Logger.LogInfo("Self-Sacrifice: the gods smile on you! both donor and recipient were healed to full health");
                                        }
                                        else if (rng <= 0.11f)
                                        {
                                            string steamID = steamIDRef(__instance.playerAvatar);
                                            int boostType = Random.Range(0, 5);

                                            switch (boostType)
                                            {
                                                case 0:
                                                    PunManager.instance.UpgradePlayerHealth(steamID);
                                                    Instance.Logger.LogInfo("SelfSacrifice: recipient granted bonus MAX HEALTH");
                                                    break;
                                                case 1:
                                                    PunManager.instance.UpgradePlayerEnergy(steamID);
                                                    Instance.Logger.LogInfo("SelfSacrifice: recipient granted bonus STAMINA");
                                                    break;
                                                case 2:
                                                    PunManager.instance.UpgradePlayerSprintSpeed(steamID);
                                                    Instance.Logger.LogInfo("SelfSacrifice: recipient granted bonus SPRINT SPEED");
                                                    break;
                                                case 3:
                                                    PunManager.instance.UpgradePlayerExtraJump(steamID);
                                                    Instance.Logger.LogInfo("SelfSacrifice: recipient granted bonus EXTRA JUMP");
                                                    break;
                                                case 4:
                                                    PunManager.instance.UpgradePlayerGrabStrength(steamID);
                                                    Instance.Logger.LogInfo("SelfSacrifice: recipient granted bonus GRAB STRENGTH");
                                                    break;
                                            }
                                        }
                                        else if (rng <= 0.26f)
                                        {
                                            recipientHealth.HurtOther(999, Vector3.zero, false, -1);
                                            Instance.Logger.LogInfo("SelfSacrifice: donor sacrificed themselves to heal their friend, but the recipient was struck down. F in the chat");
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
                                    }
                                }
                            }
                        }

                        if (___grabbingTimer >= 1f)
                        {
                            ___grabbingTimer = 0f;
                        }
                    }
                    else
                    {
                        ___grabbingTimer = 0f;
                    }
                }

                return false;
            }
        }
    }
}