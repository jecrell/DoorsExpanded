using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace ProjectHeron
{
    public class DoorExpandedDef : ThingDef
    {
        public bool fixedPerspective = false;
        public bool doorSwing = false;
        public GraphicData doorFrame;
        public GraphicData doorAsync;
    }
}
