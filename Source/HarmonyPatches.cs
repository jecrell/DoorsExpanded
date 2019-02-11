using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
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
            HarmonyInstance harmony = HarmonyInstance.Create(id: "rimworld.jecrell.doorsexpanded");

            // What?: Reduce movement penalty for moving through expanded doors.
            // Why?: Movement penalty while crossing bulky doors is frustrating to players.
            // How? Patches Verse.CostToMoveIntoCell(Pawn pawn, IntVec3 c)
            harmony.Patch(original:
                AccessTools.Method(
                    type: typeof(Pawn_PathFollower), 
                    name: "CostToMoveIntoCell", 
                    parameters: new Type[] { typeof(Pawn), typeof(IntVec3) }),
                    prefix: null,
                    postfix:
                        new HarmonyMethod(
                        type: typeof(HarmonyPatches),
                        name: nameof(CostToMoveIntoCell_PostFix_ChangeDoorPathCost)
                        )
            );

            harmony.Patch(original: AccessTools.Method(type: typeof(EdificeGrid), name: "Register"),
                prefix: new HarmonyMethod(
                    type: typeof(HarmonyPatches),
                    name: nameof(RegisterDoorExpanded)), postfix: null);
            harmony.Patch(original: AccessTools.Method(type: typeof(Building_Door), name: "DoorOpen"),
                prefix: new HarmonyMethod(
                    type: typeof(HarmonyPatches),
                    name: nameof(InvisDoorOpen)), postfix: null);
            harmony.Patch(original: AccessTools.Method(type: typeof(Building_Door), name: "DoorTryClose"),
                prefix: new HarmonyMethod(
                    type: typeof(HarmonyPatches),
                    name: nameof(InvisDoorTryClose)), postfix: null);
            harmony.Patch(original: AccessTools.Method(type: typeof(Building_Door), name: "Notify_PawnApproaching"),
                prefix: null, postfix: new HarmonyMethod(
                    type: typeof(HarmonyPatches),
                    name: nameof(InvisDoorNotifyApproaching)), transpiler: null);
            harmony.Patch(
                original: AccessTools.Method(type: typeof(Building_Door),
                    name: nameof(Building_Door.StartManualCloseBy)),
                prefix: new HarmonyMethod(type: typeof(HarmonyPatches),
                    name: nameof(InvisDoorManualClose)), postfix: null);
            harmony.Patch(
                original: AccessTools.Method(type: typeof(Building_Door),
                    name: nameof(Building_Door.StartManualOpenBy)), prefix: null,
                postfix: new HarmonyMethod(type: typeof(HarmonyPatches),
                    name: nameof(InvisDoorManualOpen)), transpiler: null);
            harmony.Patch(
                original: AccessTools.Property(type: typeof(Building_Door), name: nameof(Building_Door.FreePassage))
                    .GetGetMethod(),
                prefix: new HarmonyMethod(type: typeof(HarmonyPatches),
                    name: nameof(get_FreePassage)), postfix: null);
            harmony.Patch(
                original: AccessTools.Method(type: typeof(GhostDrawer), name: nameof(GhostDrawer.DrawGhostThing)),
                prefix: new HarmonyMethod(type: typeof(HarmonyPatches),
                    name: nameof(HeronDoorGhostHandler)), postfix: null);
            harmony.Patch(
                original: AccessTools.Method(type: typeof(GenSpawn), name: nameof(GenSpawn.SpawnBuildingAsPossible)),
                prefix: new HarmonyMethod(type: typeof(HarmonyPatches),
                    name: nameof(HeronSpawnBuildingAsPossible)), postfix: null);
            harmony.Patch(
                original: AccessTools.Method(type: typeof(GenSpawn), name: nameof(GenSpawn.WipeExistingThings)),
                prefix: new HarmonyMethod(
                    type: typeof(HarmonyPatches),
                    name: nameof(WipeExistingThings)), postfix: null);
            harmony.Patch(original: AccessTools.Method(type: typeof(GenSpawn), name: nameof(GenSpawn.SpawningWipes)),
                prefix: null, postfix: new HarmonyMethod(
                    type: typeof(HarmonyPatches),
                    name: nameof(InvisDoorsDontWipe)), transpiler: null);
            harmony.Patch(original: AccessTools.Method(type: typeof(GenPath), name: "ShouldNotEnterCell"), prefix: null,
                postfix: new HarmonyMethod(
                    type: typeof(HarmonyPatches),
                    name: nameof(ShouldNotEnterCellInvisDoors)), transpiler: null);
            harmony.Patch(
                original: AccessTools.Method(type: typeof(CompForbiddable), name: nameof(CompForbiddable.PostDraw)),
                prefix: new HarmonyMethod(type: typeof(HarmonyPatches),
                    name: nameof(DontDrawInvisDoorForbiddenIcons)), postfix: null);
            harmony.Patch(
                original: AccessTools.Method(type: typeof(PawnPathUtility),
                    name: nameof(PawnPathUtility.TryFindLastCellBeforeBlockingDoor)),
                prefix: new HarmonyMethod(type: typeof(HarmonyPatches),
                    name: nameof(ManhunterJobGiverFix)), postfix: null);
            harmony.Patch(
                original: AccessTools.Method(type: typeof(ForbidUtility),
                    name: nameof(ForbidUtility.IsForbiddenToPass)),
                prefix: null, postfix: new HarmonyMethod(type: typeof(HarmonyPatches),
                    name: nameof(IsForbiddenToPass_PostFix)));
            
            harmony.Patch(
                original: AccessTools.Method(type: typeof(PathFinder), name: nameof(PathFinder.GetBuildingCost)),
                prefix: null, postfix: new HarmonyMethod(type: typeof(HarmonyPatches),
                    name: nameof(GetBuildingCost_PostFix)));
            harmony.Patch(
                original: AccessTools.Method(type: typeof(PawnPathUtility),
                    name: nameof(PawnPathUtility.FirstBlockingBuilding)),
                prefix: null, postfix: new HarmonyMethod(type: typeof(HarmonyPatches),
                    name: nameof(FirstBlockingBuilding_PostFix)));
            harmony.Patch(
                original: AccessTools.Method(typeof(GenGrid), "CanBeSeenOver", new[] {typeof(Building)}),
                prefix: null, postfix: new HarmonyMethod(type: typeof(HarmonyPatches),
                    name: nameof(CanBeSeenOver)));
            
            harmony.Patch(
                AccessTools.Method(typeof(JobGiver_Manhunter), "TryGiveJob"),
                null, null, new HarmonyMethod(type: typeof(HarmonyPatches),
                    name: nameof(JobGiver_Manhunter_TryGiveJob_Transpiler)));
            //harmony.Patch(
            //    AccessTools.Method(typeof(JobGiver_SeekAllowedArea), "TryGiveJob"),
            //    new HarmonyMethod(type: typeof(HarmonyPatches),
            //        name: nameof(SeekAllowedArea_TryGiveJob)), null);
            harmony.Patch(
                AccessTools.Method(typeof(Building_Door), "CanPhysicallyPass"),
                new HarmonyMethod(type: typeof(HarmonyPatches),
                    name: nameof(CanPhysicallyPass)), null);
            //harmony.Patch(
            //    original: AccessTools.Method(typeof(Region), "Allows"),
            //    prefix: null, postfix: new HarmonyMethod(type: typeof(HarmonyPatches),
            //        name: nameof(RegionAllows)));
        }

        public static void CostToMoveIntoCell_PostFix_ChangeDoorPathCost(Pawn_PathFollower __instance, Pawn pawn, IntVec3 c, ref int __result)
        {
            //Edge cases
            var curMap = pawn.MapHeld;
            if (curMap == null) return;

            var curMapGridForDoors = curMap.GetComponent<MapGrid_DoorsExpanded>();
            if (curMapGridForDoors == null) return;

            //Method
            if (curMapGridForDoors.HasDoorAtLocation(c))
            {
                int adjustedPathCost = (int)(__result * 0.5f);
                //Log.Message($"DoorsExpanded: Newcost: {adjustedPathCost} - Oldcost: {__result}");
                __result = Mathf.Max(adjustedPathCost, 1);
                
            }
        }

        //JobGiver_Manhunter.TryGiveJob
        public static IEnumerable<CodeInstruction> JobGiver_Manhunter_TryGiveJob_Transpiler(
            IEnumerable<CodeInstruction> instructions, ILGenerator ilg)
        {
            List<CodeInstruction> instructionList = instructions.ToList();

            MethodInfo postureInfo =
                AccessTools.Method(type: typeof(Log), name: nameof(Log.Error));

            var indexNum = 0;
            
            for (int index = 0; index < instructionList.Count; index++)
            {
                CodeInstruction instruction = instructionList[index: index];

                if (instruction.opcode == OpCodes.Call && instruction.operand == postureInfo)
                {
                    Log.Message("Removed Log Error from Manhunter Job");
                    indexNum = index;
                    break;
                    //instruction = new CodeInstruction(OpCodes.Call, operand: AccessTools.Method(typeof(Log), "Warning"));
                }
                //yield return instruction;
            }
            instructionList.RemoveRange(indexNum - 6, 7);
            foreach (var ins in instructionList)
                yield return ins;
        }
        
        

        public static bool CanPhysicallyPass(Building_Door __instance, Pawn p, ref bool __result)
        {
            if (!p.AnimalOrWildMan()) return true;
            if (p.playerSettings == null) return true;
            //StringBuilder s = new StringBuilder();
            //s.AppendLine(p.LabelShort + " - FreePassage: " + __instance.FreePassage);
            var pawnCanOpen = (__instance is Building_DoorRegionHandler reg) ? reg.PawnCanOpen(p) : __instance.PawnCanOpen(p);
            //s.AppendLine(p.LabelShort + " - PawnCanOpen: " + pawnCanOpen);
            //s.AppendLine((p.LabelShort + " - Open: " + __instance.Open));
            //s.AppendLine((p.LabelShort + " - Hostile: " + p.HostileTo(__instance)));
            //Log.Message(s.ToString());
            __result = __instance.FreePassage || pawnCanOpen || (__instance.Open && p.HostileTo(__instance));
            return false; 
        }

        //public class JobGiver_SeekAllowedArea : ThinkNode_JobGiver
        public static bool SeekAllowedArea_TryGiveJob(JobGiver_SeekAllowedArea __instance, Pawn pawn, ref Job __result)
        {
            if (!pawn.AnimalOrWildMan()) return true;
            if (pawn.playerSettings == null) return true;
            StringBuilder res = new StringBuilder();
            if (!pawn.Position.IsForbidden(pawn))
            {
                __result = null;
                return false;
            }

            if (Traverse.Create(__instance).Method("HasJobWithSpawnedAllowedTarget", pawn).GetValue<bool>())
            {
                __result = null;
                return false;
            }

            Region region = pawn.GetRegion(RegionType.Set_Passable);
            if (region == null)
            {
                __result = null;
                return false;
            }

            var allows = false;
            TraverseParms traverseParms = TraverseParms.For(pawn, Danger.Deadly, TraverseMode.ByPawn, false);
            RegionEntryPredicate entryCondition = (Region from, Region r) =>
            {
                allows = r.Allows(traverseParms, false);
                if (allows)
                {
                    return true;
                }

                Log.Message("Entry barred");
                if (r.door == null)
                {
                    Log.Message("Door Null");
                    Log.Message("Return True");
                }
                ByteGrid avoidGrid = traverseParms.pawn.GetAvoidGrid(true);
                if (avoidGrid != null && avoidGrid[r.door.Position] == 255)
                {
                    Log.Message("Door Position == 255");

                    Log.Message("Return False");
                }
                if (traverseParms.pawn.HostileTo(r.door))
                {
                    Log.Message("Door Position == 255");
                    Log.Message("CanPhysicallyPass: " + r.door.CanPhysicallyPass(traverseParms.pawn).ToString());
                    Log.Message("CanBash: " + traverseParms.canBash.ToString());
                }
                Log.Message("door.CanPhysicallyPass(tp.pawn) == " + r.door.CanPhysicallyPass(traverseParms.pawn));
                Log.Message("!r.door.IsForbiddenToPass(traverseParms.pawn); == " + !r.door.IsForbiddenToPass(traverseParms.pawn));
                Log.Message("Return " + (r.door.CanPhysicallyPass(traverseParms.pawn) && !r.door.IsForbiddenToPass(traverseParms.pawn)));
                //return this.door.CanPhysicallyPass(tp.pawn) && !this.door.IsForbiddenToPass(tp.pawn);
                return false;
            };
            Region reg = null;
            RegionProcessor regionProcessor = delegate(Region r)
            {
                if (r.IsDoorway && r?.ListerThings?.AllThings?.Any(x => x is Building_DoorRegionHandler) == false)
                {
                    Log.Message("Doorway disallowed");
                    return false;
                }

                if (!r.IsForbiddenEntirely(pawn))
                {
                    reg = r;
                    return true;
                }

                return false;
            };
            RegionTraverser.BreadthFirstTraverse(region, entryCondition, regionProcessor, 9999,
                RegionType.Set_Passable);
            if (reg == null)
            {
                Log.Message(pawn.LabelShort + " No region found");
                __result = null;
                return false;
            }

            IntVec3 c;
            if (!reg.TryFindRandomCellInRegionUnforbidden(pawn, null, out c))
            {
                Log.Message(pawn.LabelShort + " Failed to find random cell in region unforbidden");
                __result = null;
                return false;
            }

            __result = new Job(JobDefOf.Goto, c);
            return false;
        }

        //Region
        //public bool RegionAllows(TraverseParms tp, bool isDestination)
        //{
        //    
        //}

        //GenGrid
        public static void CanBeSeenOver(Building b, ref bool __result)
        {
            //Ignores fillage
            Building_DoorExpanded building_DoorEx = b as Building_DoorExpanded;
            if (building_DoorEx != null)
            {
                __result = building_DoorEx != null && building_DoorEx.Open;
            }
        }

        //public static class PawnPathUtility
        public static void FirstBlockingBuilding_PostFix(this PawnPath path, ref IntVec3 cellBefore, Pawn pawn,
            ref Thing __result)
        {
            if (!path.Found)
            {
                cellBefore = IntVec3.Invalid;
                __result = null;
            }

            List<IntVec3> nodesReversed = path.NodesReversed;
            if (nodesReversed.NullOrEmpty() || nodesReversed.Count <= 1)
            {
                return;
            }

            Building building = null;
            IntVec3 intVec = IntVec3.Invalid;
            for (int i = nodesReversed.Count - 2; i >= 0; i--)
            {
                //Building edifice = nodesReversed[i].GetEdifice(pawn.Map);
                var edifice = nodesReversed[index: i].GetThingList(pawn.Map).FirstOrDefault(x =>
                    x.def.thingClass == typeof(Building_DoorExpanded) ||
                    x.def.thingClass == typeof(Building_DoorRegionHandler));
                if (edifice != null)
                {
                    if ((edifice is Building_DoorExpanded building_Door && !building_Door.FreePassage &&
                         (pawn == null || !building_Door.PawnCanOpen(pawn))) ||
                        edifice.def.passability == Traversability.Impassable)
                    {
                        cellBefore = nodesReversed[i + 1];
                        __result = edifice;
                    }

                    var building_DoorReg = edifice as Building_DoorRegionHandler;
                    if (building_DoorReg == null || building_DoorReg.ParentDoor == null) continue;
                    if ((!building_DoorReg.FreePassage &&
                         (pawn == null || !building_DoorReg.PawnCanOpen(pawn))) ||
                        edifice.def.passability == Traversability.Impassable)
                    {
                        cellBefore = nodesReversed[i + 1];
                        __result = building_DoorReg.ParentDoor;
                    }
                }
            }
        }

        //PathFinder
        public static void GetBuildingCost_PostFix(Building b, TraverseParms traverseParms, Pawn pawn, ref int __result)
        {
           // if (__result >= int.MaxValue) return;
            if (b is Building_DoorRegionHandler reg)
            {
                switch (traverseParms.mode)
                {
                    case TraverseMode.ByPawn:
                    {
                        if (reg.PawnCanOpen(pawn) && !reg.FreePassage)
                        {
                            __result = reg.TicksToOpenNow;
                            return;
                        }
                        if (!traverseParms.canBash && reg.IsForbidden(pawn))
                        {
                            if (DebugViewSettings.drawPaths)
                            {
                                Traverse.Create(typeof(PathFinder)).Method("DebugFlash",
                                    new object[] {b.Position, b.Map, 0.77f, "forbid"});
                                //PathFinder.DebugFlash(b.Position, b.Map, 0.77f, "forbid");
                            }

                            __result = int.MaxValue;
                        }
                        
                        break;
                    }
                }
            }
            else if (b is Building_DoorExpanded ex)
            {
                switch (traverseParms.mode)
                {
                    case TraverseMode.ByPawn:
                    {
                        if (ex.PawnCanOpen(pawn) && !ex.FreePassage)
                        {
                            __result = ex.TicksToOpenNow;
                            return;
                        }
                        if (!traverseParms.canBash && ex.IsForbidden(pawn))
                        {
                            if (DebugViewSettings.drawPaths)
                            {
                                Traverse.Create(typeof(PathFinder)).Method("DebugFlash",
                                    new object[] {b.Position, b.Map, 0.77f, "forbid"});
                            }

                            __result = int.MaxValue;
                        }

                        break;
                    }
                }
            }
        }

        //ForbidUtility
        public static void IsForbiddenToPass_PostFix(this Thing t, Pawn pawn, ref bool __result)
        {
            if (t is Building_DoorRegionHandler reg)
            {
                //Log.Message("reg called");
                //__result = __result && ((t.Spawned && t.Position.IsForbidden(pawn) && !(t is Building_DoorRegionHandler)) || t.IsForbidden(pawn.Faction)); 
                //ForbidUtility.CaresAboutForbidden(pawn, false) && t.IsForbidden(pawn.Faction);
                //__result = reg.ParentDoor
                //    .IsForbidden(
                //        pawn); 


                //if (!pawn.AnimalOrWildMan()) return;
                //if (pawn.playerSettings == null) return;

                /*
                StringBuilder s = new StringBuilder();
                s.AppendLine("t.Spawned == " + t.Spawned);
                s.AppendLine("t.Position.IsForbidden(pawn) ==" + t.Position.IsForbidden(pawn));
                s.AppendLine(" !pawn.Drafted ==" + !pawn.Drafted);

                s.AppendLine("||");

                s.AppendLine("t.IsForbidden(pawn.Faction) == " + t.IsForbidden(pawn.Faction));
                s.AppendLine("pawn.HostileTo(t) == " + pawn.HostileTo(t));

                s.AppendLine("t is " + t.ToString());

                var c = t.Position;
                s.AppendLine("ForbidUtility.CaresAboutForbidden(pawn, true) == " + ForbidUtility.CaresAboutForbidden(pawn, true));
                s.AppendLine("!c.InAllowedArea(pawn) == " + !c.InAllowedArea(pawn));
                s.AppendLine("pawn.mindState.maxDistToSquadFlag > 0f == " + (pawn.mindState.maxDistToSquadFlag > 0f));
                s.AppendLine("!c.InHorDistOf(pawn.DutyLocation(), pawn.mindState.maxDistToSquadFlag)) == " + !c.InHorDistOf(pawn.DutyLocation(), pawn.mindState.maxDistToSquadFlag));
                s.AppendLine("Result = " + (ForbidUtility.CaresAboutForbidden(pawn, true) && (!c.InAllowedArea(pawn) || (pawn.mindState.maxDistToSquadFlag > 0f && !c.InHorDistOf(pawn.DutyLocation(), pawn.mindState.maxDistToSquadFlag)))));
                s.AppendLine("Supercool Result = " + (ForbidUtility.CaresAboutForbidden(pawn, true) && (pawn.mindState.maxDistToSquadFlag > 0f && !c.InHorDistOf(pawn.DutyLocation(), pawn.mindState.maxDistToSquadFlag))));
                s.AppendLine("Final result: " + __result);
                Log.Message(s.ToString());
                
                 */

                var tPositionIsForbidden_withoutLocationCheck = (ForbidUtility.CaresAboutForbidden(pawn, true) && (pawn.mindState.maxDistToSquadFlag > 0f && !t.Position.InHorDistOf(pawn.DutyLocation(), pawn.mindState.maxDistToSquadFlag)));
                __result = ((t.Spawned && tPositionIsForbidden_withoutLocationCheck) || (t.IsForbidden(pawn.Faction) || pawn.HostileTo(t)));
                //if (__result == false && pawn.AnimalOrWildMan()) Log.Message(pawn.LabelShort + " rejected from expanded door");
                //Log.Message("Result is " + __result.ToString());
            }
        }

        //PawnPathUtility.TryFindLastCellBeforeBlockingDoor
        //Adds an extra check.
        public static bool ManhunterJobGiverFix(PawnPath path, Pawn pawn, ref IntVec3 result, ref bool __result)
        {
            if (path?.NodesReversed?.Count == 1)
            {
                result = path.NodesReversed[index: 0];
                __result = false;
                //Log.Message("Nodes less or equal to 1");
                return false;
            }

            List<IntVec3> nodesReversed = path.NodesReversed;
            if (nodesReversed != null)
            {
                for (var i = nodesReversed.Count - 2; i >= 1; i--)
                {
                    //pawn.Map.debugDrawer.FlashCell(nodesReversed[i]);
                    var edifice = nodesReversed[index: i].GetThingList(pawn.Map)
                        .FirstOrDefault(x =>
                            x.def.thingClass == typeof(Building_DoorExpanded) ||
                            x.def.thingClass == typeof(Building_DoorRegionHandler)); //GetEdifice(map: pawn.Map);
                    
                    //var edifice = nodesReversed[i].GetEdifice(pawn.Map);
                    if (edifice is Building_DoorExpanded building_DoorExpanded)
                    {
                        if (!building_DoorExpanded?.InvisDoors?.Any(predicate: x => !x.CanPhysicallyPass(p: pawn) && !x.PawnCanOpen(pawn)) ??
                            false)
                        {
                            Log.Message(text: "DoorsExpanded :: Manhunter Check Passed (doorExpanded)");
                            result = nodesReversed[index: i + 1];
                            __result = true;
                            return false;
                        }
                    }

                    if (edifice is Building_DoorRegionHandler building_DoorReg)
                    {
                        if (!building_DoorReg.CanPhysicallyPass(pawn))
                        {
                            //Log.Message(text: "DoorsExpanded :: Manhunter Check Passed (doorRegionHandler) (CanPhysicallyPass)");
                            result = nodesReversed[index: i + 1];
                            __result = true;
                            return false;
                        }
                        if (!building_DoorReg.PawnCanOpen(pawn))
                        {
                            //Log.Message(text: "DoorsExpanded :: Manhunter Check Passed (doorRegionHandler) (PawnCanOpen)");
                            result = nodesReversed[index: i + 1];
                            __result = true;
                            return false;
                        }
                    }
                }

                //Log.Message("No objects detected in path");
                result = nodesReversed[index: 0];
            }

            __result = false;
            return true;
        }

        //Building_Door
        public static bool get_FreePassage(Building_Door __instance, ref bool __result)
        {
            if (__instance is Building_DoorRegionHandler b && b.ParentDoor != null)
            {
                __result = b.ParentDoor.FreePassage;// && !b.ParentDoor.Forbidden;
                return false;
            }

            return true;
        }


        public static bool DontDrawInvisDoorForbiddenIcons(CompForbiddable __instance)
        {
            if (__instance.parent is Building_DoorRegionHandler)
                return false;
            return true;
        }

        public static void ShouldNotEnterCellInvisDoors(Pawn pawn, Map map, IntVec3 dest, ref bool __result)
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

            Building edifice = dest.GetEdifice(map: map);
            if (edifice == null)
            {
                //Log.Message("No edifice. So let's go!");
                return;
            }

            if (edifice is Building_DoorExpanded building_doorEx)
            {
                if (building_doorEx.IsForbidden(pawn))
                {
                    __result = true;
                    return;
                }

                if (!building_doorEx.PawnCanOpen(pawn))
                {
                    __result = true;
                    return;
                }
            }

            if (edifice is Building_DoorRegionHandler building_doorReg)
            {
                if (building_doorReg.IsForbidden(pawn))
                {
                    __result = true;
                    return;
                }

                if (!building_doorReg.PawnCanOpen(p: pawn))
                {
                    __result = true;
                }
            }
        }

        public static bool WipeExistingThings(IntVec3 thingPos, Rot4 thingRot, BuildableDef thingDef, Map map,
            DestroyMode mode)
        {
            //Log.Message("1");
            var trueDef = DefDatabase<ThingDef>.AllDefs.FirstOrDefault(predicate: x => x.defName == thingDef.defName);
            //if (trueDef != null && trueDef.thingClass == typeof(Building_DoorExpanded) && !thingPos.GetThingList(map).Any(x => x is Building_DoorExpanded))
            //    return false;
            if (thingDef == HeronDefOf.HeronInvisibleDoor ||
                thingDef.defName == HeronDefOf.HeronInvisibleDoor.defName)
            {
                return false;
            }

            return true;
        }

        //GenSpawn
        public static void InvisDoorsDontWipe(BuildableDef newEntDef, BuildableDef oldEntDef, ref bool __result)
        {
            var oldTrueDef =
                DefDatabase<ThingDef>.AllDefs.FirstOrDefault(predicate: x => x.defName == oldEntDef.defName);
            var newTrueDef =
                DefDatabase<ThingDef>.AllDefs.FirstOrDefault(predicate: x => x.defName == newEntDef.defName);
            if (newEntDef.defName == HeronDefOf.HeronInvisibleDoor.defName &&
                oldEntDef.defName == HeronDefOf.HeronInvisibleDoor.defName)
            {
                __result = true; //false, meaning, don't wipe the old thing when you spawn
                return;
            }

            if (newEntDef.defName == HeronDefOf.HeronInvisibleDoor.defName ||
                oldEntDef.defName == HeronDefOf.HeronInvisibleDoor.defName)
            {
                __result = false; //false, meaning, don't wipe the old thing when you spawn
                return;
            }

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

        // Verse.GenSpawn
        public static bool HeronSpawnBuildingAsPossible(Building building, Map map, bool respawningAfterLoad = false)
        {
            //Log.Message("1");
            if (building is Building_DoorExpanded ||
                building is Building_DoorRegionHandler ||
                building.def == HeronDefOf.HeronInvisibleDoor ||
                building.def.thingClass == typeof(Building_DoorRegionHandler))
            {
                GenSpawn.Spawn(newThing: building, loc: building.Position, map: map, rot: building.Rotation,
                    wipeMode: WipeMode.Vanish, respawningAfterLoad: respawningAfterLoad);
                return false;
            }

            return true;
        }

        // Verse.Graphic
        public static Quaternion QuatFromRot(Graphic __instance, Rot4 rot)
        {
            if (__instance.data != null && !__instance.data.drawRotated)
            {
                return Quaternion.identity;
            }

            if (__instance.ShouldDrawRotated)
            {
                return rot.AsQuat;
            }

            return Quaternion.identity;
        }

        public static bool isExceptionForEdificeRegistration(Building ed)
        {
            return ed.def.thingClass.IsAssignableFrom(typeof(Building_DoorExpanded)) ||
                   ed.def.thingClass.IsAssignableFrom(typeof(Building_DoorRegionHandler)) ||
                   ed.def.thingClass == typeof(Building_DoorRegionHandler) ||
                   ed.def.thingClass == typeof(Building_DoorExpanded);
        }

        //EdificeGrid
        //TODO Make transpiler
        public static bool RegisterDoorExpanded(EdificeGrid __instance, Building ed)
        {
            //Log.Message("Register");
            //Log.Message(ed.Label);
            //Log.Message(ed.def.thingClass.ToString());
            if (isExceptionForEdificeRegistration(ed))
            {
                //<VanillaCodeSequence>
                CellIndices cellIndices = Traverse.Create(__instance).Field("map").GetValue<Map>().cellIndices;
                CellRect cellRect = ed.OccupiedRect();
                for (int i = cellRect.minZ; i <= cellRect.maxZ; i++)
                {
                    for (int j = cellRect.minX; j <= cellRect.maxX; j++)
                    {
                        IntVec3 intVec = new IntVec3(j, 0, i);
                        var oldBuilding = __instance[intVec];
                        if (UnityData.isDebugBuild && oldBuilding != null && !oldBuilding.Destroyed &&
                            !isExceptionForEdificeRegistration(oldBuilding))
                        {
                            Log.Error(string.Concat(new object[]
                            {
                                "Added edifice ",
                                ed.LabelCap,
                                " over edifice ",
                                oldBuilding.LabelCap,
                                " at ",
                                intVec,
                                ". Destroying old edifice, despite DoorsExpanded code."
                            }));
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


        // Verse.GhostDrawer
        public static bool HeronDoorGhostHandler(IntVec3 center, Rot4 rot, ThingDef thingDef, Graphic baseGraphic,
            Color ghostCol, AltitudeLayer drawAltitude)
        {
            if (thingDef is DoorExpandedDef def && def.fixedPerspective)
            {
                Graphic graphic = GhostUtility.GhostGraphicFor(baseGraphic, thingDef, ghostCol);
                //Graphic graphic = Traverse.Create(typeof(GhostDrawer)).Method("GhostGraphicFor", new object[] { thingDef.graphic, thingDef, ghostCol }).GetValue<Graphic>();
                Vector3 loc = GenThing.TrueCenter(center, rot, thingDef.Size, drawAltitude.AltitudeFor());

                for (int i = 0; i < 2; i++)
                {
                    bool flipped = (i != 0) ? true : false;
                    Building_DoorExpanded.DrawParams(def, loc, rot, out var mesh, out var matrix, mod: 0,
                        flipped: flipped);
                    Graphics.DrawMesh(mesh: mesh, matrix: matrix, material: graphic.MatAt(rot: rot, thing: null),
                        layer: 0);
                }

                if (thingDef?.PlaceWorkers?.Count > 0)
                {
                    for (int i = 0; i < thingDef.PlaceWorkers.Count; i++)
                    {
                        thingDef.PlaceWorkers[index: i]
                            .DrawGhost(def: thingDef, center: center, rot: rot, ghostCol: ghostCol);
                    }
                }

                return false;
            }

            return true;
        }


        //// Verse.ThingDef
        //public static void HeronDoorIsDoor(ThingDef __instance, ref bool __result)
        //{
        //    __result = __result || (typeof(Building_DoorExpanded).IsAssignableFrom(__instance.thingClass) && __instance.thingClass != typeof(Building_DoorFixedPerspective));
        //}


        /// <summary>
        /// Duplicate code of RimWorld.Building_Door
        /// The code is modified to detect if there is
        /// a Thing with the Class Building_DoorExpanded, so the
        /// door does not stay open indefinitely by thinking that
        /// something is blocking its path.
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="__result"></param>
        public static void InvisDoorWillCloseSoon(Building_Door __instance, ref bool __result)
        {
            if (__instance is Building_DoorRegionHandler)
            {
                for (int i = 0; i < 5; i++)
                {
                    IntVec3 c = __instance.Position + GenAdj.CardinalDirectionsAndInside[i];
                    if (c.InBounds(map: __instance.Map))
                    {
                        List<Thing> thingList = c.GetThingList(map: __instance.Map);
                        for (int j = 0; j < thingList.Count; j++)
                        {
                            Building_DoorExpanded b = thingList[index: j] as Building_DoorExpanded;
                            if (b != null)
                            {
                                __result = false;
                            }
                        }
                    }
                }
            }
        }

        // RimWorld.Building_Door
        public static bool InvisDoorManualClose(Building_Door __instance, Pawn closer)
        {
            if (__instance is Building_DoorRegionHandler w)
            {
                //w.ParentDoor.StartManualCloseBy(closer);
                return false;
            }

            return true;
        }

        // RimWorld.Building_Door
        public static void InvisDoorManualOpen(Building_Door __instance, Pawn opener)
        {
            if (__instance is Building_DoorRegionHandler w)
            {
                if (w.ParentDoor.PawnCanOpen(p: opener))
                {
                    w.ParentDoor.StartManualOpenBy(opener: opener);
                    if (w.ParentDoor.InvisDoors.ToList().FindAll(match: x => x != __instance) is
                            List<Building_DoorRegionHandler> otherDoors && !otherDoors.NullOrEmpty())
                    {
                        foreach (Building_DoorRegionHandler door in otherDoors)
                        {
                            if (!door.Open)
                            {
                                int math = (int) (1200 * Math.Max(val1: w.ParentDoor.Graphic.drawSize.x,
                                                      val2: w.ParentDoor.Graphic.drawSize.y));
                                //this.ticksUntilClose = ticksToClose;
                                Traverse.Create(root: door).Field(name: "ticksUntilClose").SetValue(value: math);
                                //this.openInt = true;
                                Traverse.Create(root: door).Field(name: "openInt").SetValue(value: true);
                            }
                        }
                    }
                }
            }
        }


        // RimWorld.Building_Door
        public static void InvisDoorNotifyApproaching(Building_Door __instance, Pawn p)
        {
            if (__instance is Building_DoorRegionHandler w)
            {
                w.ParentDoor?.Notify_PawnApproaching(p: p);
            }
        }


        //Building_Door
        public static bool InvisDoorOpen(Building_Door __instance, int ticksToClose = 60)
        {
            if (__instance is Building_DoorRegionHandler w)
            {
                Traverse.Create(root: __instance).Field(name: "ticksUntilClose").SetValue(value: ticksToClose);
                if (!Traverse.Create(root: __instance).Field(name: "openInt").GetValue<bool>())
                {
                    AccessTools.Field(type: typeof(Building_Door), name: "openInt")
                        .SetValue(obj: __instance, value: true);
                    //Traverse.Create(__instance).Field("openInt"). SetValue(true);
                }

                w.ParentDoor.DoorOpen(ticksToClose: ticksToClose);
                return false;
            }

            return true;
        }

        public static bool InvisDoorTryClose(Building_Door __instance)
        {
            if (__instance is Building_DoorRegionHandler w)
            {
                //w.ParentDoor.DoorTryClose();
                if (!Traverse.Create(root: __instance).Field(name: "holdOpenInt").GetValue<bool>() ||
                    __instance.BlockedOpenMomentary || w.ParentDoor.Open)
                {
                    return false;
                }
                //AccessTools.Field(typeof(Building_Door), "openInt").SetValue(__instance, false);

                //Traverse.Create(__instance).Field("openInt").SetValue(false);
                return false;
            }

            return true;
        }


        // Verse.RegionTypeUtility
        public static void GetHeronRegionType(ref RegionType __result, IntVec3 c, Map map)
        {
            if (__result == RegionType.Normal)
            {
                List<Thing> thingList = c.GetThingList(map: map);
                for (int i = 0; i < thingList.Count; i++)
                {
                    if (thingList[index: i] is Building_DoorExpanded door)
                    {
                        if (c != door.OccupiedRect().Cells.ToArray()[0])
                        {
                            //Log.Message("SetPassable");
                            __result = RegionType.Portal; //ImpassableFreeAirExchange;
                        }
                    }
                }
            }
        }
    }
}