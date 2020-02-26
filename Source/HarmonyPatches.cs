//#define PATCH_CALL_REGISTRY

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace DoorsExpanded
{
    // This is a separate class from HarmonyPatches in case any other mod wants to patch transpilers in HarmonyPatches.
    // If those transpilers were in the same class, the static constructor would run before any patching of said transpilers
    // could be done, which makes such patching too late.
    [StaticConstructorOnStartup]
    public static class HarmonyPatchesOnStartup
    {
        static HarmonyPatchesOnStartup()
        {
            var harmony = new Harmony("rimworld.jecrell.doorsexpanded");
            HarmonyPatches.PatchAll(harmony);
            DebugInspectorPatches.PatchDebugInspector(harmony);
        }
    }

    public static class HarmonyPatches
    {
        public static void PatchAll(Harmony harmony)
        {
            HarmonyPatches.harmony = harmony;
            var rwAssembly = typeof(Building_Door).Assembly;

            // Historical note: There used to be patches on the following methods that are no longer necessary since:
            // a) edificeGrid (and thus GridsUtility.GetEdifice) should now never return a Building_DoorExpanded thing
            // b) forbidden state is now synced between Building_DoorExpanded and its invis doors
            // c) were workarounds for bugs that have since been fixed
            // GenPath.ShouldNotEnterCell
            // GenSpawn.SpawnBuildingAsPossible
            // ForbidUtility.IsForbiddenToPass
            // PathFinder.GetBuildingCost
            // PathFinder.BlocksDiagonalMovement
            // PawnPathUtility.TryFindLastCellBeforeBlockingDoor
            // PawnPathUtility.FirstBlockingBuilding
            // RegionTypeUtility.GetExpectedRegionType
            // JobGiver_Manhunter.TryGiveJob
            // JobGiver_SeekAllowedArea.TryGiveJob

            // See comments in Building_DoorRegionHandler.
            Patch(original: AccessTools.Property(typeof(Building_Door), nameof(Building_Door.FreePassage)).GetGetMethod(),
                prefix: nameof(InvisDoorFreePassagePrefix));
            Patch(original: AccessTools.Property(typeof(Building_Door), nameof(Building_Door.WillCloseSoon)).GetGetMethod(),
                prefix: nameof(InvisDoorWillCloseSoonPrefix));
            Patch(original: AccessTools.Property(typeof(Building_Door), nameof(Building_Door.BlockedOpenMomentary)).GetGetMethod(),
                prefix: nameof(InvisDoorBlockedOpenMomentaryPrefix));
            Patch(original: AccessTools.Property(typeof(Building_Door), nameof(Building_Door.SlowsPawns)).GetGetMethod(),
                prefix: nameof(InvisDoorSlowsPawnsPrefix));
            Patch(original: AccessTools.Property(typeof(Building_Door), nameof(Building_Door.TicksToOpenNow)).GetGetMethod(),
                prefix: nameof(InvisDoorTicksToOpenNowPrefix));
            Patch(original: AccessTools.Method(typeof(Building_Door), nameof(Building_Door.CheckFriendlyTouched)),
                prefix: nameof(InvisDoorCheckFriendlyTouchedPrefix));
            Patch(original: AccessTools.Method(typeof(Building_Door), nameof(Building_Door.Notify_PawnApproaching)),
                prefix: nameof(InvisDoorNotifyPawnApproachingPrefix));
            Patch(original: AccessTools.Method(typeof(Building_Door), nameof(Building_Door.CanPhysicallyPass)),
                prefix: nameof(InvisDoorCanPhysicallyPassPrefix));
            Patch(original: AccessTools.Method(typeof(Building_Door), "DoorOpen"),
                prefix: nameof(InvisDoorDoorOpenPrefix));
            Patch(original: AccessTools.Method(typeof(Building_Door), "DoorTryClose"),
                prefix: nameof(InvisDoorDoorTryClosePrefix));
            Patch(original: AccessTools.Method(typeof(Building_Door), nameof(Building_Door.StartManualOpenBy)),
                prefix: nameof(InvisDoorStartManualOpenByPrefix));
            // Building_Door.StartManualCloseBy gets inlined, so can only patch its caller Pawn_PathFollower.TryEnterNextPathCell.
            //Patch(original: AccessTools.Method(typeof(Building_Door), nameof(Building_Door.StartManualCloseBy)),
            //    prefix: nameof(InvisDoorStartManualCloseByPrefix));
            Patch(original: AccessTools.Method(typeof(Pawn_PathFollower), "TryEnterNextPathCell"),
                transpiler: nameof(InvisDoorManualCloseCallTranspiler),
                transpilerRelated: nameof(InvisDoorStartManualCloseBy));

            // Patches to redirect access from invis door def to its parent door def.
            Patch(original: AccessTools.Method(typeof(GenGrid), nameof(GenGrid.CanBeSeenOver), new[] { typeof(Building) }),
                transpiler: nameof(InvisDoorCanBeSeenOverTranspiler));
            Patch(original: AccessTools.FirstMethod(
                    AccessTools.FirstInner(typeof(FloodFillerFog), inner => inner.Name.StartsWith("<FloodUnfog>")),
                    method => method.Name.StartsWith("<") && method.ReturnType == typeof(bool)),
                transpiler: nameof(InvisDoorMakeFogTranspiler));
            Patch(original: AccessTools.Method(typeof(FogGrid), "FloodUnfogAdjacent"),
                transpiler: nameof(InvisDoorMakeFogTranspiler));
            Patch(original: AccessTools.Method(typeof(Projectile), "ImpactSomething"),
                transpiler: nameof(InvisDoorProjectileImpactSomethingTranspiler));
            Patch(original: AccessTools.Method(rwAssembly.GetType("Verse.SectionLayer_IndoorMask"), "HideRainPrimary"),
                transpiler: nameof(InvisDoorSectionLayerIndoorMaskHideRainPrimaryTranspiler));
            Patch(original: AccessTools.Method(typeof(SnowGrid), "CanHaveSnow"),
                transpiler: nameof(InvisDoorCanHaveSnowTranspiler));
            Patch(original: AccessTools.Method(typeof(GlowFlooder), nameof(GlowFlooder.AddFloodGlowFor)),
                transpiler: nameof(InvisDoorBlockLightTranspiler));
            Patch(original: AccessTools.Method(
                    typeof(SectionLayer_LightingOverlay), nameof(SectionLayer_LightingOverlay.Regenerate)),
                transpiler: nameof(InvisDoorBlockLightTranspiler));

            // Other patches for invis doors.
            Patch(original: AccessTools.Method(typeof(PathGrid), nameof(PathGrid.CalculatedCostAt)),
                transpiler: nameof(InvisDoorCalculatedCostAtTranspiler));
            Patch(original: AccessTools.Method(typeof(GenSpawn), nameof(GenSpawn.WipeExistingThings)),
                prefix: nameof(InvisDoorWipeExistingThingsPrefix));
            Patch(original: AccessTools.Method(typeof(GenSpawn), nameof(GenSpawn.SpawningWipes)),
                prefix: nameof(InvisDoorSpawningWipesPrefix));
            Patch(original: AccessTools.Method(typeof(PathFinder), nameof(PathFinder.IsDestroyable)),
                postfix: nameof(InvisDoorIsDestroyablePostfix));
            Patch(original: AccessTools.Method(typeof(MouseoverReadout), nameof(MouseoverReadout.MouseoverReadoutOnGUI)),
                transpiler: nameof(MouseoverReadoutTranspiler));

            // Patches for door expanded doors themselves.
            Patch(original: AccessTools.Method(
                    typeof(CoverUtility), nameof(CoverUtility.BaseBlockChance), new[] { typeof(Thing) }),
                transpiler: nameof(DoorExpandedBaseBlockChanceTranspiler));
            Patch(original: AccessTools.Method(typeof(GenGrid), nameof(GenGrid.CanBeSeenOver), new[] { typeof(Building) }),
                transpiler: nameof(DoorExpandedCanBeSeenOverTranspiler));
            Patch(original: AccessTools.Method(typeof(EdificeGrid), nameof(EdificeGrid.Register)),
                prefix: nameof(DoorExpandedEdificeGridRegisterPrefix));
            Patch(original: AccessTools.Property(typeof(ThingDef), nameof(ThingDef.IsDoor)).GetGetMethod(),
                postfix: nameof(DoorExpandedThingDefIsDoorPostfix));
            Patch(original: AccessTools.Property(typeof(CompForbiddable), nameof(CompForbiddable.Forbidden)).GetSetMethod(),
                transpiler: nameof(DoorExpandedSetForbiddenTranspiler),
                transpilerRelated: nameof(DoorExpandedSetForbidden));
            Patch(original: AccessTools.Method(typeof(RegionAndRoomUpdater), "ShouldBeInTheSameRoomGroup"),
                postfix: nameof(DoorExpandedShouldBeInTheSameRoomGroupPostfix));
            Patch(original: AccessTools.Method(typeof(GenTemperature), nameof(GenTemperature.EqualizeTemperaturesThroughBuilding)),
                transpiler: nameof(DoorExpandedEqualizeTemperaturesThroughBuildingTranspiler),
                transpilerRelated: nameof(GetAdjacentCellsForTemperature));
            Patch(original: AccessTools.Method(typeof(Projectile), "CheckForFreeIntercept"),
                transpiler: nameof(DoorExpandedProjectileCheckForFreeIntercept));
            Patch(original: AccessTools.Method(typeof(TrashUtility), nameof(TrashUtility.TrashJob)),
                transpiler: nameof(DoorExpandedTrashJobTranspiler));

            // Patches for ghost (pre-placement) and blueprints for door expanded.
            Patch(original: AccessTools.FirstMethod(
                    AccessTools.FirstInner(typeof(Designator_Place), inner => inner.Name.StartsWith("<DoExtraGuiControls>")),
                    method => method.Name.StartsWith("<")),
                transpiler: nameof(DoorExpandedDesignatorPlaceRotateAgainIfNeededTranspiler),
                transpilerRelated: nameof(DoorExpandedRotateAgainIfNeeded));
            Patch(original: AccessTools.Method(typeof(Designator_Place), "HandleRotationShortcuts"),
                transpiler: nameof(DoorExpandedDesignatorPlaceRotateAgainIfNeededTranspiler),
                transpilerRelated: nameof(DoorExpandedRotateAgainIfNeeded));
            Patch(original: AccessTools.Method(typeof(GhostDrawer), nameof(GhostDrawer.DrawGhostThing)),
                transpiler: nameof(DoorExpandedDrawGhostThingTranspiler),
                transpilerRelated: nameof(DoorExpandedDrawGhostGraphicFromDef));
            Patch(original: AccessTools.Method(typeof(GhostUtility), nameof(GhostUtility.GhostGraphicFor)),
                transpiler: nameof(DoorExpandedGhostGraphicForTranspiler));
            Patch(original: AccessTools.Method(typeof(Blueprint), nameof(Blueprint.SpawnSetup)),
                prefix: nameof(DoorExpandedBlueprintSpawnSetupPrefix));
            Patch(original: AccessTools.Method(typeof(Blueprint), nameof(Blueprint.Draw)),
                prefix: nameof(DoorExpandedBlueprintDrawPrefix));
        }

        private static Harmony harmony;

        private static void Patch(MethodInfo original, string prefix = null, string postfix = null, string transpiler = null,
            string transpilerRelated = null, bool harmonyDebug = false)
        {
            DebugInspectorPatches.RegisterPatch(prefix);
            DebugInspectorPatches.RegisterPatch(postfix);
            DebugInspectorPatches.RegisterPatch(transpilerRelated);
            harmony.Patch(original,
                prefix == null ? null : new HarmonyMethod(typeof(HarmonyPatches), prefix) { debug = harmonyDebug },
                postfix == null ? null : new HarmonyMethod(typeof(HarmonyPatches), postfix) { debug = harmonyDebug },
                transpiler == null ? null : new HarmonyMethod(typeof(HarmonyPatches), transpiler) { debug = harmonyDebug });
        }

        private static bool IsDoorExpandedDef(Def def) =>
            def is ThingDef thingDef && typeof(Building_DoorExpanded).IsAssignableFrom(thingDef.thingClass);

        private static bool IsVanillaDoorDef(Def def) =>
            def is ThingDef thingDef && typeof(Building_Door).IsAssignableFrom(thingDef.thingClass);

        private static bool IsInvisDoorDef(Def def) =>
            def is ThingDef thingDef && typeof(Building_DoorRegionHandler).IsAssignableFrom(thingDef.thingClass);

        private static Thing GetActualDoor(Thing thing) =>
            thing is Building_DoorRegionHandler invisDoor ? invisDoor.ParentDoor : thing;

        // Building_Door.FreePassage
        public static bool InvisDoorFreePassagePrefix(Building_Door __instance, ref bool __result)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(InvisDoorFreePassagePrefix));
            if (__instance is Building_DoorRegionHandler invisDoor)
            {
                __result = invisDoor.ParentDoor?.FreePassage ?? true;
                return false;
            }
            return true;
        }

        // Building_Door.WillCloseSoon
        public static bool InvisDoorWillCloseSoonPrefix(Building_Door __instance, ref bool __result)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(InvisDoorWillCloseSoonPrefix));
            if (__instance is Building_DoorRegionHandler invisDoor)
            {
                __result = invisDoor.ParentDoor?.WillCloseSoon ?? false;
                return false;
            }
            return true;
        }

        // Building_Door.BlockedOpenMomentary
        public static bool InvisDoorBlockedOpenMomentaryPrefix(Building_Door __instance, ref bool __result)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(InvisDoorBlockedOpenMomentaryPrefix));
            if (__instance is Building_DoorRegionHandler invisDoor)
            {
                __result = invisDoor.ParentDoor?.BlockedOpenMomentary ?? false;
                return false;
            }
            return true;
        }

        // Building_Door.SlowsPawns
        public static bool InvisDoorSlowsPawnsPrefix(Building_Door __instance, ref bool __result)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(InvisDoorSlowsPawnsPrefix));
            if (__instance is Building_DoorRegionHandler invisDoor)
            {
                __result = invisDoor.ParentDoor?.SlowsPawns ?? false;
                return false;
            }
            return true;
        }

        // Building_Door.TicksToOpenNow
        public static bool InvisDoorTicksToOpenNowPrefix(Building_Door __instance, ref int __result)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(InvisDoorTicksToOpenNowPrefix));
            if (__instance is Building_DoorRegionHandler invisDoor)
            {
                __result = invisDoor.ParentDoor?.TicksToOpenNow ?? 0;
                return false;
            }
            return true;
        }

        // Building_Door.CheckFriendlyTouched
        public static bool InvisDoorCheckFriendlyTouchedPrefix(Building_Door __instance, Pawn p)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(InvisDoorCheckFriendlyTouchedPrefix));
            if (__instance is Building_DoorRegionHandler invisDoor)
            {
                invisDoor.ParentDoor?.CheckFriendlyTouched(p);
                return false;
            }
            return true;
        }

        // Building_Door.Notify_PawnApproaching
        public static bool InvisDoorNotifyPawnApproachingPrefix(Building_Door __instance, Pawn p, int moveCost)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(InvisDoorNotifyPawnApproachingPrefix));
            if (__instance is Building_DoorRegionHandler invisDoor)
            {
                invisDoor.ParentDoor?.Notify_PawnApproaching(p, moveCost);
                return false;
            }
            return true;
        }

        // Building_Door.CanPhysicallyPass
        public static bool InvisDoorCanPhysicallyPassPrefix(Building_Door __instance, Pawn p, ref bool __result)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(InvisDoorCanPhysicallyPassPrefix));
            if (__instance is Building_DoorRegionHandler invisDoor)
            {
                __result = invisDoor.ParentDoor?.CanPhysicallyPass(p) ?? true;
                return false;
            }
            return true;
        }

        // Building_Door.DoorOpen
        public static bool InvisDoorDoorOpenPrefix(Building_Door __instance, int ticksToClose)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(InvisDoorDoorOpenPrefix));
            if (__instance is Building_DoorRegionHandler invisDoor)
            {
                invisDoor.ParentDoor?.DoorOpen(ticksToClose);
                return false;
            }
            return true;
        }

        // Building_Door.DoorTryClose
        public static bool InvisDoorDoorTryClosePrefix(Building_Door __instance)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(InvisDoorDoorTryClosePrefix));
            if (__instance is Building_DoorRegionHandler invisDoor)
            {
                invisDoor.ParentDoor?.DoorTryClose();
                return false;
            }
            return true;
        }

        // Building_Door.StartManualOpenBy
        public static bool InvisDoorStartManualOpenByPrefix(Building_Door __instance, Pawn opener)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(InvisDoorStartManualOpenByPrefix));
            if (__instance is Building_DoorRegionHandler w)
            {
                w.ParentDoor?.StartManualOpenBy(opener);
                return false;
            }
            return true;
        }

        // Building_Door.StartManualCloseBy
        // Note: Used to be a prefix patch, but changed into a method that's called within Pawn_PathFollower.TryEnterNextPathCell
        // (see next patch method), since Building_Door.StartManualCloseBy gets inlined.
        public static void InvisDoorStartManualCloseBy(Building_Door door, Pawn closer)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(InvisDoorStartManualCloseBy));
            if (door is Building_DoorRegionHandler invisDoor)
            {
                invisDoor.ParentDoor?.StartManualCloseBy(closer);
            }
            else
            {
                door.StartManualCloseBy(closer);
            }
        }

        // Pawn_PathFollower.TryEnterNextPathCell
        public static IEnumerable<CodeInstruction> InvisDoorManualCloseCallTranspiler(
            IEnumerable<CodeInstruction> instructions) =>
            instructions.MethodReplacer(
                AccessTools.Method(typeof(Building_Door), nameof(Building_Door.StartManualCloseBy)),
                AccessTools.Method(typeof(HarmonyPatches), nameof(InvisDoorStartManualCloseBy)));

        // GenGrid.CanBeSeenOver
        public static IEnumerable<CodeInstruction> InvisDoorCanBeSeenOverTranspiler(IEnumerable<CodeInstruction> instructions) =>
            GetActualDoorForDefTranspiler(instructions,
                AccessTools.Property(typeof(ThingDef), nameof(ThingDef.Fillage)).GetGetMethod());

        // FloodFillerFog.FloodUnfog
        // FogGrid.FloodUnfogAdjacent
        public static IEnumerable<CodeInstruction> InvisDoorMakeFogTranspiler(IEnumerable<CodeInstruction> instructions) =>
            GetActualDoorForDefTranspiler(instructions,
                AccessTools.Property(typeof(ThingDef), nameof(ThingDef.MakeFog)).GetGetMethod());

        // Projectile.ImpactSomething
        public static IEnumerable<CodeInstruction> InvisDoorProjectileImpactSomethingTranspiler(
            IEnumerable<CodeInstruction> instructions) =>
            GetActualDoorForDefTranspiler(instructions,
                AccessTools.Property(typeof(ThingDef), nameof(ThingDef.Fillage)).GetGetMethod());

        // SectionLayer_IndoorMask.HideRainPrimary
        public static IEnumerable<CodeInstruction> InvisDoorSectionLayerIndoorMaskHideRainPrimaryTranspiler(
            IEnumerable<CodeInstruction> instructions) =>
            GetActualDoorForDefTranspiler(instructions,
                AccessTools.Property(typeof(ThingDef), nameof(ThingDef.Fillage)).GetGetMethod());

        // SnowGrid.CanHaveSnow
        public static IEnumerable<CodeInstruction> InvisDoorCanHaveSnowTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            // Unlike the other thing.def.<defMember> transpilers, the accessing of the defMember Fillage happens
            // in another method, so we have to just replace thing.def with GetActualDoor(thing).def.
            foreach (var instruction in instructions)
            {
                if (instruction.LoadsField(fieldof_Thing_def))
                {
                    yield return new CodeInstruction(OpCodes.Call, methodof_GetActualDoor);
                }
                yield return instruction;
            }
        }

        // GlowFlooder.AddFloodGlowFor
        // SectionLayer_LightingOverlay.Regenerate
        public static IEnumerable<CodeInstruction> InvisDoorBlockLightTranspiler(IEnumerable<CodeInstruction> instructions) =>
            GetActualDoorForDefTranspiler(instructions, AccessTools.Field(typeof(ThingDef), nameof(ThingDef.blockLight)));

        private static IEnumerable<CodeInstruction> GetActualDoorForDefTranspiler(IEnumerable<CodeInstruction> instructions,
            MemberInfo defMember)
        {
            // This transforms the following code:
            //  thing.def.<defMember>
            // into:
            //  GetActualDoor(thing).def.<defMember>

            var enumerator = instructions.GetEnumerator();
            // There must be at least one instruction, so we can ignore first MoveNext() value.
            enumerator.MoveNext();
            var prevInstruction = enumerator.Current;
            while (enumerator.MoveNext())
            {
                var instruction = enumerator.Current;
                if (prevInstruction.LoadsField(fieldof_Thing_def) && instruction.OperandIs(defMember))
                {
                    yield return new CodeInstruction(OpCodes.Call, methodof_GetActualDoor);
                }
                yield return prevInstruction;
                prevInstruction = instruction;
            }
            yield return prevInstruction;
        }

        private static readonly FieldInfo fieldof_Thing_def = AccessTools.Field(typeof(Thing), nameof(Thing.def));
        private static readonly MethodInfo methodof_GetActualDoor =
            AccessTools.Method(typeof(HarmonyPatches), nameof(GetActualDoor));

        // PathGrid.CalculatedCostAt
        public static IEnumerable<CodeInstruction> InvisDoorCalculatedCostAtTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            // This removes the slowdown from consecutive invis doors of the same Building_DoorExpanded thing.
            // It keeps the slowdown from consecutive "actual" doors
            // (non-Building_DoorRegionhandler Building_Door or Building_DoorExpanded).
            // This transforms the following code:
            //  if (thing is Building_Door && prevCell.IsValid)
            //  {
            //      Building edifice = prevCell.GetEdifice(map);
            //      if (edifice != null && edifice is Building_Door)
            //      {
            //          consecutiveDoors = true;
            //      }
            //  }
            //  ...
            // into:
            //  if (thing is Building_Door && prevCell.IsValid)
            //  {
            //      Building edifice = prevCell.GetEdifice(map);
            //      if (edifice != null && edifice is Building_Door)
            //      {
            //          if (GetActualDoor(thing) != GetActualDoor(edifice))
            //              consecutiveDoors = true;
            //      }
            //  }
            //  ...

            var methodof_GetActualDoor = AccessTools.Method(typeof(HarmonyPatches), nameof(GetActualDoor));
            var instructionList = instructions.AsList();

            var searchIndex = 0;
            var firstDoor = GetIsinstDoorVar(instructionList, ref searchIndex);
            var secondDoor = GetIsinstDoorVar(instructionList, ref searchIndex);
            var condBranchToAfterFlagIndex = instructionList.FindIndex(searchIndex, instr => instr.operand is Label);
            var afterFlagLabel = (Label)instructionList[condBranchToAfterFlagIndex].operand;
            instructionList.InsertRange(condBranchToAfterFlagIndex + 1, new[]
            {
                new CodeInstruction(OpCodes.Ldloc_S, firstDoor),
                new CodeInstruction(OpCodes.Call, methodof_GetActualDoor),
                new CodeInstruction(OpCodes.Ldloc_S, secondDoor),
                new CodeInstruction(OpCodes.Call, methodof_GetActualDoor),
                new CodeInstruction(OpCodes.Beq, afterFlagLabel),
            });

            return instructionList;
        }

        // Get x from instruction sequence: ldloc.s <x>; isinst Building_Door.
        private static LocalBuilder GetIsinstDoorVar(List<CodeInstruction> instructions, ref int startIndex)
        {
            var isinstDoorIndex = instructions.FindIndex(startIndex, IsinstDoorInstruction);
            startIndex = isinstDoorIndex + 1;
            return (LocalBuilder)instructions[isinstDoorIndex - 1].operand;
        }

        private static bool IsinstDoorInstruction(CodeInstruction instruction) =>
            instruction.Is(OpCodes.Isinst, typeof(Building_Door));

        // GenSpawn.WipeExistingThings
        public static bool InvisDoorWipeExistingThingsPrefix(BuildableDef thingDef)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(InvisDoorWipeExistingThingsPrefix));
            // Only allow vanilla to run if this is not an invisible door.
            return !IsInvisDoorDef(thingDef);
        }

        // GenSpawn.SpawningWipes
        public static bool InvisDoorSpawningWipesPrefix(BuildableDef newEntDef, BuildableDef oldEntDef, ref bool __result)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(InvisDoorSpawningWipesPrefix));
            if (IsInvisDoorDef(newEntDef) || IsInvisDoorDef(oldEntDef))
            {
                __result = false; // false, meaning, don't wipe the old thing when you spawn
                return false;
            }
            return true;
        }

        // PathFinder.IsDestroyable
        public static bool InvisDoorIsDestroyablePostfix(bool result, Thing th)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(InvisDoorIsDestroyablePostfix));
            return result || th is Building_DoorRegionHandler;
        }

        // MouseoverReadout.MouseoverReadoutOnGUI
        public static IEnumerable<CodeInstruction> MouseoverReadoutTranspiler(IEnumerable<CodeInstruction> instructions,
            ILGenerator ilGen)
        {
            // This transpiler makes MouseoverReadout skip things with null or empty LabelMouseover.
            // In particular, this makes it skip invis doors, which have null LabelMouseover.
            // This transforms the following code:
            //  for (...)
            //  {
            //      var thing = ...;
            //      if (...)
            //      {
            //          var rect = ...;
            //          var labelMouseover = thing.LabelMouseover;
            //          Widgets.Label(rect, labelMouseover);
            //          y += YInterval;
            //      }
            //  }
            // into:
            //  for (...)
            //  {
            //      var thing = ...;
            //      if (...)
            //      {
            //          var rect = ...;
            //          var labelMouseover = thing.LabelMouseover;
            //          if (!labelMouseover.IsNullOrEmpty())
            //          {
            //              Widgets.Label(rect, labelMouseover);
            //              y += YInterval;
            //          }
            //      }
            //  }

            var methodof_Entity_get_LabelMouseover =
                AccessTools.Property(typeof(Entity), nameof(Entity.LabelMouseover)).GetGetMethod();
            var instructionList = instructions.AsList();

            var index = instructionList.FindIndex(instr => instr.Calls(methodof_Entity_get_LabelMouseover));
            // This relies on the fact that there's a conditional within the loop that acts as a loop continue,
            // and we're going to piggyback on that.
            var loopContinueLabelIndex = instructionList.FindIndex(index + 1, instr => instr.labels.Count > 0);
            var loopContinueLabel = instructionList[loopContinueLabelIndex].labels[0];
            // We can't simply do a brtrue to loopContinueLabel after the string.IsNullOrEmpty call,
            // since we need to pop off the LabelMouseover value from the CIL stack.
            // So we need a brfalse to the (current) next instruction, followed by a pop and then the br to loopContinueLabel.
            var nextInstructionLabel = ilGen.DefineLabel();
            instructionList[index + 1].labels.Add(nextInstructionLabel);
            instructionList.InsertRange(index + 1, new[]
            {
                new CodeInstruction(OpCodes.Dup),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(string), nameof(string.IsNullOrEmpty))),
                new CodeInstruction(OpCodes.Brfalse, nextInstructionLabel),
                new CodeInstruction(OpCodes.Pop),
                new CodeInstruction(OpCodes.Br, loopContinueLabel),
            });

            return instructionList;
        }

        // CoverUtility.BaseBlockChance
        public static IEnumerable<CodeInstruction> DoorExpandedBaseBlockChanceTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            // Since invis grid's fillPercent is 0, coverGrid should never contain them,
            // and CoverUtility.BaseBlockChance is only called on things in the coverGrid.
            // Rather, they'd contain the invis doors' parents that have >0.01 fillPercent,
            // so we need to handle door expanded doors.
            return DoorExpandedIsOpenDoorTranspiler(instructions);
        }

        // GenGrid.CanBeSeenOver
        public static IEnumerable<CodeInstruction> DoorExpandedCanBeSeenOverTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            // GenGrid.CanBeenSeenOver is only either called for edifices (never Building_DoorExpanded)
            // or by code that's responsible for setting dirty flags on caches. The latter should already be
            // done by such code also being called for invis doors, so this patch probably isn't necessary,
            // but better safe than sorry.
            return DoorExpandedIsOpenDoorTranspiler(instructions);
        }

        // EdificeGrid.Register
        public static bool DoorExpandedEdificeGridRegisterPrefix(Building ed)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(DoorExpandedEdificeGridRegisterPrefix));
            // Only the door expanded's invis doors are registered in the edifice grid,
            // not the parent door expanded itself.
            return !(ed is Building_DoorExpanded);
        }

        // ThingDef.IsDoor
        public static bool DoorExpandedThingDefIsDoorPostfix(bool result, ThingDef __instance)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(DoorExpandedThingDefIsDoorPostfix));
            // Treat Building_DoorExpanded as a door even if it doesn't derive from Building_Door.
            // This allows doors stats to appear for them, among other features.
            // However, because GhostUtility.GhostGraphicFor has hardcoded path for thingDef.IsDoor,
            // this prevents our DoorExpandedDrawGhostThingTranspiler from working properly.
            return result || IsDoorExpandedDef(__instance);
        }

        // CompForbiddable.Forbidden
        public static IEnumerable<CodeInstruction> DoorExpandedSetForbiddenTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (IsinstDoorInstruction(instruction))
                {
                    // CompForbiddable instance's parent is on top of CIL stack.
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(HarmonyPatches), nameof(DoorExpandedSetForbidden)));
                }
                yield return instruction;
            }
        }

        private static ThingWithComps DoorExpandedSetForbidden(ThingWithComps thing)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(DoorExpandedSetForbidden));
            if (thing is Building_DoorExpanded doorEx)
            {
                doorEx.Notify_ForbiddenInputChanged();
            }
            return thing;
        }

        // RegionAndRoomUpdater.ShouldBeInTheSameRoomGroup
        public static bool DoorExpandedShouldBeInTheSameRoomGroupPostfix(bool result, Room a, Room b)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(DoorExpandedShouldBeInTheSameRoomGroupPostfix));
            // All the invis doors each comprise a room.
            // They should all be combined into a single RoomGroup at least for the purposes of temperature management.
            if (result)
                return true;
            if (GetRoomDoor(a) is Building_DoorRegionHandler invisDoorA &&
                GetRoomDoor(b) is Building_DoorRegionHandler invisDoorB)
            {
                return invisDoorA.ParentDoor == invisDoorB.ParentDoor;
            }
            return false;
        }

        private static Building_Door GetRoomDoor(Room room)
        {
            if (!room.IsDoorway)
                return null;
            return room.Regions[0].door;
        }

        // GenTemperature.EqualizeTemperaturesThroughBuilding
        public static IEnumerable<CodeInstruction> DoorExpandedEqualizeTemperaturesThroughBuildingTranspiler(
            IEnumerable<CodeInstruction> instructions, ILGenerator ilGen)
        {
            // GenTemperature.EqualizeTemperaturesThroughBuildingTranspiler doesn't handle buildings that are larger than 1x1.
            // For the twoWay=false case (which is the one we care about), the algorithm for finding surrounding temperatures
            // only looks at the cardinal directions from the building's singular position cell.
            // We need it look at all cells surrounding the building's OccupiedRect, excluding corners.
            // This transforms the following code:
            //  int roomGroupCount = 0;
            //  float temperatureSum = 0f;
            //  if (twoWay)
            //  ...
            //  else
            //  {
            //      for (int j = 0; j < 4; j++)
            //      {
            //          IntVec3 c = b.Position + GenAdj.CardinalDirections[j];
            //          if (c.InBounds(b.Map))
            //          ...
            //      }
            //  }
            //  if (roomGroupCount == 0)
            //      return;
            // into:
            //  int roomGroupCount = 0;
            //  float temperatureSum = 0f;
            //  if (twoWay)
            //  ...
            //  else
            //  {
            //      IntVec3[] adjCells = GetAdjacentCellsForTemperature(b);
            //      for (int j = 0; j < adjCells.Length; j++)
            //      {
            //          IntVec3 c = adjCells[j];
            //          if (c.InBounds(b.Map))
            //          ...
            //      }
            //  }
            //  if (roomGroupCount == 0)
            //      return;

            var instructionList = instructions.AsList();
            var adjCellsVar = ilGen.DeclareLocal(typeof(IntVec3[]));
            //void DebugInstruction(string label, int index)
            //{
            //    Log.Message($"{label} @ {index}: " +
            //        ((index >= 0 && index < instructionList.Count) ? instructionList[index].ToString() : "invalid index"));
            //}

            var twoWayArgIndex = instructionList.FindIndex(instr => instr.opcode == OpCodes.Ldarg_2);
            // Assume the next brfalse(.s) operand is a label to the twoWay=false branch.
            var twoWayArgFalseBranchIndex = instructionList.FindIndex(twoWayArgIndex + 1,
                instr => instr.opcode == OpCodes.Brfalse || instr.opcode == OpCodes.Brfalse_S);
            var twoWayArgFalseLabel = (Label)instructionList[twoWayArgFalseBranchIndex].operand;
            var twoWayArgFalseIndex = instructionList.FindIndex(twoWayArgFalseBranchIndex + 1,
                instr => instr.labels.Contains(twoWayArgFalseLabel));
            // Assume next stloc.s is storing to the loop index var.
            var loopIndexIndex = instructionList.FindIndex(twoWayArgFalseIndex + 1,
                instr => instr.opcode == OpCodes.Stloc_S);
            var loopIndexVar = (LocalBuilder)instructionList[loopIndexIndex].operand;

            var newInstructions = new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0) // Building b
                { labels = instructionList[twoWayArgFalseIndex].labels.PopAll() },
                new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(HarmonyPatches), nameof(GetAdjacentCellsForTemperature))),
                new CodeInstruction(OpCodes.Starg_S, adjCellsVar),
            };
            instructionList.InsertRange(twoWayArgFalseIndex, newInstructions);

            var buildingArgIndex = instructionList.FindIndex(twoWayArgFalseIndex + newInstructions.Length,
                instr => instr.opcode == OpCodes.Ldarg_0);
            var currentCellStoreIndex = instructionList.FindIndex(buildingArgIndex + 1,
                instr => instr.opcode == OpCodes.Stloc_S);
            newInstructions = new[]
            {
                new CodeInstruction(OpCodes.Ldarg_S, adjCellsVar)
                { labels = instructionList[buildingArgIndex].labels.PopAll() },
                new CodeInstruction(OpCodes.Ldloc_S, loopIndexVar),
                new CodeInstruction(OpCodes.Ldelem, typeof(IntVec3)),
            };
            instructionList.ReplaceRange(buildingArgIndex, currentCellStoreIndex - buildingArgIndex, newInstructions);

            var loopEndIndexIndex = instructionList.FindIndex(buildingArgIndex + newInstructions.Length,
                instr => instr.opcode == OpCodes.Ldc_I4_4);
            instructionList.ReplaceRange(loopEndIndexIndex, 1, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_S, adjCellsVar),
                new CodeInstruction(OpCodes.Ldlen),
            });

            return instructionList;
        }

        private static IntVec3[] GetAdjacentCellsForTemperature(Building building)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(GetAdjacentCellsForTemperature));
            var size = building.def.Size;
            if (size.x == 1 && size.z == 1)
            {
                var position = building.Position;
                return new[]
                {
                    position + GenAdj.CardinalDirections[0],
                    position + GenAdj.CardinalDirections[1],
                    position + GenAdj.CardinalDirections[2],
                    position + GenAdj.CardinalDirections[3],
                };
            }
            else
            {
                var adjCells = GenAdj.CellsAdjacentCardinal(building).ToArray();
                // Ensure GenTemperature.beqRoomGroups is large enough.
                if (((RoomGroup[])fieldof_GenTemperature_beqRoomGroups.GetValue(null)).Length < adjCells.Length)
                {
                    fieldof_GenTemperature_beqRoomGroups.SetValue(null, new RoomGroup[adjCells.Length]);
                }
                return adjCells;
            }
        }

        private static readonly FieldInfo fieldof_GenTemperature_beqRoomGroups =
            AccessTools.Field(typeof(GenTemperature), "beqRoomGroups");

        // Projectile.CheckForFreeIntercept
        public static IEnumerable<CodeInstruction> DoorExpandedProjectileCheckForFreeIntercept(
            IEnumerable<CodeInstruction> instructions)
        {
            // Projectile.CheckForFreeIntercept has code that looks like:
            //  if (thing.def.Fillage == FillCategory.Full)
            //  {
            //      if (!(thing is Building_Door building_Door && building_Door.Open))
            //      ...
            // Invis doors have Fillage == FillCategory.None, while parent doors are not Building_Door.
            // So we need to patch the Building_Door check to work for Building_DoorExpanded.
            return DoorExpandedIsOpenDoorTranspiler(instructions);
        }

        // TrashUtility.TrashJob
        public static IEnumerable<CodeInstruction> DoorExpandedTrashJobTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            // This prevents enemies from setting Building_DoorExpanded's on fire.
            return DoorExpandedIsDoorTranspiler(instructions);
        }

        // Designator_Place.DoExtraGuiControls (internal lambda)
        // Designator_Place.HandleRotationShortcuts
        public static IEnumerable<CodeInstruction> DoorExpandedDesignatorPlaceRotateAgainIfNeededTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            // This transforms the following code:
            //  designatorPlace.placingRot.Rotate(rotDirection);
            // into:
            //  designatorPlace.placingRot.Rotate(rotDirection);
            //  DoorExpandedRotateAgainIfNeeded(designatorPlace, ref designatorPlace.placingRot, rotDirection);

            var fieldof_Designator_Place_placingRot = AccessTools.Field(typeof(Designator_Place), "placingRot");
            var methodof_Rot4_Rotate = AccessTools.Method(typeof(Rot4), nameof(Rot4.Rotate));
            var methodof_RotateAgainIfNeeded =
                AccessTools.Method(typeof(HarmonyPatches), nameof(DoorExpandedRotateAgainIfNeeded));
            var instructionList = instructions.AsList();

            var searchIndex = 0;
            var placingRotFieldIndex = instructionList.FindIndex(
                instr => instr.LoadsField(fieldof_Designator_Place_placingRot, byAddress: true));
            while (placingRotFieldIndex >= 0)
            {
                searchIndex = placingRotFieldIndex + 1;
                var rotateIndex = instructionList.FindIndex(searchIndex,
                    instr => instr.Calls(methodof_Rot4_Rotate));
                var nextPlacingRotFieldIndex = instructionList.FindIndex(searchIndex,
                    instr => instr.LoadsField(fieldof_Designator_Place_placingRot, byAddress: true));
                if (rotateIndex >= 0 && (nextPlacingRotFieldIndex < 0 || rotateIndex < nextPlacingRotFieldIndex))
                {
                    var replaceInstructions = new List<CodeInstruction>();
                    // Need copy the Designator_Place instance on top of CIL stack 2 times, in reverse order
                    // (due to stack popping):
                    // (2) placingRot field access for the original Rotate call
                    // (1) placingRot field access for 2nd arg to DoorExpandedRotateAgainIfNeeded call
                    // (0) instance itself for 1st arg to DoorExpandedRotateAgainIfNeeded call
                    replaceInstructions.AddRange(new[]
                    {
                        new CodeInstruction(OpCodes.Dup) { labels = instructionList[placingRotFieldIndex].labels.PopAll() },
                        new CodeInstruction(OpCodes.Dup),
                    });
                    // Copy original instructions from placingRot field access to Rotate call (uses up (2)).
                    var copiedRotateArgInstructions = instructionList.GetRange(placingRotFieldIndex,
                        rotateIndex - placingRotFieldIndex);
                    replaceInstructions.AddRange(copiedRotateArgInstructions);
                    replaceInstructions.Add(new CodeInstruction(OpCodes.Call, methodof_Rot4_Rotate));
                    // Call DoorExpandedRotateAgainIfNeeded with required arguments.
                    replaceInstructions.AddRange(copiedRotateArgInstructions); // uses up (1)
                    replaceInstructions.Add(new CodeInstruction(OpCodes.Call, methodof_RotateAgainIfNeeded)); // uses up (0)
                    instructionList.ReplaceRange(placingRotFieldIndex, rotateIndex - placingRotFieldIndex + 1,
                        replaceInstructions);
                    searchIndex += replaceInstructions.Count - 1;
                    nextPlacingRotFieldIndex = instructionList.FindIndex(searchIndex,
                        instr => instr.LoadsField(fieldof_Designator_Place_placingRot, byAddress: true));
                }
                placingRotFieldIndex = nextPlacingRotFieldIndex;
            }

            return instructionList;
        }

        private static void DoorExpandedRotateAgainIfNeeded(Designator_Place designatorPlace, ref Rot4 placingRot,
            RotationDirection rotDirection)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(DoorExpandedRotateAgainIfNeeded));
            if (placingRot == Rot4.South && designatorPlace.PlacingDef is DoorExpandedDef doorExDef && !doorExDef.rotatesSouth)
            {
                placingRot.Rotate(rotDirection);
            }
        }

        // GhostDrawer.DrawGhostThing
        public static IEnumerable<CodeInstruction> DoorExpandedDrawGhostThingTranspiler(
            IEnumerable<CodeInstruction> instructions) =>
            instructions.MethodReplacer(
                AccessTools.Method(typeof(Graphic), nameof(Graphic.DrawFromDef)),
                AccessTools.Method(typeof(HarmonyPatches), nameof(DoorExpandedDrawGhostGraphicFromDef)));

        private static void DoorExpandedDrawGhostGraphicFromDef(Graphic graphic, Vector3 loc, Rot4 rot, ThingDef thingDef,
            float extraRotation)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(DoorExpandedDrawGhostGraphicFromDef));
            if (thingDef is DoorExpandedDef doorExDef)
            {
                // Always delegate door expanded graphics to our custom code.
                for (var i = 0; i < 2; i++)
                {
                    Building_DoorExpanded.Draw(doorExDef, graphic.MatAt(rot), loc, rot, percentOpen: 0, flipped: i != 0);
                    if (doorExDef.singleDoor)
                        break;
                }
            }
            else
            {
                // extraRotation is always 0.
                graphic.DrawFromDef(loc, rot, thingDef, extraRotation);
            }
        }

        // GhostUtility.GhostGraphicFor
        public static IEnumerable<CodeInstruction> DoorExpandedGhostGraphicForTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            // One consequence of the patch to ThingDef.IsDoor to include door expanded defs is that
            // GhostUtility.GhostGraphicFor can now return a graphic that our patch for GhostDrawer.DrawGhostThing
            // doesn't work with (it returns a Graphic_Single based off thingDef.uiIconPath rather than Graphic_Multi
            // based off thingDef.graphic). So we need to patch GhostDrawer.DrawGhostThing as well.
            // For door expanded, we always want to return a Graphic_Multi based off thingDef.graphic.
            // This transforms the following code:
            //  if (... || thingDef.IsDoor)
            // into:
            //  if (... || (thingDef.IsDoor && !IsDoorExpandedDef(thingDef))

            var methodof_ThingDef_IsDoor = AccessTools.Property(typeof(ThingDef), nameof(ThingDef.IsDoor)).GetGetMethod();
            var instructionList = instructions.AsList();

            var isDoorIndex = instructionList.FindIndex(instr => instr.Calls(methodof_ThingDef_IsDoor));
            // Assume prev instruction is ldarg(.s) or ldloc(.s) for thingDef argument.
            var thingDefLoadInstruction = instructionList[isDoorIndex - 1];
            // Assume the next brfalse(.s) operand is a label that skips the Graphic_Single code path.
            var skipGraphicSingleBranchIndex = instructionList.FindIndex(isDoorIndex + 1,
                instr => instr.opcode == OpCodes.Brfalse || instr.opcode == OpCodes.Brfalse_S);
            var skipGraphicSingleLabel = (Label)instructionList[skipGraphicSingleBranchIndex].operand;
            instructionList.InsertRange(skipGraphicSingleBranchIndex + 1, new[]
            {
                thingDefLoadInstruction.Clone(),
                new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(HarmonyPatches), nameof(IsDoorExpandedDef))),
                new CodeInstruction(OpCodes.Brtrue, skipGraphicSingleLabel),
            });

            return instructionList;
        }

        // Blueprint.SpawnSetup
        public static void DoorExpandedBlueprintSpawnSetupPrefix(Blueprint __instance, Map map)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(DoorExpandedBlueprintSpawnSetupPrefix));
            ref var blueprint = ref __instance;
            // This needs to be a prefix (as opposed to a postfix), since Thing.SpawnSetup has logic which depends on
            // drawerType and rotation.
            if (blueprint.def.entityDefToBuild is DoorExpandedDef doorExDef)
            {
                // ThingDefGenerator_Buildings.NewBlueprintDef_Thing configures generated blueprint defs such that their
                // def.drawerType is MapMeshAndRealTime. This means that they have both a "update-when-needed" drawing that
                // calls the Print method (MapMesh), and an "update-on-tick" drawing that calls the Draw method (RealTime).
                // All our custom graphics for door expanded are done in the Draw method, so we must use RealTimeOnly mode.
                // For build blueprints, it special cases any def with Building_Door thingClass, such that their blueprint's
                // def.drawerType is RealtimeOnly. However, it doesn't special case def.drawerType for (re)install blueprints,
                // since they're not (re)installable by default.
                // We need this special casing for both build and (re)install blueprints of Building_DoorExpanded
                // (which doesn't inherit Building_Door), the latter is needed in case any door expanded are (re)installable.
                // This could be done in a harmony patch that is applied before ThingDefGenerator_Buildings runs
                // (must happen before StaticConstructorOnStartup) and would be more efficient, but it's easier to patch here
                // in SpawnSetup and Draw (see below) and any performance cost is negligible.
                blueprint.def.drawerType = DrawerType.RealtimeOnly;

                // Non-1x1 rotations change the footprint of the blueprint, so this needs to be done before that footprint
                // is cached in various ways in base.SpawnSetup, including in BlueprintGrid.
                // Fortunately once rotated, no further non-1x1 rotations will change the footprint further.
                blueprint.Rotation =
                    Building_DoorExpanded.DoorRotationAt(doorExDef, blueprint.Position, blueprint.Rotation, map);
            }
            else if (blueprint is Blueprint_Install && IsVanillaDoorDef(blueprint.def.entityDefToBuild))
            {
                // Since it's convenient to do so, we'll also "fix" (re)install blueprints for Building_Door thingClass,
                // in case another mod makes them (re)installable.
                blueprint.def.drawerType = DrawerType.RealtimeOnly;
            }
        }

        // Blueprint.Draw
        public static bool DoorExpandedBlueprintDrawPrefix(Blueprint __instance)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(DoorExpandedBlueprintDrawPrefix));
            ref var blueprint = ref __instance;
            if (blueprint.def.entityDefToBuild is DoorExpandedDef doorExDef)
            {
                // Always delegate door expanded graphics to our custom code.
                var drawPos = blueprint.DrawPos;
                var rotation = blueprint.Rotation;
                rotation = Building_DoorExpanded.DoorRotationAt(doorExDef, blueprint.Position, rotation, blueprint.Map);
                blueprint.Rotation = rotation;
                var material = blueprint.Graphic.MatAt(rotation);
                for (var i = 0; i < 2; i++)
                {
                    Building_DoorExpanded.Draw(doorExDef, material, drawPos, rotation, percentOpen: 0, flipped: i != 0);
                    if (doorExDef.singleDoor)
                        break;
                }
                Comps_PostDraw(blueprint, emptyObjArray);
                return false;
            }
            else if (blueprint is Blueprint_Install && IsVanillaDoorDef(blueprint.def.entityDefToBuild))
            {
                // Since it's convenient to do so, we'll also "fix" (re)install blueprints for Building_Door thingClass,
                // in case another mod makes them (re)installable.
                blueprint.Rotation = Building_Door.DoorRotationAt(blueprint.Position, blueprint.Map);
            }
            return true;
        }

        private static readonly FastInvokeHandler Comps_PostDraw =
            MethodInvoker.GetHandler(AccessTools.Method(typeof(ThingWithComps), "Comps_PostDraw"));
        private static readonly object[] emptyObjArray = new object[0];

        // Generic transpiler that transforms all following instances of code:
        //  thing is Building_Door door && door.Open
        // into:
        //  IsOpenDoor(thing)
        private static IEnumerable<CodeInstruction> DoorExpandedIsOpenDoorTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var instructionList = instructions.AsList();

            var searchIndex = 0;
            var isinstDoorIndex = instructionList.FindIndex(IsinstDoorInstruction);
            while (isinstDoorIndex >= 0)
            {
                searchIndex = isinstDoorIndex + 1;
                var doorOpenIndex = instructionList.FindIndex(searchIndex,
                    instr => instr.Calls(methodof_Building_Door_get_Open));
                var nextIsinstDoorIndex = instructionList.FindIndex(searchIndex, IsinstDoorInstruction);
                if (doorOpenIndex >= 0 && (nextIsinstDoorIndex < 0 || doorOpenIndex < nextIsinstDoorIndex))
                {
                    instructionList.ReplaceRange(isinstDoorIndex, doorOpenIndex - isinstDoorIndex + 1, new[]
                    {
                        new CodeInstruction(OpCodes.Call,
                            AccessTools.Method(typeof(HarmonyPatches), nameof(IsOpenDoor)))
                        { labels = instructionList[isinstDoorIndex].labels.PopAll() },
                    });
                    nextIsinstDoorIndex = instructionList.FindIndex(searchIndex, IsinstDoorInstruction);
                }
                isinstDoorIndex = nextIsinstDoorIndex;
            }

            return instructionList;
        }

        private static readonly MethodInfo methodof_Building_Door_get_Open =
            AccessTools.Property(typeof(Building_Door), nameof(Building_Door.Open)).GetGetMethod();

        private static bool IsOpenDoor(Thing thing) =>
            thing is Building_Door door && door.Open ||
            thing is Building_DoorExpanded doorEx && doorEx.Open;

        // Generic transpiler that transforms all following instances of code:
        //  thing is Building_Door
        // into:
        //  IsDoor(thing)
        private static IEnumerable<CodeInstruction> DoorExpandedIsDoorTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (IsinstDoorInstruction(instruction))
                {
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(HarmonyPatches), nameof(IsDoor)));
                }
                else
                {
                    yield return instruction;
                }
            }
        }

        private static bool IsDoor(Thing thing) => thing is Building_Door || thing is Building_DoorExpanded;
    }
}
