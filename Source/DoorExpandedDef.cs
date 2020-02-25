using UnityEngine;
using Verse;

namespace DoorsExpanded
{
    public enum DoorType
    {
        Standard = 0,
        Stretch,
        DoubleSwing,
        FreePassage,
        StretchVertical,
    }

    public class DoorExpandedDef : ThingDef
    {
        public bool fixedPerspective = false;
        public bool singleDoor = false;
        public DoorType doorType = DoorType.Standard;
        public bool rotatesSouth = true;
        public int tempLeakRate = TemperatureTuning.Door_TempEqualizeIntervalClosed;
        public float doorOpenMultiplier = Building_DoorExpanded.VisualDoorOffsetEnd;
        public float doorOpenSpeedRate = 1.0f;
        public GraphicData doorFrame;
        public Vector3 doorFrameOffset;
        public GraphicData doorFrameSplit;
        public Vector3 doorFrameSplitOffset;
        public GraphicData doorAsync;

        public DoorExpandedDef()
        {
            thingClass = typeof(Building_DoorExpanded);
        }
    }
}
