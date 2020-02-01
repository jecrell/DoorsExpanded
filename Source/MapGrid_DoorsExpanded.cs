using System;
using Verse;

namespace DoorsExpanded
{
    // Class is only being kept for backwards compatibility with existing saves,
    // since there currently isn't a good way to remove an obsolete MapComponent when loading a save file.
    [Obsolete]
    public class MapGrid_DoorsExpanded : MapComponent
    {
        public MapGrid_DoorsExpanded(Map map) : base(map) {}
    }
}
