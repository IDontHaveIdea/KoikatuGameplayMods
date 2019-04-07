﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ActionGame;
using ADV.Commands.Game;
using Config;
using Harmony;
using UniRx;
using UnityEngine;
using UnityEngine.UI;
using Random = System.Random;

namespace KoikatuGameplayMod
{

    internal static class Hooks
    {
        private const int UnlockedMaxCharacters = 99;
        public static readonly Random RandomGen = new Random();

        public static void ApplyHooks(HarmonyInstance instance)
        {
            instance.PatchAll(typeof(Hooks));

            var t = typeof(ActionScene).GetNestedType("<NPCLoadAll>c__IteratorD", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var m = t.GetMethod("MoveNext");
            instance.Patch(m, null, null, new HarmonyMethod(typeof(Hooks), nameof(NPCLoadAllUnlock)));
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(CharaPersonal), "GetBustSize", new[] { typeof(ChaFileControl) })]
        public static IEnumerable<CodeInstruction> GetBustSizeTranspiler(IEnumerable<CodeInstruction> instr)
        {
            foreach (var instruction in instr)
            {
                if (KoikatuGameplayMod.AdjustBreastSizeQuestion.Value)
                {
                    if (instruction.operand is float f && Equals(f, 0.4f))
                    {
                        instruction.operand = 0.3f;
                    }
                    else if (instruction.operand is float f2 && Equals(f2, 0.7f))
                    {
                        instruction.operand = 0.55f;
                    }
                }
                yield return instruction;
            }
        }

        #region ExitFirstH

        [HarmonyPostfix]
        [HarmonyPatch(typeof(HSceneProc), "NewHeroineEndProc")]
        public static void NewHeroineEndProcPost(HSceneProc __instance)
        {
            OnHEnd(__instance);
        }
        [HarmonyPostfix]
        [HarmonyPatch(typeof(HSceneProc), "EndProc")]
        public static void EndProcPost(HSceneProc __instance)
        {
            OnHEnd(__instance);
        }

        private static void OnHEnd(HSceneProc proc)
        {
            // If girl is stil a virgin, keep her first time status
            foreach (var heroine in proc.flags.lstHeroine)
            {
                if (heroine.isVirgin && heroine.isAnalVirgin)
                    heroine.hCount = 0;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(HSceneProc), "Start")]
        public static void HSpriteUpdatePre(HSceneProc __instance)
        {
            // Adjust help sprite location so it doesn't cover the back button
            var rt = __instance.sprite.objFirstHHelpBase.transform.parent.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.04f, 0f);
            rt.offsetMax = Vector2.zero;
            rt.offsetMin = Vector2.zero;
        }
        [HarmonyPrefix]
        [HarmonyPatch(typeof(HSprite), "Update")]
        public static void HSpriteUpdatePre(HSprite __instance, out bool __state)
        {
            // Skip code that hides the back button
            __state = false;
            if (__instance.flags.lstHeroine.Count != 0 && __instance.flags.lstHeroine[0].hCount == 0)
            {
                __state = true;
                __instance.flags.lstHeroine[0].hCount = 1;
            }
        }
        [HarmonyPostfix]
        [HarmonyPatch(typeof(HSprite), "Update")]
        public static void HSpriteUpdatePost(HSprite __instance, bool __state)
        {
            // Restore original hcount
            if (__state)
                __instance.flags.lstHeroine[0].hCount = 0;
        }

        #endregion

        #region Chara limit unlock

