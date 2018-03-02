using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
            get
            {
                return parentDoor;
            }
            set
            {
                parentDoor = value;
            }
        }

        public override string LabelMouseover => "";

        public override string Label => parentDoor.Label;

        public override string LabelShort => parentDoor.LabelShort;

        public int TicksUntilClose
        {
            get => this.ticksUntilClose;
            set => this.ticksUntilClose = value;
        }


        public override void Tick()
        {
            base.Tick();
            if (!Spawned || Destroyed) return;
            if (Find.TickManager.TicksGame % 2500 == 0)
            {
                if (this.Faction != ParentDoor.Faction)
                    this.SetFaction(ParentDoor.Faction);
            }
        }

        public override void PostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            base.PostApplyDamage(dinfo, totalDamageDealt);
            this.HitPoints = this.MaxHitPoints;
            this.ParentDoor.TakeDamage(dinfo);
        }

        public bool OpenValue
        {
            get => Traverse.Create(this).Field("openInt").GetValue<bool>();
            set => Traverse.Create(this).Field("openInt").SetValue(value);
        }
        
        public void OpenMe(int ticks)
        {
            this.ticksUntilClose = ticks;
            if (!Open)
            {
                Log.Message("Opened this door.");
                Traverse.Create(this).Field("openInt").SetValue(true);
                if (this.DoorPowerOn)
                {
                    var buildingSoundDoorOpenPowered = this.def.building.soundDoorOpenPowered;
                    if (buildingSoundDoorOpenPowered != null)
                        buildingSoundDoorOpenPowered.PlayOneShot(new TargetInfo(base.Position, base.Map,
                            false));
                }
                else
                {
                    var buildingSoundDoorOpenManual = this.def.building.soundDoorOpenManual;
                    if (buildingSoundDoorOpenManual != null)
                        buildingSoundDoorOpenManual.PlayOneShot(
                            new TargetInfo(base.Position, base.Map, false));
                }
            }
        }
        
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look<Building_DoorExpanded>(ref this.parentDoor, "parentDoor");
/*            if (Scribe.mode == LoadSaveMode.Saving)
            {
                this.Destroy(DestroyMode.Vanish);
            }*/
        }
    }
}
