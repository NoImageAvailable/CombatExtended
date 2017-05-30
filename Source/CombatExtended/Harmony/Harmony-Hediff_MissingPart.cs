﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using UnityEngine;
using Harmony;

namespace CombatExtended.Harmony
{
    [HarmonyPatch(typeof(Hediff_MissingPart))]
    [HarmonyPatch("IsFresh", PropertyMethod.Getter)]
    static class Harmony_Hediff_MissingPart_IsFresh_Patch
    {
        public static bool Prefix(Hediff_MissingPart __instance, ref bool __result)
        {
            var hediff = Traverse.Create(__instance);
            __result = Current.ProgramState != ProgramState.Entry 
                && hediff.Field("isFreshInt").GetValue<bool>()
                && !hediff.Property("TicksAfterNoLongerFreshPassed").GetValue<bool>()
                && !__instance.Part.def.IsSolid(__instance.Part, __instance.pawn.health.hediffSet.hediffs) 
                && !hediff.Property("ParentIsMissing").GetValue<bool>();
            return false;
        }
    }
}