        public static IEnumerable<CodeInstruction> NPCLoadAllUnlock(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_I4_S)
                {
                    if (((sbyte)0x26).Equals(instruction.operand))
                        instruction.operand = UnlockedMaxCharacters;
                }
                yield return instruction;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ClassRoomList), "Start")]
        public static void ClassRoomListUnlock(ClassRoomList __instance)
        {
            var f = typeof(ClassRoomList).GetField("sldAttendanceNum", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var sld = (Slider)f.GetValue(__instance);
            sld.maxValue = UnlockedMaxCharacters;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(EtceteraSetting), "Init")]
        public static void EtceteraSettingUnlock(EtceteraSetting __instance)
        {
            var f = typeof(EtceteraSetting).GetField("maxCharaNumSlider", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var sld = (Slider)f.GetValue(__instance);
            sld.maxValue = UnlockedMaxCharacters;
        }

        #endregion

        [HarmonyPostfix]
        [HarmonyPatch(typeof(HSprite), "Start", new Type[] { })]
        public static void HookToEndHButton(HSprite __instance)
        {
            var f = typeof(HSprite).GetField("btnEnd",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            var b = f.GetValue(__instance) as UnityEngine.UI.Button;

            // Modify girl's lewdness after exiting h scene based on scene stats
            b.OnClickAsObservable().Subscribe(unit =>
            {
                UpdateGirlLewdness(__instance);
                ApplyGirlAnger(__instance);
            });
        }

        #region Modify girl lewd level

        private static void UpdateGirlLewdness(HSprite __instance)
        {
            if (!KoikatuGameplayMod.DecreaseLewd.Value) return;

            var flags = __instance.flags;
            var count = flags.count;
            var heroine = Utilities.GetTargetHeroine(__instance);
            if (heroine == null) return;

            if (flags.GetOrgCount() == 0)
            {
                var massageTotal = (int)(count.selectAreas.Sum() / 4 + (+count.kiss + count.houshiOutside + count.houshiInside) * 10);
                if (massageTotal <= 5)
                    heroine.lewdness = Math.Max(0, heroine.lewdness - 30);
                else
                    heroine.lewdness = Math.Min(100, heroine.lewdness + massageTotal);
            }
            else if (count.aibuOrg > 0 && count.sonyuOrg + count.sonyuAnalOrg == 0)
                heroine.lewdness = Math.Min(100, heroine.lewdness - (count.aibuOrg - 1) * 20);
            else
            {
                int cumCount = count.sonyuCondomInside + count.sonyuInside + count.sonyuOutside + count.sonyuAnalCondomInside + count.sonyuAnalInside + count.sonyuAnalOutside;
                if (cumCount > 0)
                    heroine.lewdness = Math.Max(0, heroine.lewdness - cumCount * 20);

                heroine.lewdness = Math.Max(0, heroine.lewdness - count.aibuOrg * 20);
            }
        }

        #endregion

        #region FastTravelCost

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapSelectMenuScene), "Start", new Type[] { })]
        public static void MapSelectMenuSceneRegisterCallback(MapSelectMenuScene __instance)
        {
            var f = typeof(MapSelectMenuScene).GetField("enterButton",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            var b = f.GetValue(__instance) as UnityEngine.UI.Button;

            // Add a time penalty for using F3 fast travel
            b.OnClickAsObservable().Subscribe(unit =>
            {
                if (__instance.result == MapSelectMenuScene.ResultType.EnterMapMove)
                {
                    var cycle = UnityEngine.Object.FindObjectsOfType<ActionGame.Cycle>().FirstOrDefault();
                    if (cycle != null)
                    {
                        var newVal = Math.Min(cycle.timer + KoikatuGameplayMod.FastTravelTimePenalty.Value, ActionGame.Cycle.TIME_LIMIT - 10);
                        typeof(ActionGame.Cycle)
                            .GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)
                            .SetValue(cycle, newVal);
                    }
                }
            });
        }

        #endregion

        #region ForceAnal

        [HarmonyPrefix]
        [HarmonyPatch(typeof(HSprite), nameof(HSprite.OnInsertAnalClick), new Type[] { })]
        public static void OnInsertAnalClickPre(HSprite __instance)
        {
            if (!Input.GetMouseButtonUp(0) || !__instance.IsSpriteAciotn())
                return;

            if (__instance.flags.isAnalInsertOK)
                return;

            // Check if player can circumvent the anal deny
            if (__instance.flags.count.sonyuAnalOrg >= 1)
            {
                var heroine = Utilities.GetTargetHeroine(__instance);

                MakeGirlAngry(heroine, 20, 10);

                Utilities.ForceAllowInsert(__instance);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(HSprite), nameof(HSprite.OnInsertAnalNoVoiceClick), new Type[] { })]
        public static void OnInsertAnalNoVoiceClickPre(HSprite __instance)
        {
            if (!Input.GetMouseButtonUp(0) || !__instance.IsSpriteAciotn())
                return;

            if (__instance.flags.isAnalInsertOK)
                return;

            var heroine = Utilities.GetTargetHeroine(__instance);
            if (heroine == null) return;

            // Check if player can circumvent the anal deny
            if (!KoikatuGameplayMod.ForceInsert.Value) return;
            if (CanCircumventDeny(__instance) || __instance.flags.count.sonyuAnalOrg >= 1)
            {
                MakeGirlAngry(heroine, 30, 15);

                Utilities.ForceAllowInsert(__instance);
                __instance.flags.isDenialvoiceWait = false;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(HSprite), nameof(HSprite.OnInsertAnalClick), new Type[] { })]
        public static void OnInsertAnalClickPost(HSprite __instance)
        {
            Utilities.ResetForceAllowInsert(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(HSprite), nameof(HSprite.OnInsertAnalNoVoiceClick), new Type[] { })]
        public static void OnInsertAnalNoVoiceClickPost(HSprite __instance)
        {
            Utilities.ResetForceAllowInsert(__instance);
        }

        #endregion

        #region ForceRaw

        [HarmonyPrefix]
        [HarmonyPatch(typeof(HSprite), nameof(HSprite.OnInsertNoVoiceClick), new Type[] { })]
        public static void OnInsertNoVoiceClickPre(HSprite __instance)
        {
            if (!Input.GetMouseButtonUp(0) || !__instance.IsSpriteAciotn())
                return;

            if (__instance.flags.isInsertOK[Utilities.GetTargetHeroineId(__instance)])
                return;

            var heroine = Utilities.GetTargetHeroine(__instance);
            var girlOrgasms = __instance.flags.count.sonyuOrg;

            // Check if player can circumvent the raw deny
            if (!KoikatuGameplayMod.ForceInsert.Value) return;
            if (CanCircumventDeny(__instance) ||
                girlOrgasms >= 3 + RandomGen.Next(0, 3) - heroine.lewdness / 66)
            {
                MakeGirlAngry(heroine, 20, 10);

                Utilities.ForceAllowInsert(__instance);
                __instance.flags.isDenialvoiceWait = false;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(HSprite), nameof(HSprite.OnInsertClick), new Type[] { })]
        public static void OnInsertClickPre(HSprite __instance)
        {
            if (!Input.GetMouseButtonUp(0) || !__instance.IsSpriteAciotn())
                return;

            if (__instance.flags.isInsertOK[Utilities.GetTargetHeroineId(__instance)])
                return;

            var heroine = Utilities.GetTargetHeroine(__instance);
            if (heroine == null) return;
            var girlOrgasms = __instance.flags.count.sonyuOrg;

            // Check if girl allows raw
            if (girlOrgasms >= 4 + RandomGen.Next(0, 3) - heroine.lewdness / 45)
                Utilities.ForceAllowInsert(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(HSprite), nameof(HSprite.OnInsertNoVoiceClick), new Type[] { })]
        public static void OnInsertNoVoiceClickPost(HSprite __instance)
        {
            Utilities.ResetForceAllowInsert(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(HSprite), nameof(HSprite.OnInsertClick), new Type[] { })]
        public static void OnInsertClickPost(HSprite __instance)
        {
            Utilities.ResetForceAllowInsert(__instance);
        }

        /// <summary>
        /// ang 15 fav 10
        /// </summary>
        /// <param name="heroine"></param>
        /// <param name="angerAmount"></param>
        /// <param name="favorAmount"></param>
        private static void MakeGirlAngry(SaveData.Heroine heroine, int angerAmount, int favorAmount)
        {
            if (!KoikatuGameplayMod.ForceInsertAnger.Value) return;

            heroine.anger = Math.Min(100, heroine.anger + angerAmount);
            heroine.favor = Math.Max(0, heroine.favor - favorAmount);

            heroine.chaCtrl.tearsLv = 2;
            heroine.chaCtrl.ChangeEyesShaking(true);
            heroine.chaCtrl.ChangeLookEyesTarget(2);
            heroine.chaCtrl.ChangeTongueState(0);
            heroine.chaCtrl.ChangeEyesOpenMax(1f);
        }

        private static bool CanCircumventDeny(HSprite __instance)
        {
            // OUT_A is resting after popping the cork outdoors
            return string.Equals(__instance.flags.nowAnimStateName, "OUT_A", StringComparison.Ordinal) ||
                   string.Equals(__instance.flags.nowAnimStateName, "A_OUT_A", StringComparison.Ordinal) ||
                   __instance.flags.isDenialvoiceWait;
        }

        private static void ApplyGirlAnger(HSprite __instance)
        {
            if (!KoikatuGameplayMod.ForceInsertAnger.Value) return;

            var heroine = Utilities.GetTargetHeroine(__instance);
            if (heroine == null) return;

            if (!__instance.flags.isInsertOK[Utilities.GetTargetHeroineId(__instance)])
            {
                if (__instance.flags.count.sonyuInside > 0)
                {
                    if (HFlag.GetMenstruation(heroine.MenstruationDay) == HFlag.MenstruationType.危険日)
                    {
                        // If it's dangerous always make her angry
                        heroine.anger = Math.Min(100, heroine.anger + __instance.flags.count.sonyuInside * 45);
                        heroine.isAnger = true;
                    }
                    else
                    {
                        heroine.anger = Math.Min(100, heroine.anger + __instance.flags.count.sonyuInside * 25);
                    }
                }
                else if (__instance.flags.count.sonyuOutside > 0)
                {
                    heroine.anger = Math.Max(0, heroine.anger - __instance.flags.count.sonyuOutside * 10);
                }
            }

            if (heroine.anger >= 100)
                heroine.isAnger = true;
        }

        #endregion
    }
}