using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace DoorsExpanded
{
    /// <summary>
    ///
    /// Door Region Handler
    ///
    /// What: These are children doors spawned inside a larger door.
    ///
    /// Why: This class is used instead of rewriting the base RimWorld code for
    /// handling regions. Regions do not handle large doors well. So this class
    /// will add smaller, invisible doors, inside a bigger door.
    ///
    /// </summary>
    public class Building_DoorRegionHandler : Building_Door
    {
        private Building_DoorExpanded parentDoor;

        public Building_DoorExpanded ParentDoor
        {
            get => parentDoor;
            set => parentDoor = value;
        }

        // A harmony patch ensures that null or empty mouseover labels are not displayed.
        public override string LabelMouseover => null;

        public override string Label => parentDoor.Label;

        public override string LabelShort => parentDoor.LabelShort;

        // Following Building_Door fields are going to be synced with Building_DoorExpanded (handled in that class):
        // private bool openInt - since bool Open property is inlined and thus cannot be harmony patched

        // Other fields are ignored.

        // Following Building_Door non-virtual properties/methods are harmony patched to delegate to ParentDoor:
        // public bool FreePassage
        // public int TicksTillFullyOpened
        // public bool WillCloseSoon
        // - Note: Although only used by Building_Door.FreePassage, it could be used outside of vanilla code.
        // public bool BlockedOpenMomentary
        // public bool SlowsPawns
        // public int TicksToOpenNow
        // public void CheckFriendlyTouched(Pawn p)
        // public void Notify_PawnApproaching(Pawn p, int moveCost)
        // public bool CanPhysicallyPass(Pawn p)
        // protected void DoorOpen(int ticksToClose) - in case another subclass calls this
        // protected bool DoorTryClose() - in case another subclass calls this
        // public void StartManualOpenBy(Pawn opener)
        // public void StartManualCloseBy(Pawn closer)
        // - Note: This is inlined, so can only patch its caller Pawn_PathFollower.TryEnterNextPathCell

        // Following Building_Door methods are left as-is (they might have some extraneous yet non-harmful behavior, which is fine):
        // public override void PostMake()
        // public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)

        // Other methods are overriden here.

        public new bool Open
        {
            get => Building_Door_openInt(this);
            set => Building_Door_openInt(this) = value;
        }

        private static readonly AccessTools.FieldRef<Building_Door, bool> Building_Door_openInt =
            AccessTools.FieldRefAccess<Building_Door, bool>("openInt");

        private static readonly AccessTools.FieldRef<Building_Door, bool> Building_Door_holdOpenInt =
            AccessTools.FieldRefAccess<Building_Door, bool>("holdOpenInt");

        private static readonly AccessTools.FieldRef<Thing, IntVec3> Thing_positionInt =
            AccessTools.FieldRefAccess<Thing, IntVec3>("positionInt");

        public override bool FireBulwark => ParentDoor.FireBulwark;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            TLog.Log(this);
            // Building_Door.SpawnSetup calls BlockedOpenMomentary, which will be delegating to Building_DoorExpanded.
            // Since that Building_DoorExpanded may not be spawned yet, we want to avoid this.
            // Building_Door.SpawnSetup also calls ClearReachabilityCache, which is redundant with Building_DoorExpanded
            // (although not harmful).
            // So we skip calling Building_Door.SpawnSetup (via base.SpawnSetup) and instead call Building.SpawnSetup.
            var Building_SpawnSetup = (Action<Map, bool>)Activator.CreateInstance(typeof(Action<Map, bool>), this,
                methodof_Building_SpawnSetup.MethodHandle.GetFunctionPointer());
            Building_SpawnSetup(map, respawningAfterLoad);
        }

        private static readonly MethodInfo methodof_Building_SpawnSetup =
            AccessTools.Method(typeof(Building), nameof(Building.SpawnSetup));

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            TLog.Log(this);
            // This check is necessary to prevent errors during operations that despawn all things in the same cell,
            // since despawning/destroying parent doors also destroys their invis doors.
            if (Spawned)
                base.DeSpawn(mode);
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            TLog.Log(this);
            // This check is necessary to prevent errors during operations that destroy all things in the same cell,
            // since despawning/destroying parent doors also destroys their invis doors.
            if (!Destroyed)
                base.Destroy(mode);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            // This roundabout way of setting parentDoor is to handle the case where parentDoor is no longer a Building_DoorExpanded
            // (potentially due to XML def/patch changes), in which case, we invalidate the position to prevent spawning this invis door.
            var tempParentDoor = (ILoadReferenceable)parentDoor;
            Scribe_References.Look(ref tempParentDoor, nameof(parentDoor));
            parentDoor = tempParentDoor as Building_DoorExpanded;
            if (tempParentDoor != null && parentDoor == null)
            {
                if (TLog.Enabled)
                    TLog.Log(this, $"{this} has invalid parentDoor type {tempParentDoor.GetType()} - invalidating position to avoid spawning");
                Thing_positionInt(this) = IntVec3.Invalid;
            }
        }

        public override void SetFaction(Faction newFaction, Pawn recruiter = null)
        {
            // This check is necessary to prevent infinite loop between this method and Building_DoorExpanded.SetFaction.
            if (newFaction == Faction)
                return;
            base.SetFaction(newFaction, recruiter);
            ParentDoor.SetFaction(newFaction, recruiter);
        }

        public override void Tick()
        {
            // Sanity checks. These are inexpensive and thus done every tick.
            if (ParentDoor == null || !ParentDoor.Spawned)
            {
                var stateStr = ParentDoor == null ? "null" : ParentDoor.Destroyed ? "destroyed" : "unspawned";
                Log.Error($"{this}.ParentDoor is unexpectedly {stateStr} - destroying this");
                Destroy();
                return;
            }
            if (Faction != ParentDoor.Faction)
                SetFaction(ParentDoor.Faction);

            // Some mods (such as OpenedDoorsDontBlockLight) directly read ticksSinceOpen or ticksUntilClose fields,
            // since no public accessor exists for those fields, so for compatibility with such mods, copy them from parent door here.
            ticksSinceOpen = ParentDoor.TicksSinceOpen;
            ticksUntilClose = ParentDoor.TicksUntilClose;

            // We're delegating all the Tick logic to Building_DoorExpanded, which syncs its fields with its invis doors as needed.
            // So we skip calling Building_Door.Tick (via base.Tick()) and instead call Building.Tick (actually ThingWithComps.Tick).
            // Not replicating the logic in ThingWithComps.Tick, in case the logic changes or another mod patches that method.
            Building_Tick ??= (Action)Activator.CreateInstance(typeof(Action), this,
                methodof_Building_Tick.MethodHandle.GetFunctionPointer());
            Building_Tick();
        }

        private static readonly MethodInfo methodof_Building_Tick =
            AccessTools.Method(typeof(Building), nameof(Building.Tick));
        private Action Building_Tick;

        public override void PostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            // Note: The invis door def has useHitPoints=true, so invis doors never take damage.
            // A harmony patch allows PathFinder.IsDestroyable to still return true for invis doors.
            ParentDoor.TakeDamage(dinfo);
        }

        public override bool PawnCanOpen(Pawn p) => ParentDoor.PawnCanOpen(p);

        public override bool BlocksPawn(Pawn p) => ParentDoor.BlocksPawn(p);

        // Only for exposing public access to Building_Door.DoorOpen.
        public new void DoorOpen(int ticksToClose) => base.DoorOpen(ticksToClose);

        // Only for exposing public access to Building_Door.DoorTryClose.
        public new void DoorTryClose() => base.DoorTryClose();

        public override void Draw()
        {
            // Do nothing, not even call base.Draw(), since this is an invisible door.
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            // This invis door shouldn't be selectable to get gizmos, but just in case, return empty.
            return Enumerable.Empty<Gizmo>();
        }

        public override string ToString()
        {
            if (parentDoor == null)
                return base.ToString() + " (NO PARENT)";
            return base.ToString() + $" ({parentDoor}.invisDoors[{parentDoor.InvisDoors.IndexOf(this)}])";
        }
    }
}
