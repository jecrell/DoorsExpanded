using Harmony;
using RimWorld;
using Verse;
using Verse.Sound;

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

        public override string LabelMouseover => "";

        public override string Label => parentDoor.Label;

        public override string LabelShort => parentDoor.LabelShort;

        public int TicksUntilClose
        {
            get => ticksUntilClose;
            set => ticksUntilClose = value;
        }

        public override void Tick()
        {
            base.Tick();
            // TODO: Buildings never tick when destroyed or unspawned.
            if (!Spawned || Destroyed)
                return;

            // Periodic sanity checks.
            if (Find.TickManager.TicksGame % 2500 == 0)
            {
                if (ParentDoor == null)
                    Destroy();

                if (Faction != ParentDoor.Faction)
                    SetFaction(ParentDoor.Faction);
            }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            Map.edificeGrid.Register(this);
        }

        public override void PostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            base.PostApplyDamage(dinfo, totalDamageDealt);
            HitPoints = MaxHitPoints;
            ParentDoor.TakeDamage(dinfo);
        }

        public bool OpenValue
        {
            get => Traverse.Create(this).Field("openInt").GetValue<bool>();
            set => Traverse.Create(this).Field("openInt").SetValue(value);
        }

        public override bool BlocksPawn(Pawn p)
        {
            return base.BlocksPawn(p) || ParentDoor.BlocksPawn(p);
        }

        public override bool PawnCanOpen(Pawn p)
        {
            return base.PawnCanOpen(p) && ParentDoor.PawnCanOpenSpecialCases(p);
        }

        public void OpenMe(int ticks)
        {
            ticksUntilClose = ticks;
            if (!Open)
            {
                //Log.Message("Opened this door.");
                Traverse.Create(this).Field("openInt").SetValue(true);
                if (DoorPowerOn)
                {
                    def.building.soundDoorOpenPowered?.PlayOneShot(new TargetInfo(Position, Map));
                }
                else
                {
                    def.building.soundDoorOpenManual?.PlayOneShot(new TargetInfo(Position, Map));
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref parentDoor, nameof(parentDoor));
        }
    }
}
