using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using RimWorld;
using UnityEngine;

namespace DoorsExpanded
{

    public enum DoorType
    {
        Standard = 0,
        Stretch,
        DoubleSwing,
        FreePassage
    }
    public class DoorExpandedDef : ThingDef
    {
        public bool fixedPerspective = false;
        public bool singleDoor = false;
        public DoorType doorType = DoorType.Standard;
        public bool rotatesSouth = true;
        public int tempLeakRate = 375;
        public float doorOpenMultiplier = 0.45f;
        public float doorOpenSpeedRate = 1.0f;
        public GraphicData doorFrame;
        public Vector3 doorFrameOffset = new Vector3(0,0,0);
        public GraphicData doorFrameSplit;
        public Vector3 doorFrameSplitOffset = new Vector3(0,0,0);
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
