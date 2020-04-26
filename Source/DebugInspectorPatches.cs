using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace DoorsExpanded
{
    public static class MoreDebugViewSettings
    {
#pragma warning disable CS0649
        public static bool writeTemperature;
        public static bool writeEdificeGrid;
        public static bool writeDoors;
        public static bool writePatchCallRegistry;
#pragma warning restore CS0649
    }

    public static class DebugInspectorPatches
    {
        public static void PatchDebugInspector()
        {
            var harmony = HarmonyPatches.harmony;
            harmony.Patch(original: AccessTools.Method(typeof(Dialog_DebugSettingsMenu), "DoListingItems"),
                transpiler: new HarmonyMethod(typeof(DebugInspectorPatches), nameof(DebugSettingsMenuDoListingItemsTranspiler)));
            harmony.Patch(original: AccessTools.Method(typeof(EditWindow_DebugInspector), "CurrentDebugString"),
                transpiler: new HarmonyMethod(typeof(DebugInspectorPatches), nameof(EditWindowDebugInspectorTranspiler)));
            harmony.Patch(original: AccessTools.Method(typeof(Room), "DebugString"),
                postfix: new HarmonyMethod(typeof(DebugInspectorPatches), nameof(RoomMoreDebugString)));
        }

        private static Dictionary<string, bool> patchCallRegistry;

        // Note: Callers of RegisterPatch and RegisterPatchCalled need to have #define PATCH_CALL_REGISTRY in their file,
        // since its callers of Conditional-attributed methods that are ellided rather than the method itself.
        [Conditional("PATCH_CALL_REGISTRY")]
        public static void RegisterPatch(string name)
        {
            if (name != null)
            {
                if (patchCallRegistry == null)
                    patchCallRegistry = new Dictionary<string, bool>();
                patchCallRegistry[name] = false;
            }
        }

        [Conditional("PATCH_CALL_REGISTRY")]
        public static void RegisterPatchCalled(string name) => patchCallRegistry[name] = true;

        // Dialog_DebugSettingsMenu.DoListingItems
        public static IEnumerable<CodeInstruction> DebugSettingsMenuDoListingItemsTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var enumerator = instructions.GetEnumerator();

            var instruction = default(CodeInstruction);
            while (enumerator.MoveNext())
            {
                instruction = enumerator.Current;
                yield return instruction;
                if (instruction.OperandIs(typeof(DebugViewSettings)))
                    break;
            }

            var prevInstruction = instruction;
            while (enumerator.MoveNext())
            {
                instruction = enumerator.Current;
                yield return instruction;
                if (instruction.Calls(methodof_Dialog_DebugSettingsMenu_DoField))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0); // Dialog_DebugSettingsMenu instance
                    yield return prevInstruction.Clone(); // assumed to be ldloc(.s) for the current field
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(DebugInspectorPatches), nameof(DoListingMoreDebugViewSettings)));
                    break;
                }
                prevInstruction = instruction;
            }

            while (enumerator.MoveNext())
            {
                yield return enumerator.Current;
            }
        }

        private static void DoListingMoreDebugViewSettings(Dialog_DebugSettingsMenu menu, FieldInfo currentField)
        {
            if (currentField == fieldof_DebugViewSettings_writePathCosts)
            {
                foreach (var field in typeof(MoreDebugViewSettings).GetFields())
                {
                    if (!(field == fieldof_MoreDebugViewSettings_writePatchCallRegistry && patchCallRegistry == null))
                        methodof_Dialog_DebugSettingsMenu_DoField.Invoke(menu, new object[] { field });
                }
            }
        }

        private static readonly MethodInfo methodof_Dialog_DebugSettingsMenu_DoField =
            AccessTools.Method(typeof(Dialog_DebugSettingsMenu), "DoField");
        private static readonly FieldInfo fieldof_DebugViewSettings_writePathCosts =
            AccessTools.Field(typeof(DebugViewSettings), nameof(DebugViewSettings.writePathCosts));
        private static readonly FieldInfo fieldof_DebugViewSettings_drawRooms =
            AccessTools.Field(typeof(DebugViewSettings), nameof(DebugViewSettings.drawRooms));
        private static readonly FieldInfo fieldof_MoreDebugViewSettings_writePatchCallRegistry =
            AccessTools.Field(typeof(MoreDebugViewSettings), nameof(MoreDebugViewSettings.writePatchCallRegistry));

        // EditWindow_DebugInspector.CurrentDebugString
        public static IEnumerable<CodeInstruction> EditWindowDebugInspectorTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var methodof_UI_MouseCell = AccessTools.Method(typeof(UI), nameof(UI.MouseCell));
            var methodof_object_ToString = AccessTools.Method(typeof(object), nameof(ToString));
            var instructionList = instructions.AsList();

            var mouseCellIndex = instructionList.FindIndex(instr => instr.Calls(methodof_UI_MouseCell));
            var mouseCellVar = (LocalBuilder)instructionList[mouseCellIndex + 1].operand;

            var mouseCellToStringIndex = instructionList.FindSequenceIndex(
                instr => instr.IsLdloc(mouseCellVar),
                instr => instr.Is(OpCodes.Constrained, typeof(IntVec3)),
                instr => instr.Calls(methodof_object_ToString));
            instructionList.ReplaceRange(mouseCellToStringIndex, 3, new[]
            {
                new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(DebugInspectorPatches), nameof(MousePositionToString))),
            });

            var writePathCostsFlagIndex = instructionList.FindIndex(
                instr => instr.LoadsField(fieldof_DebugViewSettings_writePathCosts));
            var nextFlagIndex = instructionList.FindIndex(writePathCostsFlagIndex + 1, IsDebugViewSettingFlagAccess);
            instructionList.InsertRange(nextFlagIndex - 1, new[]
            {
                new CodeInstruction(OpCodes.Ldloc_0), // StringBuilder
                new CodeInstruction(OpCodes.Ldloc_S, mouseCellVar),
                new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(DebugInspectorPatches), nameof(WriteMorePathCostsDebugOutput))),
                new CodeInstruction(OpCodes.Ldloc_0) { labels = instructionList[nextFlagIndex].labels.PopAll() },
                new CodeInstruction(OpCodes.Ldloc_S, mouseCellVar),
                new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(DebugInspectorPatches), nameof(WriteMoreDebugOutput))),
            });

            return instructionList;
        }

        private static string MousePositionToString()
        {
            var mousePos = UI.MouseMapPosition();
            return $"({mousePos.x:000.00}, {mousePos.z:000.00})";
        }

        private static bool IsDebugViewSettingFlagAccess(CodeInstruction instruction) =>
            instruction.opcode == OpCodes.Ldsfld && ((FieldInfo)instruction.operand).DeclaringType == typeof(DebugViewSettings);

        private static void WriteMorePathCostsDebugOutput(StringBuilder debugString, IntVec3 mouseCell)
        {
            if (Find.Selector.SingleSelectedObject is Pawn pawn)
            {
                debugString.AppendLine($"CalculatedCostAt(this, false, p.Position): " +
                    pawn.Map.pathGrid.CalculatedCostAt(mouseCell, perceivedStatic: false, pawn.Position));
                debugString.AppendLine($"CostToMoveIntoCell({pawn}, this): {CostToMoveIntoCell(pawn, mouseCell)}");
            }
        }

        private static readonly Func<Pawn, IntVec3, int> CostToMoveIntoCell =
            (Func<Pawn, IntVec3, int>)Delegate.CreateDelegate(typeof(Func<Pawn, IntVec3, int>),
                AccessTools.Method(typeof(Pawn_PathFollower), "CostToMoveIntoCell", new[] { typeof(Pawn), typeof(IntVec3) }));

        private static void WriteMoreDebugOutput(StringBuilder debugString, IntVec3 mouseCell)
        {
            var map = Find.CurrentMap;

            if (MoreDebugViewSettings.writeTemperature)
            {
                debugString.AppendLine("---");
                debugString.AppendLine("Temperature: " +
                    (GenTemperature.TryGetTemperatureForCell(mouseCell, map, out var temperature) ? $"{temperature:f1}" : ""));
            }

            if (MoreDebugViewSettings.writeEdificeGrid)
            {
                debugString.AppendLine("---");
                debugString.AppendLine("Building at edifice grid: " + map.edificeGrid[mouseCell]);
            }

            if (MoreDebugViewSettings.writeDoors)
            {
                debugString.AppendLine("---");
                var pawn = Find.Selector.SingleSelectedObject as Pawn;
                if (pawn != null)
                {
                    debugString.AppendLine("From selected pawn " + pawn + " to door");
                    debugString.AppendLine("- CaresAboutForbidden(p, false): " + ForbidUtility.CaresAboutForbidden(pawn, false));
                    //debugString.AppendLine("- mindState.maxDistToSquadFlag: " + pawn.mindState.maxDistToSquadFlag);
                }

                foreach (var thing in map.thingGrid.ThingsAt(mouseCell))
                {
                    if (thing is Building_Door door)
                    {
                        debugString.AppendLine("Door: " + door);
                        debugString.AppendLine("- Open: " + door.Open);
                        debugString.AppendLine("- HoldOpen: " + door.HoldOpen);
                        debugString.AppendLine("- FreePassage: " + door.FreePassage);
                        debugString.AppendLine("- WillCloseSoon: " + door.WillCloseSoon);
                        debugString.AppendLine("- BlockedOpenMomentary: " + door.BlockedOpenMomentary);
                        debugString.AppendLine("- SlowsPawns: " + door.SlowsPawns);
                        debugString.AppendLine("- TicksToOpenNow: " + door.TicksToOpenNow);
                        debugString.AppendLine("- FriendlyTouchedRecently: " +
                            methodof_Building_Door_FriendlyTouchedRecently.Invoke(door, Array.Empty<object>()));
                        debugString.AppendLine("- lastFriendlyTouchTick: " + Building_Door_lastFriendlyTouchTick(door));
                        debugString.AppendLine("- ticksUntilClose: " + Building_Door_ticksUntilClose(door));
                        debugString.AppendLine("- ticksSinceOpen: " + Building_Door_ticksSinceOpen(door));
                        debugString.AppendLine("- IsForbidden(player): " + door.IsForbidden(Faction.OfPlayer));
                        debugString.AppendLine("- def.Fillage: " + door.def.Fillage);
                        debugString.AppendLine("- CanBeSeenOver: " + door.CanBeSeenOver());
                        debugString.AppendLine("- BaseBlockChance: " + door.BaseBlockChance());

                        if (pawn != null)
                        {
                            debugString.AppendLine("- For selected pawn: " + pawn);
                            debugString.AppendLine("  - CanPhysicallyPass(p): " + door.CanPhysicallyPass(pawn));
                            debugString.AppendLine("  - PawnCanOpen(p): " + door.PawnCanOpen(pawn));
                            debugString.AppendLine("  - BlocksPawn(p): " + door.BlocksPawn(pawn));
                            debugString.AppendLine("  - p.HostileTo(this): " + pawn.HostileTo(door));
                            debugString.AppendLine("  - PathWalkCostFor(p): " + door.PathWalkCostFor(pawn));
                            debugString.AppendLine("  - IsDangerousFor(p): " + door.IsDangerousFor(pawn));
                            debugString.AppendLine("  - IsForbidden(p): " + door.IsForbidden(pawn));
                            debugString.AppendLine("  - Position.IsForbidden(p): " + door.Position.IsForbidden(pawn));
                            debugString.AppendLine("  - Position.InAllowedArea(p): " + door.Position.InAllowedArea(pawn));
                            //debugString.AppendLine("  - Position.InHorDistOf(p.DutyLocation,p.mindState.maxDistToSquadFlag): " +
                            //    (pawn.mindState.maxDistToSquadFlag > 0f ?
                            //        door.Position.InHorDistOf(pawn.DutyLocation(), pawn.mindState.maxDistToSquadFlag).ToString() :
                            //        "N/A"));
                            //debugString.AppendLine("  - IsForbidden(p.Faction): " + door.IsForbidden(pawn.Faction));
                            //debugString.AppendLine("  - IsForbidden(p.HostFaction): " + door.IsForbidden(pawn.HostFaction));
                            //debugString.AppendLine("  - pawn.Lord.extraForbiddenThings.Contains(this): " +
                            //    (pawn.GetLord()?.extraForbiddenThings?.Contains(door) ?? false));
                            debugString.AppendLine("  - IsForbiddenToPass(p): " + door.IsForbiddenToPass(pawn));
                        }

                        if (door is Building_DoorRegionHandler invisDoor)
                        {
                            var parentDoor = invisDoor.ParentDoor;
                            debugString.AppendLine("- ParentDoor: " + parentDoor);
                            debugString.AppendLine("  - DrawPos: " + parentDoor.DrawPos);
                            debugString.AppendLine("  - debugDrawVectors.percentOpen: " + parentDoor.debugDrawVectors?.percentOpen);
                            debugString.AppendLine("  - debugDrawVectors.offsetVector: " + parentDoor.debugDrawVectors?.offsetVector);
                            debugString.AppendLine("  - debugDrawVectors.scaleVector: " + parentDoor.debugDrawVectors?.scaleVector);
                            debugString.AppendLine("  - debugDrawVectors.graphicVector: " + parentDoor.debugDrawVectors?.graphicVector);
                            debugString.AppendLine("  - Open: " + parentDoor.Open);
                            debugString.AppendLine("  - HoldOpen: " + parentDoor.HoldOpen);
                            debugString.AppendLine("  - FreePassage: " + parentDoor.FreePassage);
                            debugString.AppendLine("  - WillCloseSoon: " + parentDoor.WillCloseSoon);
                            debugString.AppendLine("  - BlockedOpenMomentary: " + parentDoor.BlockedOpenMomentary);
                            debugString.AppendLine("  - SlowsPawns: " + parentDoor.SlowsPawns);
                            debugString.AppendLine("  - TicksToOpenNow: " + parentDoor.TicksToOpenNow);
                            debugString.AppendLine("  - FriendlyTouchedRecently: " + parentDoor.FriendlyTouchedRecently);
                            debugString.AppendLine("  - lastFriendlyTouchTick: " + Building_DoorExpanded_lastFriendlyTouchTick(parentDoor));
                            debugString.AppendLine("  - ticksUntilClose: " + parentDoor.TicksUntilClose);
                            debugString.AppendLine("  - ticksSinceOpen: " + parentDoor.TicksSinceOpen);
                            debugString.AppendLine("  - Forbidden: " + parentDoor.Forbidden);
                            debugString.AppendLine("  - def.Fillage: " + parentDoor.def.Fillage);
                            debugString.AppendLine("  - CanBeSeenOver: " + parentDoor.CanBeSeenOver());
                            debugString.AppendLine("  - BaseBlockChance: " + parentDoor.BaseBlockChance());

                            if (parentDoor is Building_DoorRemote parentDoorRemote)
                            {
                                debugString.AppendLine("  - Button: " + parentDoorRemote.Button);
                                debugString.AppendLine("  - SecuredRemotely: " + parentDoorRemote.SecuredRemotely);
                                debugString.AppendLine("  - HoldOpenRemotely: " + parentDoorRemote.HoldOpenRemotely);
                                debugString.AppendLine("  - ForcedClosed: " + parentDoorRemote.ForcedClosed);
                            }

                            if (pawn != null)
                            {
                                debugString.AppendLine("  - For selected pawn: " + pawn);
                                //debugString.AppendLine("    - CanPhysicallyPass(p): " + parentDoor.CanPhysicallyPass(pawn));
                                debugString.AppendLine("    - PawnCanOpen(p): " + parentDoor.PawnCanOpen(pawn));
                                debugString.AppendLine("    - BlocksPawn(p): " + parentDoor.BlocksPawn(pawn));
                                debugString.AppendLine("    - p.HostileTo(this): " + pawn.HostileTo(parentDoor));
                                debugString.AppendLine("    - PathWalkCostFor(p): " + parentDoor.PathWalkCostFor(pawn));
                                debugString.AppendLine("    - IsDangerousFor(p): " + parentDoor.IsDangerousFor(pawn));
                                debugString.AppendLine("    - IsForbidden(p): " + parentDoor.IsForbidden(pawn));
                                debugString.AppendLine("    - Position.IsForbidden(p): " + parentDoor.Position.IsForbidden(pawn));
                                debugString.AppendLine("    - Position.InAllowedArea(p): " + parentDoor.Position.InAllowedArea(pawn));
                                //debugString.AppendLine("    - Position.InHorDistOf(p.DutyLocation,p.mindState.maxDistToSquadFlag): " +
                                //    (pawn.mindState.maxDistToSquadFlag > 0f ?
                                //        parentDoor.Position.InHorDistOf(pawn.DutyLocation(), pawn.mindState.maxDistToSquadFlag).ToString() :
                                //        "N/A"));
                                //debugString.AppendLine("    - IsForbidden(p.Faction): " + parentDoor.IsForbidden(pawn.Faction));
                                //debugString.AppendLine("    - IsForbidden(p.HostFaction): " + parentDoor.IsForbidden(pawn.HostFaction));
                                //debugString.AppendLine("    - pawn.Lord.extraForbiddenThings.Contains(this): " +
                                //    (pawn.GetLord()?.extraForbiddenThings?.Contains(parentDoor) ?? false));
                            }
                        }
                    }
                    else if (thing is Building_DoorRemoteButton remoteButton)
                    {
                        debugString.AppendLine("RemoteButton: " + remoteButton);
                        debugString.AppendLine("- LinkedDoors: " + remoteButton.LinkedDoors.ToStringSafeEnumerable());
                        debugString.AppendLine("- ButtonOn: " + remoteButton.ButtonOn);
                        debugString.AppendLine("- NeedsToBeSwitched: " + remoteButton.NeedsToBeSwitched);
                    }
                }
            }

            if (MoreDebugViewSettings.writePatchCallRegistry)
            {
                debugString.AppendLine("---");
                debugString.AppendLine("Harmony Patch Call Registry:");
                foreach (var pair in patchCallRegistry)
                    debugString.AppendLine($"- {pair.Key}: {pair.Value}");
            }
        }

        private static readonly MethodInfo methodof_Building_Door_FriendlyTouchedRecently =
            AccessTools.Property(typeof(Building_Door), "FriendlyTouchedRecently").GetGetMethod(true);
        private static readonly AccessTools.FieldRef<Building_Door, int> Building_Door_lastFriendlyTouchTick =
            AccessTools.FieldRefAccess<Building_Door, int>("lastFriendlyTouchTick");
        private static readonly AccessTools.FieldRef<Building_DoorExpanded, int> Building_DoorExpanded_lastFriendlyTouchTick =
            AccessTools.FieldRefAccess<Building_DoorExpanded, int>("lastFriendlyTouchTick");
        private static readonly AccessTools.FieldRef<Building_Door, int> Building_Door_ticksUntilClose =
            AccessTools.FieldRefAccess<Building_Door, int>("ticksUntilClose");
        private static readonly AccessTools.FieldRef<Building_Door, int> Building_Door_ticksSinceOpen =
            AccessTools.FieldRefAccess<Building_Door, int>("ticksSinceOpen");

        // Room.DebugString
        public static string RoomMoreDebugString(string result, Room __instance)
        {
            return result + "\n  Neighbors=\n  - " + __instance.Neighbors.Join(delimiter: "\n  - ");
        }
    }
}
