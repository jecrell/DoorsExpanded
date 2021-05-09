using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DoorsExpanded
{
    public class StatWorker_DoorOpenTime : StatWorker
    {
        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true)
        {
            return Building_DoorExpanded.DoorOpenTime(req, DoorPowerOn(req), applyPostProcess);
        }

        public override string GetExplanationUnfinalized(StatRequest req, ToStringNumberSense numberSense)
        {
            return Building_DoorExpanded.DoorOpenTimeExplanation(req, DoorPowerOn(req), stat);
        }

        public override bool ShouldShowFor(StatRequest req)
        {
            if (req.Def is ThingDef thingDef && thingDef.IsDoor)
            {
                if (!Building_DoorExpanded.DoorNeedsPower(thingDef))
                    return stat == HeronDefOf.DoorOpenTime;
                if (!req.HasThing)
                    return stat != HeronDefOf.DoorOpenTime;
                if (stat == HeronDefOf.DoorOpenTime)
                    return true;
                if (Building_DoorExpanded.DoorIsPoweredOn(req.Thing) is bool doorPowerOn)
                    return doorPowerOn ? stat == HeronDefOf.UnpoweredDoorOpenTime : stat == HeronDefOf.PoweredDoorOpenTime;
            }
            return false;
        }

        private bool DoorPowerOn(StatRequest req)
        {
            if (stat == HeronDefOf.PoweredDoorOpenTime)
                return true;
            if (stat == HeronDefOf.UnpoweredDoorOpenTime)
                return false;
            return Building_DoorExpanded.DoorIsPoweredOn(req.Thing) ?? Building_DoorExpanded.DoorNeedsPower((ThingDef)req.Def);
        }

        public override IEnumerable<Dialog_InfoCard.Hyperlink> GetInfoCardHyperlinks(StatRequest statRequest)
        {
            if (statRequest.Thing?.Stuff is ThingDef thingStuff)
                yield return new Dialog_InfoCard.Hyperlink(thingStuff);
            else if (statRequest.StuffDef is ThingDef stuffDef)
                yield return new Dialog_InfoCard.Hyperlink(stuffDef);
        }
    }
}
