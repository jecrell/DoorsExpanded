using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;
using System.Linq;

namespace DoorsExpanded
{
    public class WorkGiver_PressOrFlip : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest
        {
            get
            {
                return ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);
            }
        }

        public IEnumerable<Thing> ButtonsOrLevers(Pawn pawn)
        {
            var stuff = from Building_DoorRemoteButton t in pawn.Map.listerBuildings.AllBuildingsColonistOfClass<Building_DoorRemoteButton>()
                        where t.NeedsToBeSwitched == true
                        select t;

            HashSet<Thing> things = new HashSet<Thing>();
            foreach (var thing in stuff)
            {
                things.Add(thing);
            }

            return things;

        }

        public override PathEndMode PathEndMode
        {
            get
            {
                return PathEndMode.Touch;
            }
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            return ButtonsOrLevers(pawn);
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            return ButtonsOrLevers(pawn).Count<Thing>() == 0;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Building_DoorRemoteButton building = t as Building_DoorRemoteButton;
            if (building == null)
            {
                return false;
            }
            if (!building.NeedsToBeSwitched)
            {
                return false;
            }
            if (t.Faction != pawn.Faction)
            {
                return false;
            }
            if (pawn.Faction == Faction.OfPlayer && !pawn.Map.areaManager.Home[t.Position])
            {
                JobFailReason.Is(WorkGiver_FixBrokenDownBuilding.NotInHomeAreaTrans);
                return false;
            }
            if (!pawn.CanReserve(t)) return false;// pawn.Map.reservationManager.IsReserved(t, pawn.Faction)) return false;
            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return new Job(DefDatabase<JobDef>.GetNamed("PH_FlipOrPress"), t);
        }
    }
}
