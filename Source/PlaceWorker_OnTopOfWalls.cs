// Note: This is copied from JecsTools.

using System.Linq;
using Verse;

namespace JecsTools
{
    public class PlaceWorker_OnTopOfWalls : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map,
            Thing thingToIgnore = null, Thing thing = null)
        {
            if (loc.GetThingList(map).FirstOrDefault(x =>
                    x.def.defName.Contains("Wall")) != null)
                return true;
            // Note: Using different translation key ("JT" => "PH") to avoid "duplicate keyed translation key" warnings.
            return new AcceptanceReport("PH_PlaceWorker_OnTopOfWalls".Translate());
        }
    }
}
