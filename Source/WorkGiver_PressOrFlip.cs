using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace DoorsExpanded
{
    public class WorkGiver_PressOrFlip : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);

        public static IEnumerable<Thing> ButtonsOrLevers(Pawn pawn)
        {
            var buttonsOrLevers =
                from Building_DoorRemoteButton t
                in pawn.Map.listerBuildings.AllBuildingsColonistOfClass<Building_DoorRemoteButton>()
                where t.NeedsToBeSwitched == true
                select (Thing)t;
            return new HashSet<Thing>(buttonsOrLevers);
        }

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            return ButtonsOrLevers(pawn);
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            return ButtonsOrLevers(pawn).Count() == 0;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_DoorRemoteButton building))
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
            if (!pawn.CanReserve(t))
                return false;
            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return new Job(DefDatabase<JobDef>.GetNamed("PH_FlipOrPress"), t);
        }
    }
}
