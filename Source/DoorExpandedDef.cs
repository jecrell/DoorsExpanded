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

    // TODO: Couldn't this be reworked into a ThingComp/CompProperties?
    public class DoorExpandedDef : ThingDef
    {
        public const float DefaultStretchPercent = 0.2f;

        public bool fixedPerspective = false;
        public bool singleDoor = false;
        public DoorType doorType = DoorType.Standard;
        public bool rotatesSouth = true;
        public int tempLeakRate = TemperatureTuning.Door_TempEqualizeIntervalClosed;
        public float doorOpenMultiplier = Building_DoorExpanded.VisualDoorOffsetEnd;
        // TODO: Shouldn't this be incorporated into the DoorOpenSpeed stat somehow?
        public float doorOpenSpeedRate = 1.0f;
        public GraphicData doorFrame;
        public Vector3 doorFrameOffset;
        public GraphicData doorFrameSplit;
        public Vector3 doorFrameSplitOffset;
        public GraphicData doorAsync;

        // Following properties are only relevant for Stretch/StretchVertical doors.
        // The size of a closed door, relative to stretchOpenSize.
        // This is typically the "actual" size ignoring transparent sections of the texture.
        // Like DrawPos, its origin point is assumed to be at the center of the rectangle.
        public Vector2 stretchCloseSize;
        // Size to stretch (typically shrink) to for an opened door, relative to stretchCloseSize.
        // Defaults to DefaultStretchPercent * x or y of stretchCloseSize, depending on Stretch or StretchVertical.
        public Vector2 stretchOpenSize;
        // Offset from stretchCloseSize's center to stretchOpenSize's center.
        // Supposing north-facing door and stretch size shrinks, default to offsetting left (-x) and up (+y),
        // such the left/up side looks like it hasn't moved.
        public Vector2? stretchOffset;

        public DoorExpandedDef()
        {
            // Following are already defined in the AbstractHeronDoorBase ThingDef,
            // but just in case anyone doesn't inherit from that def, so that they're at least minimally functional.
            thingClass = typeof(Building_DoorExpanded);
            category = ThingCategory.Building;
            tickerType = TickerType.Normal;
            drawerType = DrawerType.RealtimeOnly;
        }

        public override void ResolveReferences()
        {
            base.ResolveReferences();
            // See comments regarding stretch property defaults in the fields above.
            if (graphicData != null && (doorType == DoorType.Stretch || doorType == DoorType.StretchVertical))
            {
                if (stretchCloseSize == Vector2.zero)
                    stretchCloseSize = graphicData.drawSize;
                if (stretchOpenSize == Vector2.zero)
                {
                    if (doorType == DoorType.Stretch)
                        stretchOpenSize = new Vector2(stretchCloseSize.x * DefaultStretchPercent, stretchCloseSize.y);
                    else
                        stretchOpenSize = new Vector2(stretchCloseSize.x, stretchCloseSize.y * DefaultStretchPercent);
                }
                if (stretchOffset == null)
                {
                    stretchOffset = new Vector2(
                        (stretchOpenSize.x - stretchCloseSize.x) / 2,
                        (stretchCloseSize.y - stretchOpenSize.y) / 2);
                }
                //Log.Message($"Stretch door {defName} properties:\n" +
                //    $"- stretchCloseSize: {stretchCloseSize}\n" +
                //    $"- stretchOpenSize: {stretchOpenSize}\n" +
                //    $"- stretchOffset: {stretchOffset}");
            }
        }
    }
}
