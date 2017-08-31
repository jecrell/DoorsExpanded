using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace ProjectHeron
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

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look<Building_DoorExpanded>(ref this.parentDoor, "parentDoor");
        }
    }
}
