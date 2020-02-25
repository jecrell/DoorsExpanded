using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace DoorsExpanded
{
    [StaticConstructorOnStartup]
    static class HarmonyPatches
    {
        static HarmonyPatches()
        {
            harmony = HarmonyInstance.Create("rimworld.jecrell.doorsexpanded");

            Patch(original: AccessTools.Property(typeof(Building_Door), nameof(Building_Door.FreePassage)).GetGetMethod(),
                prefix: nameof(InvisDoorFreePassagePrefix));
            Patch(original: AccessTools.Method(typeof(Building_Door), nameof(Building_Door.Notify_PawnApproaching)),
                postfix: nameof(InvisDoorNotifyPawnApproachingPostfix));
            Patch(original: AccessTools.Method(typeof(Building_Door), nameof(Building_Door.CanPhysicallyPass)),
                prefix: nameof(InvisDoorCanPhysicallyPassPrefix));
            Patch(original: AccessTools.Method(typeof(Building_Door), "DoorOpen"),
                prefix: nameof(InvisDoorDoorOpenPrefix));
            Patch(original: AccessTools.Method(typeof(Building_Door), "DoorTryClose"),
                prefix: nameof(InvisDoorDoorTryClosePrefix));
            Patch(original: AccessTools.Method(typeof(Building_Door), nameof(Building_Door.StartManualOpenBy)),
                postfix: nameof(InvisDoorStartManualOpenByPostfix));
            Patch(original: AccessTools.Method(typeof(Building_Door), nameof(Building_Door.StartManualCloseBy)),
                prefix: nameof(InvisDoorStartManualCloseByPrefix));

            Patch(original: AccessTools.Method(typeof(GenSpawn), nameof(GenSpawn.SpawnBuildingAsPossible)),
                prefix: nameof(InvisDoorSpawnBuildingAsPossiblePrefix));
            Patch(original: AccessTools.Method(typeof(GenSpawn), nameof(GenSpawn.WipeExistingThings)),
                prefix: nameof(InvisDoorWipeExistingThingsPrefix));
            Patch(original: AccessTools.Method(typeof(GenSpawn), nameof(GenSpawn.SpawningWipes)),
                postfix: nameof(InvisDoorSpawningWipesPostfix));
            Patch(original: AccessTools.Method(typeof(PathFinder), nameof(PathFinder.IsDestroyable)),
                postfix: nameof(InvisDoorIsDestroyablePostfix));
            Patch(original: AccessTools.Method(typeof(ForbidUtility), nameof(ForbidUtility.IsForbiddenToPass)),
                postfix: nameof(InvisDoorIsForbiddenToPassPostfix));
            Patch(original: AccessTools.Method(typeof(CompForbiddable), nameof(CompForbiddable.PostDraw)),
                prefix: nameof(InvisDoorCompForbiddablePostDrawPrefix));

            Patch(original: AccessTools.Method(typeof(GenGrid), nameof(GenGrid.CanBeSeenOver), new[] { typeof(Building) }),
                postfix: nameof(DoorExpandedCanBeSeenOverPostfix));
            Patch(original: AccessTools.Method(typeof(GenPath), "ShouldNotEnterCell"),
                postfix: nameof(DoorExpandedShouldNotEnterCellPostfix));
            Patch(original: AccessTools.Method(typeof(EdificeGrid), nameof(EdificeGrid.Register)),
                prefix: nameof(DoorExpandedEdificeGridRegisterPrefix));
            Patch(original: AccessTools.Method(typeof(JobGiver_Manhunter), "TryGiveJob"),
                transpiler: nameof(DoorExpandedJobGiverManhunterTryGiveJobTranspiler));
            Patch(original: AccessTools.Method(typeof(PawnPathUtility), nameof(PawnPathUtility.TryFindLastCellBeforeBlockingDoor)),
                prefix: nameof(DoorExpandedTryFindLastCellBeforeBlockingDoorPrefix));
            Patch(original: AccessTools.Method(typeof(GhostDrawer), nameof(GhostDrawer.DrawGhostThing)),
                prefix: nameof(DoorExpandedDrawGhostThingPrefix));
        }

        private static readonly HarmonyInstance harmony;

        private static void Patch(MethodInfo original, string prefix = null, string postfix = null, string transpiler = null,
            bool harmonyDebug = false)
        {
            HarmonyInstance.DEBUG = harmonyDebug;
            try
            {
                harmony.Patch(original,
                    prefix == null ? null : new HarmonyMethod(typeof(HarmonyPatches), prefix),
                    postfix == null ? null : new HarmonyMethod(typeof(HarmonyPatches), postfix),
                    transpiler == null ? null : new HarmonyMethod(typeof(HarmonyPatches), transpiler));
            }
            finally
            {
                HarmonyInstance.DEBUG = false;
            }
        }

        // Building_Door.FreePassage
        public static bool InvisDoorFreePassagePrefix(Building_Door __instance, ref bool __result)
        {
            if (__instance is Building_DoorRegionHandler invisDoor && invisDoor.ParentDoor != null)
            {
                __result = invisDoor.ParentDoor.FreePassage;// && !invisDoor.ParentDoor.Forbidden;
                return false;
            }

            return true;
        }

        // Building_Door.Notify_PawnApproaching
        public static void InvisDoorNotifyPawnApproachingPostfix(Building_Door __instance, Pawn p)
        {
            if (__instance is Building_DoorRegionHandler invisDoor)
            {
                invisDoor.ParentDoor?.Notify_PawnApproaching(p);
            }
        }

        // Building_Door.CanPhysicallyPass
        public static bool InvisDoorCanPhysicallyPassPrefix(Building_Door __instance, Pawn p, ref bool __result)
        {
            bool pawnCanOpen;
            if (__instance is Building_DoorRegionHandler invisDoor)
            {
                pawnCanOpen = invisDoor.PawnCanOpen(p);
                if (invisDoor.ParentDoor is Building_DoorRemote rem && rem.RemoteState == DoorRemote_State.ForcedClose)
                {
                    __result = false;
                    return false;
                }
            }
            else
            {
                pawnCanOpen = __instance.PawnCanOpen(p);
            }
            __result = __instance.FreePassage || pawnCanOpen || (__instance.Open && p.HostileTo(__instance));
            return false;
        }

        // Building_Door.DoorOpen
        public static bool InvisDoorDoorOpenPrefix(Building_Door __instance, int ticksToClose)
        {
            if (__instance is Building_DoorRegionHandler invisDoor)
            {
                Traverse.Create(__instance).Field("ticksUntilClose").SetValue(ticksToClose);
                if (!Traverse.Create(__instance).Field("openInt").GetValue<bool>())
                {
                    AccessTools.Field(typeof(Building_Door), "openInt").SetValue(__instance, true);
                }

                invisDoor.ParentDoor.DoorOpen(ticksToClose);
                return false;
            }

            return true;
        }

        // Building_Door.DoorTryClose
        public static bool InvisDoorDoorTryClosePrefix(Building_Door __instance)
        {
            if (__instance is Building_DoorRegionHandler invisDoor)
            {
                //invisDoor.ParentDoor.DoorTryClose();
                if (!Traverse.Create(__instance).Field("holdOpenInt").GetValue<bool>() ||
                    __instance.BlockedOpenMomentary || invisDoor.ParentDoor.Open)
                {
                    return false;
                }
                //AccessTools.Field(typeof(Building_Door), "openInt").SetValue(__instance, false);
                return false;
            }

            return true;
        }

        // Building_Door.StartManualOpenBy
        public static void InvisDoorStartManualOpenByPostfix(Building_Door __instance, Pawn opener)
        {
            if (__instance is Building_DoorRegionHandler invisDoor)
            {
                if (invisDoor.ParentDoor.PawnCanOpen(opener))
                {
                    invisDoor.ParentDoor.StartManualOpenBy(opener);
                    if (invisDoor.ParentDoor.InvisDoors.ToList().FindAll(otherDoor => otherDoor != __instance) is
                            List<Building_DoorRegionHandler> otherDoors && !otherDoors.NullOrEmpty())
                    {
                        foreach (var otherDoor in otherDoors)
                        {
                            if (!otherDoor.Open)
                            {
                                var drawSize = invisDoor.ParentDoor.Graphic.drawSize;
                                var math = (int)(1200 * Math.Max(drawSize.x, drawSize.y));
                                Traverse.Create(otherDoor).Field("ticksUntilClose").SetValue(math);
                                Traverse.Create(otherDoor).Field("openInt").SetValue(true);
                            }
                        }
                    }
                }
            }
        }

        // Building_Door.StartManualCloseBy
        public static bool InvisDoorStartManualCloseByPrefix(Building_Door __instance, Pawn closer)
        {
            if (__instance is Building_DoorRegionHandler invisDoor)
            {
                //invisDoor.ParentDoor.StartManualCloseBy(closer);
                return false;
            }

            return true;
        }

        // GenSpawn.SpawnBuildingAsPossible
        public static bool InvisDoorSpawnBuildingAsPossiblePrefix(Building building, Map map, bool respawningAfterLoad = false)
        {
            // TODO: Redundant much?
            if (building is Building_DoorExpanded ||
                building is Building_DoorRegionHandler ||
                building.def == HeronDefOf.HeronInvisibleDoor ||
                building.def.thingClass == typeof(Building_DoorRegionHandler))
            {
                GenSpawn.Spawn(building, building.Position, map, building.Rotation, WipeMode.Vanish, respawningAfterLoad);
                return false;
            }

            return true;
        }

        // GenSpawn.WipeExistingThings
        public static bool InvisDoorWipeExistingThingsPrefix(BuildableDef thingDef)
        {
            // Allow vanilla to run if this is not an invisible door.
            return thingDef.defName != HeronDefOf.HeronInvisibleDoor.defName
                   && thingDef != HeronDefOf.HeronInvisibleDoor;
        }

        // GenSpawn.SpawningWipes
        public static void InvisDoorSpawningWipesPostfix(BuildableDef newEntDef, BuildableDef oldEntDef, ref bool __result)
        {
            // TODO: Remove this Combat Extended workaround when DefDatabase<ThingDef>.GetNamed calls below are removed.
            if (newEntDef.defName.StartsWith("Fragment_") &&
                oldEntDef.defName.StartsWith("Fragment_"))
            {
                return;
            }

            if (newEntDef.defName == HeronDefOf.HeronInvisibleDoor.defName ||
                oldEntDef.defName == HeronDefOf.HeronInvisibleDoor.defName)
            {
                __result = false; //false, meaning, don't wipe the old thing when you spawn
                return;
            }

            // TODO: All the following is redundant and useless, plus DefDatabase<ThingDef>.GetNamed is slow.

            if (newEntDef.defName == HeronDefOf.HeronInvisibleDoor.defName &&
                oldEntDef.defName == HeronDefOf.HeronInvisibleDoor.defName)
            {
                __result = true;
                return;
            }

            var oldTrueDef = DefDatabase<ThingDef>.GetNamed(oldEntDef.defName);
            var newTrueDef = DefDatabase<ThingDef>.GetNamed(newEntDef.defName);

            if (newTrueDef != null && newTrueDef.thingClass == typeof(Building_DoorExpanded) &&
                oldTrueDef != null && oldTrueDef.thingClass == typeof(Building_DoorExpanded))
            {
                __result = true;
                return;
            }

            if (oldTrueDef != null && oldTrueDef.thingClass == typeof(Building_DoorExpanded) &&
                newEntDef.defName == HeronDefOf.HeronInvisibleDoor.defName)
            {
                __result = false;
                return;
            }

            if (newTrueDef != null && newTrueDef.thingClass == typeof(Building_DoorExpanded) &&
                oldEntDef.defName == HeronDefOf.HeronInvisibleDoor.defName)
            {
                __result = false;
                return;
            }
        }

        // PathFinder.IsDestroyable
        public static void InvisDoorIsDestroyablePostfix(Thing th, ref bool __result)
        {
            __result = __result && th is Building_DoorRegionHandler;
        }

        // ForbidUtility.IsForbiddenToPass
        public static void InvisDoorIsForbiddenToPassPostfix(this Thing t, Pawn pawn, ref bool __result)
        {
            if (t is Building_DoorRegionHandler invisDoor)
            {
                var tPositionIsForbidden_withoutLocationCheck = ForbidUtility.CaresAboutForbidden(pawn, true) && pawn.mindState.maxDistToSquadFlag > 0f && !t.Position.InHorDistOf(pawn.DutyLocation(), pawn.mindState.maxDistToSquadFlag);

                __result =
                    (t.Spawned && tPositionIsForbidden_withoutLocationCheck)
                    || t.IsForbidden(pawn.Faction)
                    || pawn.HostileTo(t) && !invisDoor.Open ||
                    (invisDoor.ParentDoor is Building_DoorRemote r && r.RemoteState == DoorRemote_State.ForcedClose);
            }
        }

        // CompForbiddable.PostDraw
        public static bool InvisDoorCompForbiddablePostDrawPrefix(CompForbiddable __instance)
        {
            if (__instance.parent is Building_DoorRegionHandler)
                return false;
            return true;
        }

        // GenGrid.CanBeSeenOver
        public static void DoorExpandedCanBeSeenOverPostfix(Building b, ref bool __result)
        {
            //Ignores fillage
            if (b is Building_DoorExpanded doorEx)
            {
                __result = doorEx != null && doorEx.Open;
            }
        }

        // GenPath.ShouldNotEnterCell
        public static void DoorExpandedShouldNotEnterCellPostfix(Pawn pawn, Map map, IntVec3 dest, ref bool __result)
        {
            if (__result || pawn == null)
                return;
            if (map.pathGrid.PerceivedPathCostAt(dest) > 30)
            {
                __result = true;
                return;
            }

            if (!dest.Walkable(map))
            {
                __result = true;
                return;
            }

            var edifice = dest.GetEdifice(map);
            if (edifice == null)
            {
                //Log.Message("No edifice. So let's go!");
                return;
            }

            if (edifice is Building_DoorExpanded doorEx)
            {
                if (doorEx.IsForbidden(pawn))
                {
                    __result = true;
                    return;
                }

                if (!doorEx.PawnCanOpen(pawn))
                {
                    __result = true;
                    return;
                }
            }

            if (edifice is Building_DoorRegionHandler invisDoor)
            {
                if (invisDoor.IsForbidden(pawn))
                {
                    __result = true;
                    return;
                }

                if (!invisDoor.PawnCanOpen(pawn))
                {
                    __result = true;
                }
            }
        }

        // EdificeGrid.Register
        // TODO Make transpiler
        public static bool DoorExpandedEdificeGridRegisterPrefix(EdificeGrid __instance, Building ed)
        {
            if (IsExceptionForEdificeRegistration(ed))
            {
                //<VanillaCodeSequence>
                var cellIndices = Traverse.Create(__instance).Field("map").GetValue<Map>().cellIndices;
                var cellRect = ed.OccupiedRect();
                for (var i = cellRect.minZ; i <= cellRect.maxZ; i++)
                {
                    for (var j = cellRect.minX; j <= cellRect.maxX; j++)
                    {
                        var intVec = new IntVec3(j, 0, i);
                        var oldBuilding = __instance[intVec];
                        if (UnityData.isDebugBuild && oldBuilding != null && !oldBuilding.Destroyed &&
                            !IsExceptionForEdificeRegistration(oldBuilding))
                        {
                            Log.Error("Added edifice " + ed.LabelCap + " over edifice " + oldBuilding.LabelCap + " at " + intVec +
                                ". Destroying old edifice, despite DoorsExpanded code.");
                            oldBuilding.Destroy(DestroyMode.Vanish);
                            return false;
                        }

                        Traverse.Create(__instance).Field("innerArray").GetValue<Building[]>()[
                            cellIndices.CellToIndex(intVec)] = ed;
                    }
                }
                //</VanillaCodeSequence>
                return false;
            }

            return true;
        }

        private static bool IsExceptionForEdificeRegistration(Building ed)
        {
            // TODO: This should be typeof(Building_DoorExpanded).IsAssignableFrom(ed.def.thingClass), etc.
            return ed.def.thingClass.IsAssignableFrom(typeof(Building_DoorExpanded)) ||
                   ed.def.thingClass.IsAssignableFrom(typeof(Building_DoorRegionHandler)) ||
                   ed.def.thingClass == typeof(Building_DoorRegionHandler) ||
                   ed.def.thingClass == typeof(Building_DoorExpanded);
        }

        // JobGiver_Manhunter.TryGiveJob
        // Removes logged error if PawnPathUtility.TryFindLastCellBeforeBlockingDoor returns false.
        public static IEnumerable<CodeInstruction> DoorExpandedJobGiverManhunterTryGiveJobTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var instructionList = instructions.ToList();

            var postureInfo = AccessTools.Method(typeof(Log), nameof(Log.Error));

            var indexNum = 0;

            for (var index = 0; index < instructionList.Count; index++)
            {
                var instruction = instructionList[index];

                if (instruction.opcode == OpCodes.Call && instruction.operand == postureInfo)
                {
                    indexNum = index;
                    break;
                }
            }
            instructionList.RemoveRange(indexNum - 6, 7);
            foreach (var ins in instructionList)
                yield return ins;
        }

        // PawnPathUtility.TryFindLastCellBeforeBlockingDoor
        // Adds an extra check for manhunters.
        public static bool DoorExpandedTryFindLastCellBeforeBlockingDoorPrefix(PawnPath path, Pawn pawn, ref IntVec3 result, ref bool __result)
        {
            if (path?.NodesReversed?.Count == 1)
            {
                result = path.NodesReversed[0];
                __result = false;
                //Log.Message("Nodes less or equal to 1");
                return false;
            }

            var nodesReversed = path.NodesReversed;
            if (nodesReversed != null)
            {
                for (var i = nodesReversed.Count - 2; i >= 1; i--)
                {
                    //pawn.Map.debugDrawer.FlashCell(nodesReversed[i]);
                    //var edifice = nodesReversed[i].GetEdifice(pawn.Map);
                    var edifice = nodesReversed[i].GetThingList(pawn.Map)
                        .FirstOrDefault(x =>
                            x.def.thingClass == typeof(Building_DoorExpanded) ||
                            x.def.thingClass == typeof(Building_DoorRegionHandler));

                    if (edifice is Building_DoorExpanded doorEx)
                    {
                        if (!doorEx?.InvisDoors?.Any(x => !x.CanPhysicallyPass(pawn) && !x.PawnCanOpen(pawn)) ??
                            false)
                        {
                            //Log.Message("DoorsExpanded :: Manhunter Check Passed (doorExpanded)");
                            result = nodesReversed[i + 1];
                            __result = true;
                            return false;
                        }
                    }

                    if (edifice is Building_DoorRegionHandler invisDoor)
                    {
                        if (!invisDoor.CanPhysicallyPass(pawn))
                        {
                            //Log.Message("DoorsExpanded :: Manhunter Check Passed (doorRegionHandler) (CanPhysicallyPass)");
                            result = nodesReversed[i + 1];
                            __result = true;
                            return false;
                        }
                        if (!invisDoor.PawnCanOpen(pawn))
                        {
                            //Log.Message("DoorsExpanded :: Manhunter Check Passed (doorRegionHandler) (PawnCanOpen)");
                            result = nodesReversed[i + 1];
                            __result = true;
                            return false;
                        }
                    }
                }

                //Log.Message("No objects detected in path");
                result = nodesReversed[0];
            }

            __result = false;
            return true;
        }

        // GhostDrawer.DrawGhostThing
        public static bool DoorExpandedDrawGhostThingPrefix(IntVec3 center, Rot4 rot, ThingDef thingDef, Graphic baseGraphic,
            Color ghostCol, AltitudeLayer drawAltitude)
        {
            if (thingDef is DoorExpandedDef doorExDef && doorExDef.fixedPerspective)
            {
                var graphic = GhostUtility.GhostGraphicFor(baseGraphic, thingDef, ghostCol);
                var loc = GenThing.TrueCenter(center, rot, thingDef.Size, drawAltitude.AltitudeFor());

                for (var i = 0; i < 2; i++)
                {
                    Building_DoorExpanded.DrawParams(doorExDef, loc, rot, out var mesh, out var matrix, mod: 0, flipped: i != 0);
                    Graphics.DrawMesh(mesh, matrix, graphic.MatAt(rot), layer: 0);
                }

                if (thingDef?.PlaceWorkers?.Count > 0)
                {
                    for (var i = 0; i < thingDef.PlaceWorkers.Count; i++)
                    {
                        thingDef.PlaceWorkers[i].DrawGhost(thingDef, center, rot, ghostCol);
                    }
                }

                return false;
            }

            return true;
        }
    }
}
