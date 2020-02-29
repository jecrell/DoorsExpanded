using RimWorld;
using Verse;

namespace DoorsExpanded
{
    [DefOf]
    public class HeronDefOf
    {
        public static ThingDef HeronInvisibleDoor;

        public static JobDef PH_UseRemoteButton;

        static HeronDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(HeronDefOf));
    }
}
