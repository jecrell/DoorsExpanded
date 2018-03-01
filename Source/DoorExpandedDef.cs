using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using RimWorld;

namespace DoorsExpanded
{

    public enum DoorType
    {
        Standard = 0,
        Stretch,
        DoubleSwing
    }
    public class DoorExpandedDef : ThingDef
    {
        public bool fixedPerspective = false;
        public bool singleDoor = false;
        public DoorType doorType = DoorType.Standard;
        public bool rotatesSouth = true;
        public int tempLeakRate = 375;
        public float doorOpenSpeedRate = 1.0f;
        public GraphicData doorFrame;
        public GraphicData doorFrameSplit;
        public GraphicData doorAsync;

        public CompPower powerComp;
        public Map map;
        public void test()
        {

        }

        public DoorExpandedDef()
        {
            this.thingClass = typeof(Building_DoorExpanded);
        }
    }

}
