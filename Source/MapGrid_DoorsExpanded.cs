using System.Collections.Generic;
using System.Linq;
using Verse;

namespace DoorsExpanded
{
    public class MapGrid_DoorsExpanded : MapComponent
    {
        // Keep track of doors on the map
        private readonly Dictionary<IntVec3, bool> _doors = new Dictionary<IntVec3, bool>();

        public MapGrid_DoorsExpanded(Map map) : base(map) {}

        /// <summary>
        /// Removes door reference
        /// </summary>
        /// <param name="loc">Door location</param>
        public void Notify_UpdateDoorReference(IntVec3 loc)
        {
            if (!_doors.ContainsKey(loc))
                return;
            _doors.Remove(loc);
        }

        /// <summary>
        /// Uses a hashtable to keep track of doors at locations.
        /// </summary>
        /// <param name="loc"></param>
        /// <returns></returns>
        public bool HasDoorAtLocation(IntVec3 loc)
        {
            if (!_doors.ContainsKey(loc))
            {
                _doors.Add(loc, DoorLocationCheck(loc));
            }
            return _doors[loc];
        }

        /// <summary>
        /// If a Door or DoorExpanded door type is present at the location,
        /// return true or false.
        /// </summary>
        /// <param name="loc">IntVec3 of check location</param>
        /// <returns>Door exists?</returns>
        private bool DoorLocationCheck(IntVec3 loc)
        {
            if (loc == null || !loc.IsValid)
                return false;

            var door =
                (from thing in loc.GetThingList(map)
                where thing is Building_DoorExpanded
                || thing is Building_DoorRegionHandler
                select thing).FirstOrDefault();

            return door != null;
        }
    }
}
