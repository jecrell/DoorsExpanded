using RimWorld;
using Verse;

namespace DoorsExpanded
{
    [DefOf]
    public static class HeronDefOf
    {
        public static ThingDef HeronInvisibleDoor;

        public static JobDef PH_UseRemoteButton;

        public static StatDef DoorOpenTime;
        public static StatDef PoweredDoorOpenTime;
        public static StatDef UnpoweredDoorOpenTime;

        static HeronDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(HeronDefOf));
    }
}
