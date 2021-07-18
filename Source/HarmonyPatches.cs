//#define PATCH_CALL_REGISTRY

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Xml;
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
            HarmonyPatches.Patches();
            DebugInspectorPatches.PatchDebugInspector();
        }
    }

    // TODO: Reorganize this into multiple classes/files and use Harmony attribute-based patch classes.
    public static class HarmonyPatches
    {
        internal static Harmony harmony = new Harmony("rimworld.jecrell.doorsexpanded");

        // Early patching before any XML Def/Patch loading and StaticConstructorOnStartup code.
        // This is called from DoorsExpandedMod constructor for earliest possible patching.
        public static void EarlyPatches()
        {
            // The MinifyEverything mod attempts to make all ThingDefs having a minifiedDef.
            // This includes our invisible doors (def HeronInvisibleDoor and class Building_DoorRegionHandler),
            // and users have reported that this can result in minified invisible doors somehow (how, I don't know...)
            // So this is a hack to undo MinifyEverything's AddMinifiedFor behavior for invisible doors.
            // This patch also must be applied earlier that MinifyEverything's StaticConstructorOnStartup-based patching,
            // since that's when its AddMinifiedFor is called.
            Patch(original: AccessTools.Method(typeof(ThingDefGenerator_Buildings), "NewBlueprintDef_Thing"),
                postfix: nameof(InvisDoorNewBlueprintDefThingPostfix));
        }

        // ThingDefGenerator_Buildings.NewBlueprintDef_Thing
        public static void InvisDoorNewBlueprintDefThingPostfix(ThingDef def, bool isInstallBlueprint)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(InvisDoorNewBlueprintDefThingPostfix));
            if (def == HeronDefOf.HeronInvisibleDoor && isInstallBlueprint)
            {
                def.blueprintDef = null;
                var installBlueprintDef = def.installBlueprintDef;
                def.installBlueprintDef = null;
                def.minifiedDef = null;
                // ThingDefGenerator_Buildings.NewBlueprintDef_Thing is called within MinifyEverything's AddMinifiedFor,
                // which then adds installBlueprintDef to the DefDatabase. So must remove from DefDatabase afterwards.
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    if (DefDatabase<ThingDef>.GetNamedSilentFail(installBlueprintDef.defName) != null)
                        DefDatabaseThingDefRemove(installBlueprintDef);
                    Log.Message($"[Doors Expanded] Detected minifiedDef for def {def} with installBlueprintDef {installBlueprintDef}, " +
                        "likely added by MinifyEverything mod - removed to avoid minifiable invisible doors");
                });
            }
        }

        private static readonly Action<ThingDef> DefDatabaseThingDefRemove =
            (Action<ThingDef>)AccessTools.Method(typeof(DefDatabase<ThingDef>), "Remove", new[] { typeof(ThingDef) })
                .CreateDelegate(typeof(Action<ThingDef>));

        public static void Patches()
        {
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

            // Notes on what methods don't need patching despite def.Fillage being potentially used on invis doors,
            // but that method ultimately use def.Fillage on every thing at the cell to the net effect of no negative impact:
            // SymbolResolver_Clear.Resolve
            // BeautyUtility.CellBeauty
            // GenConstruct.BlocksConstruction
            // Skyfaller.SpawnThings
            // BuildingsDamageSectionLayerUtility.UsesLinkableCornersAndEdges
            // DamageWorker.ExplosionAffectCell
            // ForGrid.Unfog (also see InvisDoorMakeFogTranspiler)
            // RegionTypeUtility.GetExpectedRegionType

            // Notes on what methods don't need patching despite def.Fillage being potentially used on invis doors
            // for other reasons:
            // Designator_RemoveFloor.CanDesignateCell
            // - def.Fillage usage is only for determining Impassible def.passibility (walls),
            //   and doesn't return false for invis doors, which is wanted behavior.
            // Designator_SmoothSurface.CanDesignateCell/SmoothSurfaceDesignatorUtility.CanSmoothFloorUnder
            // - def.Fillage usage is only for determining whether floors can be smoothed,
            //   SmoothSurfaceDesignatorUtility.CanSmoothFloorUnder returns true for invis doors, which is wanted behavior.
            // Sketch.CanBeSeenOver (and other Sketch* methods)
            // - Assume that ThingDefs used in Sketch objects are never invis doors.
            // PawnPathUtility.FirstBlockingBuilding
            // - def.Fillage usage is only for determining PassThroughOnly def.passibility, which doors should never have.
            // CoverGrid.Register/DeRegister/RecalculateCell
            // - This will do nothing for an invis door, and ultimately be called for the parent door,
            //   with the algorithm uses all cells within the parent door.
            //   The net effect is parent doors, not invis doors, will be in the cover grid.
            // SectionLayer_Snow.Filled
            // - Method is unused.
            // SnowGrid.CanCoexistWithSnow
            // - See InvisDoorCanHaveSnowTranspiler.
            // - Only other use of the method is for determining snow depth at cell and is ultimately called for the parent door.
            // Verb.CanHitFromCellIgnoringRange (and other Verb* methods)
            // - Target should never be an invis door.
            // TODO: Fill above section out more for documentation purposes.

            // Notes on usage of patch priority:
            // - Destructive prefix patches (prefix patch that returns false, preventing remaining non-postfix/finalizer logic)
            //   that simply prevent a method from running based off a simple filter have high priority yet NOT highest priority,
            //   because poorly written destructive prefix patches that replicate and modify original logic typically have normal
            //   priority, and for mod authors that are aware of the harmony priority system and use highest priority, assume they
            //   know what they're doing and let their prefix patches run before our high priority ones.
            // - Prefix patches that delegate calls to invis door methods to parent door methods have highest (first) priority
            //   to get ahead of other mods' destructive prefix patches. Unfortunately, our Building_Door patches must be destructive
            //   prefix patches, and there's no safe way to redirect other mods' Building_Door patches to Building_DoorExpanded patches.

            // See comments in Building_DoorRegionHandler.
            Patch(original: AccessTools.PropertyGetter(typeof(Building_Door), nameof(Building_Door.FreePassage)),
                prefix: nameof(InvisDoorFreePassagePrefix),
                priority: Priority.First);
            Patch(original: AccessTools.PropertyGetter(typeof(Building_Door), nameof(Building_Door.TicksTillFullyOpened)),
                prefix: nameof(InvisDoorTicksTillFullyOpenedPrefix),
                priority: Priority.First);
            Patch(original: AccessTools.PropertyGetter(typeof(Building_Door), nameof(Building_Door.WillCloseSoon)),
                prefix: nameof(InvisDoorWillCloseSoonPrefix),
                priority: Priority.First);
            Patch(original: AccessTools.PropertyGetter(typeof(Building_Door), nameof(Building_Door.BlockedOpenMomentary)),
                prefix: nameof(InvisDoorBlockedOpenMomentaryPrefix),
                priority: Priority.First);
            Patch(original: AccessTools.PropertyGetter(typeof(Building_Door), nameof(Building_Door.SlowsPawns)),
                prefix: nameof(InvisDoorSlowsPawnsPrefix),
                priority: Priority.First);
            Patch(original: AccessTools.PropertyGetter(typeof(Building_Door), nameof(Building_Door.TicksToOpenNow)),
                prefix: nameof(InvisDoorTicksToOpenNowPrefix),
                priority: Priority.First);
            Patch(original: AccessTools.Method(typeof(Building_Door), nameof(Building_Door.CheckFriendlyTouched)),
                prefix: nameof(InvisDoorCheckFriendlyTouchedPrefix),
                priority: Priority.First);
            Patch(original: AccessTools.Method(typeof(Building_Door), nameof(Building_Door.Notify_PawnApproaching)),
                prefix: nameof(InvisDoorNotifyPawnApproachingPrefix),
                priority: Priority.First);
            Patch(original: AccessTools.Method(typeof(Building_Door), nameof(Building_Door.CanPhysicallyPass)),
                prefix: nameof(InvisDoorCanPhysicallyPassPrefix),
                priority: Priority.First);
            Patch(original: AccessTools.Method(typeof(Building_Door), "DoorOpen"),
                prefix: nameof(InvisDoorDoorOpenPrefix),
                priority: Priority.First);
            Patch(original: AccessTools.Method(typeof(Building_Door), "DoorTryClose"),
                prefix: nameof(InvisDoorDoorTryClosePrefix),
                priority: Priority.First);
            Patch(original: AccessTools.Method(typeof(Building_Door), nameof(Building_Door.StartManualOpenBy)),
                prefix: nameof(InvisDoorStartManualOpenByPrefix),
                priority: Priority.First);
            // Note: Building_Door.StartManualCloseBy gets inlined, but Harmony 2 can prevent this, so that it can be patched.
            Patch(original: AccessTools.Method(typeof(Building_Door), nameof(Building_Door.StartManualCloseBy)),
                prefix: nameof(InvisDoorStartManualCloseByPrefix),
                priority: Priority.First);

            // Patches to redirect access from invis door def to its parent door def.
            Patch(original: AccessTools.Method(typeof(GenStep_Terrain), nameof(GenStep_Terrain.Generate)),
                transpiler: nameof(InvisDoorDefFillageTranspiler));
            Patch(original: AccessTools.Method(typeof(GenGrid), nameof(GenGrid.CanBeSeenOver), new[] { typeof(Building) }),
                transpiler: nameof(InvisDoorDefFillageTranspiler));
            Patch(original: AccessTools.Method(typeof(GridsUtility), nameof(GridsUtility.Filled)),
                transpiler: nameof(InvisDoorDefFillageTranspiler));
            Patch(original: AccessTools.Method(typeof(Projectile), "ImpactSomething"),
                transpiler: nameof(InvisDoorDefFillageTranspiler));
            Patch(original: AccessTools.Method(rwAssembly.GetType("Verse.SectionLayer_IndoorMask"), "HideRainPrimary"),
                transpiler: nameof(InvisDoorDefFillageTranspiler));
            foreach (var original in typeof(FloodFillerFog).FindLambdaMethods(nameof(FloodFillerFog.FloodUnfog), typeof(bool)))
            {
                Patch(original,
                    transpiler: nameof(InvisDoorDefMakeFogTranspiler));
            }
            Patch(original: AccessTools.Method(typeof(FogGrid), "FloodUnfogAdjacent"),
                transpiler: nameof(InvisDoorDefMakeFogTranspiler));
            Patch(original: AccessTools.Method(typeof(SnowGrid), "CanHaveSnow"),
                transpiler: nameof(InvisDoorCanHaveSnowTranspiler));

            // Note: Not using AccessTools.TypeByName, since an invalid loaded assembly (e.g. wrong referenced assembly version) can cause
            // AccessTools.TypeByName to fail as shown in the following example logs
            // (note how there are no listed DoorsExpanded patches from InvisDoorBlockLightTranspiler onwards in above PatchAll):
            // https://gist.github.com/HugsLibRecordKeeper/1f5317c8f1643df4593ad981a7e038df
            // https://gist.github.com/HugsLibRecordKeeper/fa0fc17b6ddff4eb871e4ec946f7b834
            // Using GenTypes.GetTypeInAnyAssembly instead, even if it's slower on first call for a given type name,
            // since GenTypes.GetTypeInAnyAssembly only "valid" loaded assemblies (as determined via ModAssemblyHandler.AssemblyIsUsable).
            if (GenTypes.GetTypeInAnyAssembly("OpenedDoorsDontBlockLight.GlowFlooder_Patch") is
                Type openedDoorsDontBlockLightGlowFlooderPatch)
            {
                foreach (var original in AccessTools.GetDeclaredMethods(openedDoorsDontBlockLightGlowFlooderPatch))
                {
                    if (original.GetParameters().Any(param => param.ParameterType == typeof(Thing)))
                    {
                        Patch(original,
                            transpiler: nameof(InvisDoorCanHaveSnowTranspiler));
                    }
                }
                // Note: The transpiler in OpenedDoorsDontBlockLight.GlowFlooder_Patch matches on thing.def.blockLight
                // and replaces that with a call to a custom method that checks thing.def.blockLight,
                // so we don't want to patch GlowFlooder.AddFloodGlowFor ourselves in this case.
            }
            else
            {
                Patch(original: AccessTools.Method(typeof(GlowFlooder), nameof(GlowFlooder.AddFloodGlowFor)),
                    transpiler: nameof(InvisDoorBlockLightTranspiler));
            }

            Patch(original: AccessTools.Method(typeof(SectionLayer_LightingOverlay), nameof(SectionLayer_LightingOverlay.Regenerate)),
                transpiler: nameof(InvisDoorBlockLightTranspiler));

            // Other patches for invis doors.
            Patch(original: AccessTools.Method(typeof(PathGrid), nameof(PathGrid.CalculatedCostAt)),
                transpiler: nameof(InvisDoorCalculatedCostAtTranspiler));
            Patch(original: AccessTools.Method(typeof(GenSpawn), nameof(GenSpawn.WipeExistingThings)),
                prefix: nameof(InvisDoorWipeExistingThingsPrefix),
                priority: Priority.VeryHigh);
            Patch(original: AccessTools.Method(typeof(GenSpawn), nameof(GenSpawn.SpawningWipes)),
                prefix: nameof(InvisDoorSpawningWipesPrefix),
                priority: Priority.VeryHigh);
            Patch(original: AccessTools.Method(typeof(PathFinder), nameof(PathFinder.IsDestroyable)),
                postfix: nameof(InvisDoorIsDestroyablePostfix));
            Patch(original: AccessTools.Method(typeof(Room), nameof(Room.Notify_ContainedThingSpawnedOrDespawned)),
                prefix: nameof(InvisDoorRoomNotifyContainedThingSpawnedOrDespawnedPrefix),
                priority: Priority.VeryHigh);
            Patch(original: AccessTools.Method(typeof(MouseoverReadout), nameof(MouseoverReadout.MouseoverReadoutOnGUI)),
                transpiler: nameof(MouseoverReadoutTranspiler));

            // Patches for door expanded doors themselves.
            Patch(original: AccessTools.Method(typeof(CoverUtility), nameof(CoverUtility.BaseBlockChance), new[] { typeof(Thing) }),
                transpiler: nameof(DoorExpandedBaseBlockChanceTranspiler));
            Patch(original: AccessTools.Method(typeof(GenGrid), nameof(GenGrid.CanBeSeenOver), new[] { typeof(Building) }),
                transpiler: nameof(DoorExpandedCanBeSeenOverTranspiler));
            Patch(original: AccessTools.Method(typeof(EdificeGrid), nameof(EdificeGrid.Register)),
                prefix: nameof(DoorExpandedEdificeGridRegisterPrefix),
                priority: Priority.VeryHigh);
            Patch(original: AccessTools.PropertyGetter(typeof(ThingDef), nameof(ThingDef.IsDoor)),
                postfix: nameof(DoorExpandedThingDefIsDoorPostfix));
            Patch(original: AccessTools.PropertySetter(typeof(CompForbiddable), nameof(CompForbiddable.Forbidden)),
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
            Patch(original: AccessTools.Method(typeof(Thing), nameof(Thing.SetFactionDirect)),
                prefix: nameof(DoorExpandedSetFactionDirectPrefix),
                priority: Priority.VeryHigh);

            // Patches for ghost (pre-placement) and blueprints for door expanded.
            foreach (var original in typeof(Designator_Place).FindLambdaMethods(nameof(Designator_Place.DoExtraGuiControls), typeof(void)))
            {
                Patch(original,
                    transpiler: nameof(DoorExpandedDesignatorPlaceRotateAgainIfNeededTranspiler),
                    transpilerRelated: nameof(DoorExpandedRotateAgainIfNeeded));
            }
            Patch(original: AccessTools.Method(typeof(Designator_Place), "HandleRotationShortcuts"),
                transpiler: nameof(DoorExpandedDesignatorPlaceRotateAgainIfNeededTranspiler),
                transpilerRelated: nameof(DoorExpandedRotateAgainIfNeeded));
            Patch(original: AccessTools.Method(typeof(GhostDrawer), "DrawGhostThing_NewTmp") ??
                    AccessTools.Method(typeof(GhostDrawer), nameof(GhostDrawer.DrawGhostThing)),
                transpiler: nameof(DoorExpandedDrawGhostThingTranspiler),
                transpilerRelated: nameof(DoorExpandedDrawGhostGraphicFromDef));
            Patch(original: AccessTools.Method(typeof(GhostUtility), nameof(GhostUtility.GhostGraphicFor)),
                transpiler: nameof(DoorExpandedGhostGraphicForTranspiler));
            Patch(original: AccessTools.Method(typeof(Blueprint), nameof(Blueprint.SpawnSetup)),
                prefix: nameof(DoorExpandedBlueprintSpawnSetupPrefix));
            Patch(original: AccessTools.Method(typeof(Blueprint), nameof(Blueprint.Draw)),
                prefix: nameof(DoorExpandedBlueprintDrawPrefix));

            // Patches related to door remotes.
            Patch(original: AccessTools.Method(typeof(FloatMenuMakerMap), "AddJobGiverWorkOrders_NewTmp") ??
                    AccessTools.Method(typeof(FloatMenuMakerMap), "AddJobGiverWorkOrders"),
                transpiler: nameof(DoorRemoteAddJobGiverWorkOrdersTranspiler),
                transpilerRelated: nameof(TranslateCustomizeUseDoorRemoteJobLabel));

            // Workaround for MinifyEverything issue where reinstalling doors sometimes causes a transient and harmless NRE
            // in GridsUtility.GetThingList. This patch fixes the issue for vanilla doors.
            // Doors Expanded doors are fixed in Building_DoorExpanded.Tick.
            Patch(original: AccessTools.Method(typeof(Building_Door), nameof(Building_Door.Tick)),
                prefix: nameof(BuildingDoorTickPrefix),
                priority: Priority.VeryHigh);

            // Patch CompBreakdownable to consider CompProperties_BreakdownableCustom.
            Patch(original: AccessTools.Method(typeof(CompBreakdownable), nameof(CompBreakdownable.CheckForBreakdown)),
                transpiler: nameof(CompBreakdownableCheckForBreakdownTranspiler),
                transpilerRelated: nameof(CompBreakdownableMTBUnit));

            // Backwards compatibility patches.
            Patch(original: AccessTools.Method(typeof(BackCompatibility), nameof(BackCompatibility.GetBackCompatibleType)),
                prefix: nameof(DoorExpandedGetBackCompatibleType),
                priority: Priority.VeryHigh);
            Patch(original: AccessTools.Method(typeof(BackCompatibility), nameof(BackCompatibility.CheckSpawnBackCompatibleThingAfterLoading)),
                prefix: nameof(DoorExpandedCheckSpawnBackCompatibleThingAfterLoading),
                priority: Priority.VeryHigh);
            Patch(original: AccessTools.Method(typeof(GenTypes), nameof(GenTypes.GetTypeNameWithoutIgnoredNamespaces)),
                prefix: nameof(DoorExpandedGetTypeNameWithoutIgnoredNamespacesPrefix),
                priority: Priority.VeryHigh);

            // Following isn't actually a Harmony patch, but bundling this patch here anyway.
            DefaultDoorMassPatch();
        }

        private static void Patch(MethodInfo original, string prefix = null, string postfix = null, string transpiler = null,
            string transpilerRelated = null, int priority = Priority.Normal, string[] before = null, string[] after = null,
            bool? debug = null)
        {
            DebugInspectorPatches.RegisterPatch(prefix);
            DebugInspectorPatches.RegisterPatch(postfix);
            DebugInspectorPatches.RegisterPatch(transpilerRelated);
            harmony.Patch(original,
                NewHarmonyMethod(prefix, priority, before, after, debug),
                NewHarmonyMethod(postfix, priority, before, after, debug),
                NewHarmonyMethod(transpiler, priority, before, after, debug));
        }

        private static HarmonyMethod NewHarmonyMethod(string methodName, int priority, string[] before, string[] after, bool? debug)
        {
            if (methodName == null)
                return null;
            return new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), methodName), priority, before, after, debug);
        }

        private static IEnumerable<MethodInfo> FindLambdaMethods(this Type type, string parentMethodName, Type returnType,
            Func<MethodInfo, bool> predicate = null)
        {
            // A lambda on RimWorld 1.0 on Unity 5.6.5f1 (mono .NET Framework 3.5 equivalent) is compiled into
            // a CompilerGenerated-attributed non-public inner type with name prefix "<{parentMethodName}>"
            // including an instance method with name prefix "<>".
            // A lambda on RimWorld 1.1 on Unity 2019.2.17f1 (mono .NET Framework 4.7.2 equivalent) is compiled into
            // a CompilerGenerated-attributed non-public inner type with name prefix "<>"
            // including an instance method with name prefix "<{parentMethodName}>".
            // Recent-ish versions of Visual Studio also compile this way.
            // So to be generic enough, return methods of a declaring inner type that:
            // a) either the method or the declaring inner type has name prefix "<{parentMethodName}>".
            // b) has given return type
            // c) satisfies predicate, if given
            var innerTypes = type.GetNestedTypes(AccessTools.all)
                .Where(innerType => innerType.IsDefined(typeof(CompilerGeneratedAttribute)));
            var foundMethod = false;
            foreach (var innerType in innerTypes)
            {
                if (innerType.Name.StartsWith("<" + parentMethodName + ">", StringComparison.Ordinal))
                {
                    foreach (var method in innerType.GetMethods(AccessTools.allDeclared))
                    {
                        if (method.Name.StartsWith("<", StringComparison.Ordinal) &&
                            method.ReturnType == returnType && (predicate == null || predicate(method)))
                        {
                            foundMethod = true;
                            yield return method;
                        }
                    }
                }
                else if (innerType.Name.StartsWith("<", StringComparison.Ordinal))
                {
                    foreach (var method in innerType.GetMethods(AccessTools.allDeclared))
                    {
                        if (method.Name.StartsWith("<" + parentMethodName + ">", StringComparison.Ordinal) &&
                            method.ReturnType == returnType && (predicate == null || predicate(method)))
                        {
                            foundMethod = true;
                            yield return method;
                        }
                    }
                }
            }
            if (!foundMethod)
            {
                throw new ArgumentException($"Could not find any lambda method for type {type} and method {parentMethodName}" +
                    " that satisfies given predicate");
            }
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

        // Building_Door.TicksTillFullyOpened
        public static bool InvisDoorTicksTillFullyOpenedPrefix(Building_Door __instance, ref int __result)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(InvisDoorTicksTillFullyOpenedPrefix));
            if (__instance is Building_DoorRegionHandler invisDoor)
            {
                __result = invisDoor.ParentDoor?.TicksTillFullyOpened ?? 0;
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
            if (__instance is Building_DoorRegionHandler invisDoor)
            {
                invisDoor.ParentDoor?.StartManualOpenBy(opener);
                return false;
            }
            return true;
        }

        // Building_Door.StartManualCloseBy
        public static bool InvisDoorStartManualCloseByPrefix(Building_Door __instance, Pawn closer)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(InvisDoorStartManualCloseByPrefix));
            if (__instance is Building_DoorRegionHandler invisDoor)
            {
                invisDoor.ParentDoor?.StartManualCloseBy(closer);
                return false;
            }
            return true;
        }

        // GenStep_Terrain.Generate
        // GenGrid.CanBeSeenOver
        // GridsUtility.Filled
        // Projectile.ImpactSomething
        // SectionLayer_IndoorMask.HideRainPrimary
        public static IEnumerable<CodeInstruction> InvisDoorDefFillageTranspiler(
            IEnumerable<CodeInstruction> instructions) =>
            GetActualDoorForDefTranspiler(instructions,
                AccessTools.PropertyGetter(typeof(ThingDef), nameof(ThingDef.Fillage)));

        // FloodFillerFog.FloodUnfog
        // FogGrid.FloodUnfogAdjacent
        public static IEnumerable<CodeInstruction> InvisDoorDefMakeFogTranspiler(IEnumerable<CodeInstruction> instructions) =>
            GetActualDoorForDefTranspiler(instructions,
                AccessTools.PropertyGetter(typeof(ThingDef), nameof(ThingDef.MakeFog)));

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
        public static IEnumerable<CodeInstruction> InvisDoorCalculatedCostAtTranspiler(IEnumerable<CodeInstruction> instructions,
            MethodBase method, ILGenerator ilGen)
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
            var locals = new Locals(method, ilGen);

            var searchIndex = 0;
            var firstDoorVar = GetIsinstDoorVar(locals, instructionList, ref searchIndex);
            var secondDoorVar = GetIsinstDoorVar(locals, instructionList, ref searchIndex);
            var condBranchToAfterFlagIndex = instructionList.FindIndex(searchIndex, instr => instr.operand is Label);
            var afterFlagLabel = (Label)instructionList[condBranchToAfterFlagIndex].operand;
            instructionList.SafeInsertRange(condBranchToAfterFlagIndex + 1, new[]
            {
                firstDoorVar.ToLdloc(),
                new CodeInstruction(OpCodes.Call, methodof_GetActualDoor),
                secondDoorVar.ToLdloc(),
                new CodeInstruction(OpCodes.Call, methodof_GetActualDoor),
                new CodeInstruction(OpCodes.Beq, afterFlagLabel),
            });

            return instructionList;
        }

        // Get x from instruction sequence: ldloc.s <x>; isinst Building_Door.
        private static LocalVar GetIsinstDoorVar(Locals locals, List<CodeInstruction> instructionList, ref int startIndex)
        {
            var isinstDoorIndex = instructionList.FindIndex(startIndex, IsinstDoorInstruction);
            startIndex = isinstDoorIndex + 1;
            return locals.FromLdloc(instructionList[isinstDoorIndex - 1]);
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

        // Room.Notify_ContainedThingSpawnedOrDespawned
        public static bool InvisDoorRoomNotifyContainedThingSpawnedOrDespawnedPrefix(Thing th)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(InvisDoorRoomNotifyContainedThingSpawnedOrDespawnedPrefix));
            // If an invis door's DeSpawn is somehow called before the parent door's DeSpawn is called,
            // this can result in a NRE since region links are cleared prematurely
            // (specifically RegionLink.GetOtherRegion can return null, unexpectedly for the algorithm).
            // So skip if the given Thing is an invis door, and let parent door's DeSpawn handle calling
            // Room.Notify_ContainedThingSpawnedOrDespawned for each invis door's Room after they're all despawned.
            return !(th.Spawned && th is Building_DoorRegionHandler);
        }

        // MouseoverReadout.MouseoverReadoutOnGUI
        public static IEnumerable<CodeInstruction> MouseoverReadoutTranspiler(IEnumerable<CodeInstruction> instructions,
            MethodBase method, ILGenerator ilGen)
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
            //      if (... && !thing.LabelMouseover.IsNullOrEmpty())
            //      {
            //          var rect = ...;
            //          var labelMouseover = thing.LabelMouseover;
            //          Widgets.Label(rect, labelMouseover);
            //          y += YInterval;
            //      }
            //  }

            var methodof_Entity_get_LabelMouseover =
                AccessTools.PropertyGetter(typeof(Entity), nameof(Entity.LabelMouseover));
            var instructionList = instructions.AsList();
            var locals = new Locals(method, ilGen);

            var labelMouseoverIndex = instructionList.FindIndex(instr => instr.Calls(methodof_Entity_get_LabelMouseover));
            // This relies on the fact that there's a conditional within the loop that acts as a loop continue,
            // and we're going to piggyback on that.
            var loopContinueLabelIndex = instructionList.FindIndex(labelMouseoverIndex + 1, instr => instr.labels.Count > 0);
            var loopContinueLabel = instructionList[loopContinueLabelIndex].labels[0];
            // Unfortunately, we can't simply do a brtrue to loopContinueLabel after a string.IsNullOrEmpty check,
            // since we there is at least the LabelMouseover value that needs to be popped from the CIL stack.
            // While we can deduce the exact amount of stack pops needed, this is fragile, so we insert the check
            // right after the afore-mentioned conditional, such that there's no need for stack pops.
            var loopContinueBranchIndex = instructionList.FindLastIndex(labelMouseoverIndex - 1,
                instr => instr.OperandIs(loopContinueLabel));
            // We also need the var that has the Thing on which Entity.LabelMouseover is called.
            // Assume this is just a ldloc(.s) right before the Entity.LabelMouseover callvirt.
            var thingVar = locals.FromLdloc(instructionList[labelMouseoverIndex - 1]);

            instructionList.SafeInsertRange(loopContinueBranchIndex + 1, new[]
            {
                thingVar.ToLdloc(),
                new CodeInstruction(OpCodes.Callvirt, methodof_Entity_get_LabelMouseover),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(string), nameof(string.IsNullOrEmpty))),
                new CodeInstruction(OpCodes.Brtrue, loopContinueLabel),
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
            IEnumerable<CodeInstruction> instructions, MethodBase method, ILGenerator ilGen)
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
            var locals = new Locals(method, ilGen);
            var adjCellsVar = locals.DeclareLocal(typeof(IntVec3[]));
            //void DebugInstruction(string label, int index)
            //{
            //    Log.Message($"{label} @ {index}: " +
            //        ((index >= 0 && index < instructionList.Count) ? instructionList[index].ToString() : "invalid index"));
            //}

            var twoWayArgIndex = instructionList.FindIndex(instr => instr.opcode == OpCodes.Ldarg_2);
            // Assume the next brfalse(.s) operand is a label to the twoWay=false branch.
            var twoWayArgFalseBranchIndex = instructionList.FindIndex(twoWayArgIndex + 1,
                instr => instr.IsBrfalse());
            var twoWayArgFalseLabel = (Label)instructionList[twoWayArgFalseBranchIndex].operand;
            var twoWayArgFalseIndex = instructionList.FindIndex(twoWayArgFalseBranchIndex + 1,
                instr => instr.labels.Contains(twoWayArgFalseLabel));
            // Assume next stloc(.s) is storing to the loop index var.
            var loopIndexVar = locals.FromStloc(instructionList[instructionList.FindIndex(twoWayArgFalseIndex + 1,
                instr => locals.IsStloc(instr))]);

            var newInstructions = new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0), // Building b
                new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(HarmonyPatches), nameof(GetAdjacentCellsForTemperature))),
                adjCellsVar.ToStloc(),
            };
            instructionList.SafeInsertRange(twoWayArgFalseIndex, newInstructions);

            var buildingArgIndex = instructionList.FindIndex(twoWayArgFalseIndex + newInstructions.Length,
                instr => instr.opcode == OpCodes.Ldarg_0);
            var currentCellStoreIndex = instructionList.FindIndex(buildingArgIndex + 1,
                instr => locals.IsStloc(instr));
            newInstructions = new[]
            {
                adjCellsVar.ToLdloc(),
                loopIndexVar.ToLdloc(),
                new CodeInstruction(OpCodes.Ldelem, typeof(IntVec3)),
            };
            instructionList.SafeReplaceRange(buildingArgIndex, currentCellStoreIndex - buildingArgIndex, newInstructions);

            var loopEndIndexIndex = instructionList.FindIndex(buildingArgIndex + newInstructions.Length,
                instr => instr.opcode == OpCodes.Ldc_I4_4);
            instructionList.SafeReplaceRange(loopEndIndexIndex, 1, new[]
            {
                adjCellsVar.ToLdloc(),
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

        // Thing.SetFactionDirect
        private static bool DoorExpandedSetFactionDirectPrefix(Thing __instance, Faction newFaction)
        {
            if (__instance is Building_DoorExpanded doorEx)
            {
                // Simulate virtual call. This also involves preventing infinite loop from "override" method calling "base" method.
                if (setFactionDirectAlreadyCalled.TryAdd(__instance, true))
                {
                    try
                    {
                        doorEx.SetFactionDirect(newFaction);
                    }
                    finally
                    {
                        setFactionDirectAlreadyCalled.TryRemove(__instance, out _);
                    }
                    return false;
                }
            }
            return true;
        }

        private static readonly ConcurrentDictionary<Thing, bool> setFactionDirectAlreadyCalled = new ConcurrentDictionary<Thing, bool>();

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
                        new CodeInstruction(OpCodes.Dup),
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
                    instructionList.SafeReplaceRange(placingRotFieldIndex, rotateIndex - placingRotFieldIndex + 1,
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
            if (placingRot == Rot4.South && designatorPlace.PlacingDef.GetDoorExpandedProps() is CompProperties_DoorExpanded doorExProps &&
                !doorExProps.rotatesSouth)
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
            if (thingDef.GetDoorExpandedProps() is CompProperties_DoorExpanded doorExProps)
            {
                // Always delegate door expanded graphics to our custom code.
                for (var i = 0; i < 2; i++)
                {
                    Building_DoorExpanded.Draw(thingDef, doorExProps, graphic.MatAt(rot), loc, rot, percentOpen: 0, flipped: i != 0);
                    if (doorExProps.singleDoor)
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

            var methodof_ThingDef_IsDoor = AccessTools.PropertyGetter(typeof(ThingDef), nameof(ThingDef.IsDoor));
            var instructionList = instructions.AsList();

            var isDoorIndex = instructionList.FindIndex(instr => instr.Calls(methodof_ThingDef_IsDoor));
            // Assume prev instruction is ldarg(.s) or ldloc(.s) for thingDef argument.
            var thingDefLoad = instructionList[isDoorIndex - 1];
            // Assume the next brfalse(.s) operand is a label that skips the Graphic_Single code path.
            var skipGraphicSingleBranchIndex = instructionList.FindIndex(isDoorIndex + 1,
                instr => instr.IsBrfalse());
            var skipGraphicSingleLabel = (Label)instructionList[skipGraphicSingleBranchIndex].operand;
            // Note: Not using SafeInsertRange, since we want labels to stay at skipGraphicSingleBranchIndex + 1.
            instructionList.InsertRange(skipGraphicSingleBranchIndex + 1, new[]
            {
                thingDefLoad.Clone(),
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
            if (blueprint.def.entityDefToBuild is ThingDef thingDef &&
                thingDef.GetCompProperties<CompProperties_DoorExpanded>() is CompProperties_DoorExpanded doorExProps)
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
                    Building_DoorExpanded.DoorRotationAt(thingDef, doorExProps, blueprint.Position, blueprint.Rotation, map);
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
            if (blueprint.def.entityDefToBuild is ThingDef thingDef &&
                thingDef.GetCompProperties<CompProperties_DoorExpanded>() is CompProperties_DoorExpanded doorExProps)
            {
                // Always delegate door expanded graphics to our custom code.
                var drawPos = blueprint.DrawPos;
                var rotation = blueprint.Rotation;
                rotation = Building_DoorExpanded.DoorRotationAt(thingDef, doorExProps, blueprint.Position, rotation, blueprint.Map);
                blueprint.Rotation = rotation;
                var material = blueprint.Graphic.MatAt(rotation);
                for (var i = 0; i < 2; i++)
                {
                    Building_DoorExpanded.Draw(thingDef, doorExProps, material, drawPos, rotation, percentOpen: 0, flipped: i != 0);
                    if (doorExProps.singleDoor)
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
        private static readonly object[] emptyObjArray = Array.Empty<object>();

        // FloatMenuMakerMap.AddJobGiverWorkOrders
        public static IEnumerable<CodeInstruction> DoorRemoteAddJobGiverWorkOrdersTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            // Workaround to remove the "Prioritize" prefix for the remote press/flip job in the float menu.
            // This transforms the following code:
            //  TranslatorFormattedStringExtensions.Translate("PrioritizeGeneric", ...)
            // into:
            //  TranslateRemovePrioritizeJobLabelPrefix("PrioritizeGeneric".Translate(...))

            var methodof_TranslatorFormattedStringExtensions_Translate =
                AccessTools.Method(typeof(TranslatorFormattedStringExtensions), nameof(TranslatorFormattedStringExtensions.Translate),
                    new[] { typeof(string), typeof(NamedArgument), typeof(NamedArgument) });
            var enumerator = instructions.GetEnumerator();

            while (enumerator.MoveNext())
            {
                var instruction = enumerator.Current;
                yield return instruction;
                if (instruction.Is(OpCodes.Ldstr, "PrioritizeGeneric"))
                    break;
            }

            while (enumerator.MoveNext())
            {
                var instruction = enumerator.Current;
                if (instruction.Calls(methodof_TranslatorFormattedStringExtensions_Translate))
                    break;
                if (instruction.IsLdloc())
                    yield return instruction;
            }

            yield return new CodeInstruction(OpCodes.Call,
                AccessTools.Method(typeof(HarmonyPatches), nameof(TranslateCustomizeUseDoorRemoteJobLabel)));

            while (enumerator.MoveNext())
            {
                yield return enumerator.Current;
            }
        }

        private static TaggedString TranslateCustomizeUseDoorRemoteJobLabel(string translationKey, WorkGiver_Scanner scanner,
            Job job, Thing thing)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(TranslateCustomizeUseDoorRemoteJobLabel));
            if (scanner is WorkGiver_UseRemoteButton)
                return "PH_UseButtonOrLever".Translate(thing.Label);
            // Following is copied from FloatMenuMakerMap.AddJobGiverWorkOrders.
            return translationKey.Translate(scanner.PostProcessedGerund(job), thing.Label);
        }

        // Building_Door.Tick
        public static bool BuildingDoorTickPrefix(Building_Door __instance)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(BuildingDoorTickPrefix));
            return __instance.Spawned;
        }

        // CompBreakdownable.CheckForBreakdown
        public static IEnumerable<CodeInstruction> CompBreakdownableCheckForBreakdownTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            // This transforms the following code:
            //  Rand.MTBEventOccurs(..., 1f, ...)
            // into:
            //  Rand.MTBEventOccurs(..., CompBreakdownableMTBUnit(this.props), ...)

            foreach (var instruction in instructions)
            {
                if (instruction.Is(OpCodes.Ldc_R4, 1f))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldfld,
                        AccessTools.Field(typeof(ThingComp), nameof(ThingComp.props)));
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(HarmonyPatches), nameof(CompBreakdownableMTBUnit)));
                }
                else
                {
                    yield return instruction;
                }
            }
        }

        private static float CompBreakdownableMTBUnit(CompProperties compProps)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(CompBreakdownableMTBUnit));
            return compProps is CompProperties_BreakdownableCustom custom ? custom.breakdownMTBUnit : 1f;
        }

        // BackCompatibility.GetBackCompatibleType
        public static bool DoorExpandedGetBackCompatibleType(Type baseType, string providedClassName, XmlNode node, ref Type __result)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(DoorExpandedGetBackCompatibleType));
            // To accommodate changes in the specific Building_DoorExpanded class
            // (like blast doors from Building_DoorExpanded to Building_DoorRemote, or autodoors from Building_Door to Building_DoorRemote),
            // always return a door's actual def's thingClass (which should by set by CompProperties_DoorExpanded).
            if (baseType == typeof(Thing) && node["def"] is XmlNode defNode &&
                (providedClassName == Building_Door_FullName || providedClassName == Building_DoorExpanded_FullName))
            {
                __result = DefDatabase<ThingDef>.GetNamedSilentFail(defNode.InnerText).thingClass;
                return false;
            }
            return true;
        }

        private static readonly string Building_Door_FullName = typeof(Building_Door).Name; // omit "RimWorld." prefix
        private static readonly string Building_DoorExpanded_FullName = typeof(Building_DoorExpanded).FullName;

        // BackCompatibility.CheckSpawnBackCompatibleThingAfterLoading
        public static bool DoorExpandedCheckSpawnBackCompatibleThingAfterLoading(Thing thing, ref bool __result)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(DoorExpandedCheckSpawnBackCompatibleThingAfterLoading));
            // If invis doors somehow become minified (there are reports this can somehow happen with the MinifyEverything mod),
            // destroy them when loading them.
            if (thing is MinifiedThing minifiedThing)
            {
                var innerContainer = minifiedThing.GetDirectlyHeldThings();
                List<Thing> invisDoors = null;
                innerContainer.RemoveAll(innerThing =>
                {
                    if (innerThing.def == HeronDefOf.HeronInvisibleDoor)
                    {
                        invisDoors ??= new List<Thing>();
                        invisDoors.Add(innerThing);
                        return true;
                    }
                    return false;
                });
                if (invisDoors != null && innerContainer.Count == 0)
                {
                    minifiedThing.Destroy();
                    __result = true; // true => avoid spawning
                    Log.Warning($"[Doors Expanded] Found and destroyed minified invis door(s) during loading: " + invisDoors.ToStringSafeEnumerable());
                    return false;
                }
            }
            return true;
        }

        // GenTypes.GetTypeNameWithoutIgnoredNamespaces
        public static bool DoorExpandedGetTypeNameWithoutIgnoredNamespacesPrefix(Type type, ref string __result)
        {
            DebugInspectorPatches.RegisterPatchCalled(nameof(DoorExpandedGetTypeNameWithoutIgnoredNamespacesPrefix));
            // Ensure doors patched to have thingClass be Building_DoorExpanded or subclass, which includes door defs with
            // CompProperties_DoorExpanded, are saved with Class="Building_Door" in the save file, such that if Doors Expanded
            // is removed by the user, such doors revert to vanilla behavior (rather than disappearing altogether). When loading a save,
            // the above DoorExpandedGetBackCompatibleType patch converts Building_Door to the proper Building_DoorExpanded type.
            // Note: GenTypes.GetTypeNameWithoutIgnoredNamespaces is being patched rather than its caller
            // Scribe_Deep.Look<T>(ref T, bool, string, params object[]) since the latter can't currently be patched by Harmony
            // (generic argument limitations). One consequence of this that only the door's Type is available, not the door itself,
            // so we can't just filter for only one-cell patched doors such as autodoors (unless it's encoded in the door's Type itself).
            if (typeof(Building_DoorExpanded).IsAssignableFrom(type))
            {
                __result = nameof(Building_Door);
                return false;
            }
            return true;
        }

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
                    instructionList.SafeReplaceRange(isinstDoorIndex, doorOpenIndex - isinstDoorIndex + 1, new[]
                    {
                        new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(HarmonyPatches), nameof(IsOpenDoor))),
                    });
                    nextIsinstDoorIndex = instructionList.FindIndex(searchIndex, IsinstDoorInstruction);
                }
                isinstDoorIndex = nextIsinstDoorIndex;
            }

            return instructionList;
        }

        private static readonly MethodInfo methodof_Building_Door_get_Open =
            AccessTools.PropertyGetter(typeof(Building_Door), nameof(Building_Door.Open));

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

        public const float DefaultDoorMass = 20f;

        public static void DefaultDoorMassPatch()
        {
            // Although doors, including our custom doors, aren't uninstallable (minifiable) by default,
            // we're defining masses for custom doors, so we might as well define a default for all doors
            // if another mod hasn't already defined one yet.
            // This way, if another mod like MinifyEverything does make doors uninstallable, they'll have a reasonable mass
            // that looks consistent with the mass of our custom doors.
            // This patch is done in code during StaticConstructorOnStartup rather than an XML patch since:
            // a) A later-in-load-order mod's XML patch that also patches door mass without checking if it already exists,
            //    would result in an error, albeit a harmless one.
            // b) StaticConstructorOnStartup happens after XML patches for all mods are applied
            //    and after ResolveReferences code for all mods is run, helping ensure default is only applied when necessary.
            foreach (var thingDef in DefDatabase<ThingDef>.AllDefs)
            {
                if (typeof(Building_Door).IsAssignableFrom(thingDef.thingClass) &&
                    thingDef.thingClass != typeof(Building_DoorRegionHandler) &&
                    !thingDef.statBases.StatListContains(StatDefOf.Mass))
                {
                    StatUtility.SetStatValueInList(ref thingDef.statBases, StatDefOf.Mass, DefaultDoorMass);
                }
            }
        }
    }
}
