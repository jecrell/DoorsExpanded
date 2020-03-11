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

        public override bool FireBulwark => ParentDoor.FireBulwark;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
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
            // This check is necessary to prevent errors during operations that despawn all things in the same cell,
            // since despawning/destroying parent doors also destroys their invis doors.
            if (Spawned)
                base.DeSpawn(mode);
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            // This check is necessary to prevent errors during operations that delete all things in the same cell,
            // since despawning/destroying parent doors also destroys their invis doors.
            if (!Destroyed)
                base.Destroy(mode);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref parentDoor, nameof(parentDoor));
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
                Destroy(DestroyMode.Vanish);
                return;
            }
            if (Faction != ParentDoor.Faction)
                SetFaction(ParentDoor.Faction);

            // We're delegating all the Tick logic to Building_DoorExpanded, which syncs its fields with its invis doors as needed.
            // So we skip calling Building_Door.Tick (via base.Tick()) and instead call Building.Tick (actually ThingWithComps.Tick).
            // Not replicating the logic in ThingWithComps.Tick, in case the logic changes or another mod patches that method.
            if (Building_Tick == null)
                Building_Tick = (Action)Activator.CreateInstance(typeof(Action), this,
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
            return base.ToString() + " of " + parentDoor;
        }
    }
}
