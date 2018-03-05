using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
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
            HarmonyInstance harmony = HarmonyInstance.Create("rimworld.jecrell.doorsexpanded");
            harmony.Patch(AccessTools.Method(typeof(Building_Door), "DoorOpen"), new HarmonyMethod(typeof(HarmonyPatches),
                nameof(InvisDoorOpen)), null);
            harmony.Patch(AccessTools.Method(typeof(Building_Door), "DoorTryClose"), new HarmonyMethod(typeof(HarmonyPatches),
                nameof(InvisDoorTryClose)), null);
            harmony.Patch(AccessTools.Method(typeof(Building_Door), "Notify_PawnApproaching"), null, new HarmonyMethod(typeof(HarmonyPatches),
               nameof(InvisDoorNotifyApproaching)), null);
            harmony.Patch(AccessTools.Method(typeof(Building_Door), "StartManualCloseBy"), new HarmonyMethod(typeof(HarmonyPatches),
                nameof(InvisDoorManualClose)), null);
            harmony.Patch(AccessTools.Method(typeof(Building_Door), "StartManualOpenBy"), null, new HarmonyMethod(typeof(HarmonyPatches),
                nameof(InvisDoorManualOpen)), null);
            //harmony.Patch(AccessTools.Method(typeof(ThingDef), "get_IsDoor"), null, new HarmonyMethod(typeof(HarmonyPatches),
            //    nameof(HeronDoorIsDoor)), null);
            harmony.Patch(AccessTools.Method(typeof(GhostDrawer), "DrawGhostThing"), new HarmonyMethod(typeof(HarmonyPatches),
                nameof(HeronDoorGhostHandler)), null);
            harmony.Patch(AccessTools.Method(typeof(GenSpawn), "SpawnBuildingAsPossible"), new HarmonyMethod(typeof(HarmonyPatches),
                nameof(HeronSpawnBuildingAsPossible)), null);
            harmony.Patch(AccessTools.Method(typeof(GenSpawn), "WipeExistingThings"), new HarmonyMethod(typeof(HarmonyPatches),
                nameof(WipeExistingThings)), null);
            harmony.Patch(AccessTools.Method(typeof(GenSpawn), "SpawningWipes"), null, new HarmonyMethod(typeof(HarmonyPatches),
                nameof(InvisDoorsDontWipe)), null);
            harmony.Patch(AccessTools.Method(typeof(GenPath), "ShouldNotEnterCell"), null, new HarmonyMethod(typeof(HarmonyPatches),
                nameof(ShouldNotEnterCellInvisDoors)), null);
            harmony.Patch(AccessTools.Method(typeof(CompForbiddable), "PostDraw"), new HarmonyMethod(typeof(HarmonyPatches),
                nameof(DontDrawInvisDoorForbiddenIcons)), null);
        }

        public static bool DontDrawInvisDoorForbiddenIcons(CompForbiddable __instance)
        {
            if (__instance.parent is Building_DoorRegionHandler)
                return false;
            return true;
        }

        public static void ShouldNotEnterCellInvisDoors(Pawn pawn, Map map, IntVec3 dest, ref bool __result )
        {
            if (__result)
                return;
            Building edifice = dest.GetEdifice(map);
            if (edifice != null)
            {
                Building_DoorExpanded building_doorEx = edifice as Building_DoorExpanded;
                if (building_doorEx != null)
                {
                    if (building_doorEx.IsForbidden(pawn))
                    {
                        __result = true;
                    }
                    if (!building_doorEx.PawnCanOpen(pawn))
                    {
                        __result = true;
                    }
                }
            }
        }
        
        public static bool WipeExistingThings(IntVec3 thingPos, Rot4 thingRot, BuildableDef thingDef, Map map, DestroyMode mode)
        {
            //Log.Message("1");
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
            if (newEntDef.defName == HeronDefOf.HeronInvisibleDoor.defName || oldEntDef.defName == HeronDefOf.HeronInvisibleDoor.defName)
                __result = false;  //false, meaning, don't wipe the old thing when you spawn
        }

        // Verse.GenSpawn
        public static bool HeronSpawnBuildingAsPossible(Building building, Map map, bool respawningAfterLoad = false)
        {
            //Log.Message("1");
            if (building is Building_DoorRegionHandler ||
                building.def == HeronDefOf.HeronInvisibleDoor ||
                building.def.thingClass == typeof(Building_DoorRegionHandler)) 
            {
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


        // Verse.GhostDrawer
        public static bool HeronDoorGhostHandler(IntVec3 center, Rot4 rot, ThingDef thingDef, Graphic baseGraphic, Color ghostCol, AltitudeLayer drawAltitude)
        {
            if (thingDef is DoorExpandedDef def && def.fixedPerspective)
            {
                    //Log.Message("1");
                    Graphic graphic = (Graphic)AccessTools.Method(typeof(GhostDrawer), "GhostGraphicFor").Invoke(null, new object[] { thingDef.graphic, thingDef, ghostCol });
                    //Log.Message("2");
                    Vector3 loc = Gen.TrueCenter(center, rot, thingDef.Size, Altitudes.AltitudeFor(drawAltitude));

                    for (int i = 0; i < 2; i++)
                    {
                        bool flipped = (i != 0) ? true : false;
                        Mesh mesh;
                        Matrix4x4 matrix;
                        Building_DoorExpanded.DrawParams(def, loc, rot, out mesh, out matrix, 0, flipped);
                        Graphics.DrawMesh(mesh, matrix, graphic.MatAt(rot, null), 0);
                    }
                    
                    if (thingDef.PlaceWorkers != null)
                    {
                        for (int i = 0; i < thingDef.PlaceWorkers.Count; i++)
                        {
                            thingDef.PlaceWorkers[i].DrawGhost(thingDef, center, rot);
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
                    if (c.InBounds(__instance.Map))
                    {
                        List<Thing> thingList = c.GetThingList(__instance.Map);
                        for (int j = 0; j < thingList.Count; j++)
                        {
                            Building_DoorExpanded b = thingList[j] as Building_DoorExpanded;
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
                if (w.ParentDoor.PawnCanOpen(opener))
                {
                    w.ParentDoor.StartManualOpenBy(opener);
                    if (w.ParentDoor.InvisDoors.ToList().FindAll(x => x != __instance) is List<Building_DoorRegionHandler> otherDoors && !otherDoors.NullOrEmpty())
                    {
                        foreach (Building_DoorRegionHandler door in otherDoors)
                        {
                            if (!door.Open)
                            {
                                int math = (int)(1200 * Math.Max(w.ParentDoor.Graphic.drawSize.x, w.ParentDoor.Graphic.drawSize.y));
                                //this.ticksUntilClose = ticksToClose;
                                Traverse.Create(door).Field("ticksUntilClose").SetValue(math);
                                //this.openInt = true;
                                Traverse.Create(door).Field("openInt").SetValue(true);
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
                w.ParentDoor.Notify_PawnApproaching(p);
            }
        }


        //Building_Door
        public static bool InvisDoorOpen(Building_Door __instance, int ticksToClose = 60)
        {
            if (__instance is Building_DoorRegionHandler w)
            {
                Traverse.Create(__instance).Field("ticksUntilClose").SetValue(ticksToClose);
                if (!Traverse.Create(__instance).Field("openInt").GetValue<bool>())
                {
                    AccessTools.Field(typeof(Building_Door), "openInt").SetValue(__instance, true);
                    //Traverse.Create(__instance).Field("openInt"). SetValue(true);
                }
                w.ParentDoor.DoorOpen(ticksToClose);
                return false;
            }
            return true;
        }

        public static bool InvisDoorTryClose(Building_Door __instance)
        {

            if (__instance is Building_DoorRegionHandler w)
            {
                //w.ParentDoor.DoorTryClose();
                if (!Traverse.Create(__instance).Field("holdOpenInt").GetValue<bool>() || __instance.BlockedOpenMomentary || w.ParentDoor.Open)
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
                List<Thing> thingList = c.GetThingList(map);
                for (int i = 0; i < thingList.Count; i++)
                {
                    if (thingList[i] is Building_DoorExpanded door)
                    {
                        if (c != door.OccupiedRect().Cells.ToArray()[0])
                        {
                            //Log.Message("SetPassable");
                            __result = RegionType.ImpassableFreeAirExchange;
                        }
                    }
                }
            }
        }
    }
}

