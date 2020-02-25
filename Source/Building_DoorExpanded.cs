using System;
using System.Collections.Generic;
using System.Linq;
using Harmony;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace DoorsExpanded
{
    /// <summary>
    /// 
    /// Building_DoorExpanded
    /// 
    /// What: A class for multi-celled, larger and more complicated doors.
    /// 
    /// HowWhy: This class is a copy of the Building_Door class without inheriting it.
    /// This prevents it from being passed into region checks that cause region
    /// link errors. It also spawns in Building_DoorRegionHandler classed Things
    /// to act as invisible doors between the spaces of the larger door. This
    /// prevents portal errors.
    /// 
    /// </summary>
    public class Building_DoorExpanded : Building
    {
        private const float OpenTicks = 45f;
        private const int CloseDelayTicks = 60;
        private const int WillCloseSoonThreshold = 60;
        private const int ApproachCloseDelayTicks = 120;
        private const int MaxTicksSinceFriendlyTouchToAutoClose = 97;
        private const float PowerOffDoorOpenSpeedFactor = 0.25f;
        private const float VisualDoorOffsetStart = 0f;
        internal const float VisualDoorOffsetEnd = 0.45f;

        private List<Building_DoorRegionHandler> invisDoors;
        private List<Pawn> crossingPawns;
        private CompPowerTrader powerComp;
        private CompForbiddable forbiddenComp;
        private bool openInt;
        protected bool holdOpenInt;
        private int lastFriendlyTouchTick = -9999;
        protected int ticksUntilClose;
        protected int visualTicksOpen;
        private bool freePassageWhenClearedReachabilityCache;
        private float friendlyTouchTicksFactor = 1f;
        private bool lastForbidSetting;

        public DoorExpandedDef Def => def as DoorExpandedDef;

        public List<Building_DoorRegionHandler> InvisDoors =>
            invisDoors ?? (invisDoors = new List<Building_DoorRegionHandler>());

        public bool Open => Def.doorType == DoorType.FreePassage || openInt;

        public bool FreePassage => Def.doorType == DoorType.FreePassage || openInt && (holdOpenInt || !WillCloseSoon);

        public virtual bool WillCloseSoon
        {
            get
            {
                if (!Spawned)
                {
                    return true;
                }

                if (!openInt)
                {
                    return true;
                }

                if (ticksUntilClose > 0 && ticksUntilClose <= WillCloseSoonThreshold && CanCloseAutomatically)
                {
                    return true;
                }

                if (holdOpenInt)
                {
                    return false;
                }

                return true;
            }
        }

        public bool BlockedOpenMomentary => InvisDoors != null && InvisDoors?.Count > 0 && InvisDoors.Any(invisDoor =>
        {
            var result = false;
            try
            {
                result = invisDoor?.BlockedOpenMomentary ?? false;
            }
            catch (Exception)
            {
            }
            return result;
        });

        public bool DoorPowerOn => powerComp != null && powerComp.PowerOn;

        public bool SlowsPawns => Def.doorType != DoorType.FreePassage && (/*!DoorPowerOn ||*/ TicksToOpenNow > 20);

        public int TicksToOpenNow
        {
            get
            {
                var ticksToOpenNow = OpenTicks / this.GetStatValue(StatDefOf.DoorOpenSpeed, true);
                if (DoorPowerOn)
                {
                    ticksToOpenNow *= PowerOffDoorOpenSpeedFactor;
                }

                ticksToOpenNow *= Def.doorOpenSpeedRate;
                if (Def.doorType == DoorType.FreePassage)
                {
                    ticksToOpenNow *= 0.01f;
                }

                return Mathf.RoundToInt(ticksToOpenNow);
            }
        }

        private bool CanCloseAutomatically => DoorPowerOn && FriendlyTouchedRecently;

        private bool FriendlyTouchedRecently =>
            Find.TickManager.TicksGame < lastFriendlyTouchTick +
                (int)(MaxTicksSinceFriendlyTouchToAutoClose * friendlyTouchTicksFactor);

        private int VisualTicksToOpen => TicksToOpenNow;

        public override void PostMake()
        {
            base.PostMake();
            powerComp = GetComp<CompPowerTrader>();
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            crossingPawns = new List<Pawn>();

            base.SpawnSetup(map, respawningAfterLoad);
            powerComp = GetComp<CompPowerTrader>();
            forbiddenComp = GetComp<CompForbiddable>();
            lastForbidSetting = forbiddenComp.Forbidden;
            if (invisDoors?.Count > 0)
            {
                foreach (var invisDoor in invisDoors)
                    invisDoor.SetForbidden(forbiddenComp.Forbidden);
            }
            Map.edificeGrid.Register(this);
            Map.reachability.ClearCache();
            foreach (var c in this.OccupiedRect().Cells)
            {
                Map.GetComponent<MapGrid_DoorsExpanded>().Notify_UpdateDoorReference(c);
            }
        }

        private void SpawnDoors()
        {
            InvisDoors.Clear();
            foreach (var c in this.OccupiedRect().Cells)
            {
                if (c.GetThingList(MapHeld).FirstOrDefault(thing => thing.def == HeronDefOf.HeronInvisibleDoor) is
                    Building_DoorRegionHandler invisDoor)
                {
                    // Spawn over another door? Let's erase that door and add our own invis doors.
                    if (invisDoor.ParentDoor != this && (invisDoor.ParentDoor?.Spawned ?? false))
                    {
                        invisDoor.ParentDoor.DeSpawn();
                        invisDoor = (Building_DoorRegionHandler)ThingMaker.MakeThing(HeronDefOf.HeronInvisibleDoor);
                        invisDoor.ParentDoor = this;
                        GenSpawn.Spawn(invisDoor, c, MapHeld);
                        invisDoor.SetFaction(Faction);
                        AccessTools.Field(typeof(Building_DoorRegionHandler), "holdOpenInt").SetValue(invisDoor, holdOpenInt);
                        InvisDoors.Add(invisDoor);
                        continue;
                    }

                    invisDoor.ParentDoor = this;
                    invisDoor.SetFaction(Faction);
                    AccessTools.Field(typeof(Building_DoorRegionHandler), "holdOpenInt")
                        .SetValue(invisDoor, holdOpenInt);
                    InvisDoors.Add(invisDoor);
                }
                else
                {
                    //Log.Message("Door not found");
                    var thing = (Building_DoorRegionHandler)ThingMaker.MakeThing(HeronDefOf.HeronInvisibleDoor);
                    thing.ParentDoor = this;
                    GenSpawn.Spawn(thing, c, MapHeld);
                    thing.SetFaction(Faction);
                    AccessTools.Field(typeof(Building_DoorRegionHandler), "holdOpenInt").SetValue(thing, holdOpenInt);
                    InvisDoors.Add(thing);
                }
            }

            ClearReachabilityCache(MapHeld);
            if (BlockedOpenMomentary)
                DoorOpen();
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            if (invisDoors?.Count > 0)
            {
                var tempDoors = new List<Building_DoorRegionHandler>(invisDoors);
                foreach (var invisDoor in invisDoors)
                {
                    if (invisDoor != null && invisDoor.Spawned)
                        tempDoors?.FirstOrDefault(otherInvisDoor => otherInvisDoor == invisDoor)?.DeSpawn();
                }
                tempDoors = null;
                invisDoors = null;
            }

            foreach (var c in this.OccupiedRect().Cells)
            {
                Map.GetComponent<MapGrid_DoorsExpanded>().Notify_UpdateDoorReference(c);
            }
            base.DeSpawn(mode);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref invisDoors, nameof(invisDoors), LookMode.Reference);
            Scribe_Collections.Look(ref crossingPawns, nameof(crossingPawns), LookMode.Reference);
            Scribe_Values.Look(ref openInt, "open", false);
            Scribe_Values.Look(ref holdOpenInt, "holdOpen", false);
            Scribe_Values.Look(ref lastFriendlyTouchTick, nameof(lastFriendlyTouchTick), 0);
        }

        public override void Tick()
        {
            base.Tick();
            // TODO: Buildings never tick when destroyed or unspawned. And this is never null.
            if (!Spawned || this.DestroyedOrNull())
                return;
            if (invisDoors.NullOrEmpty())
                SpawnDoors();

            if (forbiddenComp.Forbidden != lastForbidSetting)
            {
                lastForbidSetting = forbiddenComp.Forbidden;
                foreach (var invisDoor in invisDoors)
                    invisDoor.SetForbidden(forbiddenComp.Forbidden);
                Map.reachability.ClearCache();
            }

            var closedTempLeakRate = Def?.tempLeakRate ?? TemperatureTuning.Door_TempEqualizeIntervalClosed;
            if (Find.TickManager.TicksGame % MaxTicksSinceFriendlyTouchToAutoClose == 0)
            {
                if (ShouldKeepDoorOpen())
                {
                    foreach (var pawn in new List<Pawn>(crossingPawns))
                    {
                        var curDist = pawn.PositionHeld.LengthHorizontalSquared;
                        //Log.Message(curDist.ToString());
                        if (curDist > Mathf.Max(def.Size.x, def.Size.z) + 1)
                        {
                            //Log.Message("Removed " + pawn.LabelShort);
                            crossingPawns.Remove(pawn);
                        }
                    }
                }
                else
                    DoorTryClose();
            }

            if (FreePassage != freePassageWhenClearedReachabilityCache)
                ClearReachabilityCache(Map);

            if (!openInt)
            {
                if (visualTicksOpen > 0)
                    visualTicksOpen--;
                if ((Find.TickManager.TicksGame + thingIDNumber.HashOffset()) % closedTempLeakRate == 0)
                    GenTemperature.EqualizeTemperaturesThroughBuilding(this, 1f, twoWay: false);
            }
            else if (openInt)
            {
                if (visualTicksOpen < VisualTicksToOpen)
                    visualTicksOpen++;
                if (!holdOpenInt)
                {
                    var isPawnPresent = false;
                    foreach (var invisDoor in invisDoors)
                    {
                        if (!Map.thingGrid.CellContains(invisDoor.PositionHeld, ThingCategory.Pawn))
                            continue;
                        invisDoor.OpenValue = true;
                        invisDoor.TicksUntilClose = CloseDelayTicks;
                        isPawnPresent = true;
                    }

                    if (!isPawnPresent)
                    {
                        ticksUntilClose--;
                        if (ticksUntilClose <= 0 && CanCloseAutomatically)
                        {
                            DoorTryClose();
                            foreach (var invisDoor in invisDoors)
                            {
                                AccessTools.Method(typeof(Building_Door), "DoorTryClose").Invoke(invisDoor, null);
                            }
                        }
                    }
                    else
                    {
                        ticksUntilClose = CloseDelayTicks;
                    }
                }

                if ((Find.TickManager.TicksGame + thingIDNumber.HashOffset()) % TemperatureTuning.Door_TempEqualizeIntervalOpen == 0)
                {
                    GenTemperature.EqualizeTemperaturesThroughBuilding(this, 1f, twoWay: false);
                }
            }
        }

        public void FriendlyTouched(Pawn p)
        {
            if (crossingPawns.NullOrEmpty())
            {
                crossingPawns = new List<Pawn>();
            }

            if (!crossingPawns.Contains(p))
                crossingPawns.Add(p);
            friendlyTouchTicksFactor = 1.0f;
            if (p.CurJob != null)
            {
                switch (p.CurJob.locomotionUrgency)
                {
                    case LocomotionUrgency.None:
                    case LocomotionUrgency.Amble:
                        friendlyTouchTicksFactor += 1.5f;
                        break;
                    case LocomotionUrgency.Walk:
                        friendlyTouchTicksFactor += 0.75f;
                        break;
                    case LocomotionUrgency.Jog:
                    case LocomotionUrgency.Sprint:
                        break;
                }
            }

            if (p.health.capacities.GetLevel(PawnCapacityDefOf.Moving) is float val && val < 1f)
            {
                //Log.Message("Moving capacity is: " + val);
                friendlyTouchTicksFactor += 1f - val;
            }

            lastFriendlyTouchTick = Find.TickManager.TicksGame;
        }

        public virtual void Notify_PawnApproaching(Pawn p)
        {
            if (crossingPawns.Contains(p))
                return;

            if (p.InAggroMentalState && p.AnimalOrWildMan())
                return;

            if (!p.HostileTo(this) && !this.IsForbidden(p))
            {
                FriendlyTouched(p);
                return;
            }

            if (PawnCanOpen(p))
            {
                //Map.fogGrid.Notify_PawnEnteringDoor(this, p);
                if (!crossingPawns.Contains(p))
                    crossingPawns.Add(p);

                //Map.fogGrid.Notify_PawnEnteringDoor(this, p);
                if (!SlowsPawns)
                {
                    DoorOpen(ApproachCloseDelayTicks);
                }
            }
        }

        public virtual bool PawnCanOpen(Pawn p)
        {
            if (invisDoors?.Count > 0)
            {
                if (invisDoors.Any(x => x.PawnCanOpen(p)))
                    return true;
            }
            return false;
        }

        public virtual bool PawnCanOpenSpecialCases(Pawn p) => true;

        public override bool BlocksPawn(Pawn p) => Def.doorType != DoorType.FreePassage && !openInt && !PawnCanOpen(p);

        protected virtual bool ShouldKeepDoorOpen()
        {
            return !openInt || holdOpenInt || BlockedOpenMomentary || FriendlyTouchedRecently || crossingPawns?.Count > 0;
        }

        protected internal void DoorOpen(int ticksToClose = CloseDelayTicks)
        {
            ticksUntilClose = ticksToClose;
            if (!openInt)
            {
                openInt = true;
                if (DoorPowerOn)
                {
                    def?.building?.soundDoorOpenPowered?.PlayOneShot(new TargetInfo(Position, Map));
                }
                else
                {
                    def?.building?.soundDoorOpenManual?.PlayOneShot(new TargetInfo(Position, Map));
                }

                foreach (var invisDoor in invisDoors)
                {
                    Traverse.Create(invisDoor).Field("lastFriendlyTouchTick").SetValue(Find.TickManager.TicksGame);
                    //invisDoor.CheckFriendlyTouched(); //FriendlyTouched();
                    invisDoor.OpenMe(ticksToClose * Mathf.Max(Def.Size.x, Def.Size.z) * 2);
                    //AccessTools.Method(typeof(Building_Door), "DoorOpen").Invoke(invisDoor, new object[] { ticksToClose * Mathf.Max(Def.Size.x, Def.Size.z) * 2});
                }
            }
        }

        protected void DoorTryClose()
        {
            if (ShouldKeepDoorOpen())
            {
                return;
            }

            //Log.Message("Stop that!");
            foreach (var invisDoor in InvisDoors)
            {
                if (invisDoor.Open)
                    AccessTools.Field(typeof(Building_Door), "openInt").SetValue(invisDoor, false);
                //AccessTools.Method(typeof(Building_Door), "DoorTryClose").Invoke(handler, null);
            }

            openInt = false;
            if (DoorPowerOn)
            {
                def?.building?.soundDoorClosePowered?.PlayOneShot(new TargetInfo(Position, Map));
            }
            else
            {
                def?.building?.soundDoorCloseManual?.PlayOneShot(new TargetInfo(Position, Map));
            }
        }

        public void StartManualOpenBy(Pawn opener)
        {
            //if (PawnCanOpen(opener))
            DoorOpen();
        }

        public void StartManualCloseBy(Pawn closer)
        {
            DoorTryClose();
        }

        public override void Draw()
        {
            var percentOpen = Mathf.Clamp01((float)visualTicksOpen / VisualTicksToOpen);
            var mod = (VisualDoorOffsetStart + Def.doorOpenMultiplier * percentOpen) * def.Size.x;
            var rotation = Rotation;
            if (!Def.rotatesSouth && Rotation == Rot4.South)
                rotation = Rot4.North;
            for (var i = 0; i < 2; i++)
            {
                var flipped = i != 0;
                Mesh mesh;
                Matrix4x4 matrix;

                switch (Def.doorType)
                {
                    case DoorType.StretchVertical:
                        DrawStretchParams(Def, DrawPos, rotation, out mesh, out matrix, mod, flipped, true);
                        break;
                    case DoorType.Stretch:
                        DrawStretchParams(Def, DrawPos, rotation, out mesh, out matrix, mod, flipped);
                        break;
                    case DoorType.DoubleSwing:
                        DrawDoubleSwingParams(Def, DrawPos, rotation, out mesh, out matrix, mod, flipped);
                        break;
                    default:
                        DrawParams(Def, DrawPos, rotation, out mesh, out matrix, mod, flipped);
                        break;
                }

                var matToDraw = (!flipped && Def.doorAsync is GraphicData doorAsyncGraphic)
                    ? doorAsyncGraphic.GraphicColoredFor(this).MatAt(rotation)
                    : Graphic.MatAt(rotation);
                Graphics.DrawMesh(mesh, matrix, matToDraw, 0);
                if (Def.singleDoor)
                    break;
            }

            if (Def.doorFrame is GraphicData)
            {
                DrawFrameParams(Def, DrawPos, rotation, false, out var fMesh, out var fMatrix);

                //var currRot = (Def.fixedPerspective && Rotation == Rot4.West) ? Rot4.East : base.Rotation;
                Graphics.DrawMesh(fMesh, fMatrix, Def.doorFrame.GraphicColoredFor(this).MatAt(rotation), 0);
                if (Def.doorFrameSplit is GraphicData)
                {
                    DrawFrameParams(Def, DrawPos, rotation, true, out fMesh, out fMatrix);
                    Graphics.DrawMesh(fMesh, fMatrix, Def.doorFrameSplit.GraphicColoredFor(this).MatAt(rotation), 0);
                }
            }

            Comps_PostDraw();
        }

        /// <param name="verticalStretch">This allows for vertical stretching doors, such as garage doors.</param>
        private void DrawStretchParams(DoorExpandedDef thingDef, Vector3 drawPos, Rot4 rotation, out Mesh mesh,
            out Matrix4x4 matrix, float mod = 1, bool flipped = false, bool verticalStretch = false)
        {
            Rotation = Building_Door.DoorRotationAt(Position, Map);
            var verticalRotation = Rotation.IsHorizontal;
            Vector3 offsetVector;
            if (!flipped)
            {
                offsetVector = new Vector3(-1.5f, 0f, 0f);
                mesh = MeshPool.plane10;
            }
            else
            {
                offsetVector = new Vector3(1.5f, 0f, 0f);
                mesh = MeshPool.plane10Flip;
            }

            rotation.Rotate(RotationDirection.Clockwise);
            offsetVector = rotation.AsQuat * offsetVector;

            var graphicVector = drawPos;
            graphicVector.y = Altitudes.AltitudeFor(AltitudeLayer.DoorMoveable);
            graphicVector += offsetVector * (mod * 1.15f);

            var drawSize = thingDef.graphicData.drawSize;
            var persMod = thingDef.fixedPerspective ? 2f : 1f;
            Vector3 scaleVector;
            if (verticalStretch)
            {
                scaleVector = verticalRotation
                    ? new Vector3(drawSize.x * persMod, 1f, drawSize.y * persMod - mod * 1.3f)
                    : new Vector3(drawSize.x, 1f, drawSize.y - mod * 1.3f);
            }
            else
            {
                scaleVector = verticalRotation
                    ? new Vector3(drawSize.x * persMod - mod * 1.3f, 1f, drawSize.y * persMod)
                    : new Vector3(drawSize.x - mod * 1.3f, 1f, drawSize.y);
            }

            matrix = default;
            matrix.SetTRS(graphicVector, Rotation.AsQuat, scaleVector);

        }

        private static void DrawDoubleSwingParams(DoorExpandedDef thingDef, Vector3 drawPos, Rot4 rotation,
            out Mesh mesh, out Matrix4x4 matrix, float mod = 1, bool flipped = false)
        {
            var verticalRotation = rotation.IsHorizontal;
            rotation = (rotation == Rot4.South) ? Rot4.North : rotation;
            Vector3 offsetVector;
            if (!flipped)
            {
                offsetVector = new Vector3(-1f, 0f, 0f);
                if (verticalRotation)
                    offsetVector = new Vector3(1.4f, 0f, 1.1f);
                mesh = MeshPool.plane10;
            }
            else
            {
                offsetVector = new Vector3(1f, 0f, 0f);
                if (verticalRotation)
                    offsetVector = new Vector3(-1.4f, 0f, 1.1f);
                mesh = MeshPool.plane10Flip;
            }

            var rotQuat = rotation.AsQuat;
            if (verticalRotation)
            {
                rotQuat = (!flipped)
                    ? Quaternion.AngleAxis(rotation.AsAngle + (mod * -100f), Vector3.up)
                    : Quaternion.AngleAxis(rotation.AsAngle + (mod * 100f), Vector3.up);
            }

            offsetVector = rotQuat * offsetVector;

            var graphicVector = drawPos;
            graphicVector.y = Altitudes.AltitudeFor(AltitudeLayer.DoorMoveable);
            if (verticalRotation)
            {
                if (!flipped && rotation == Rot4.East
                    || flipped && rotation == Rot4.West)
                    graphicVector.y = Altitudes.AltitudeFor(AltitudeLayer.BuildingOnTop);
            }

            //if (!verticalRotation)
            //if (verticalRotation)
            //    mod *= 2f;
            graphicVector += offsetVector * mod;

            var drawSize = thingDef.graphicData.drawSize;
            var persMod = thingDef.fixedPerspective ? 2f : 1f;
            var scaleVector = verticalRotation
                ? new Vector3(drawSize.x * persMod, 1f, drawSize.y * persMod)
                : new Vector3(drawSize.x, 1f, drawSize.y);

            matrix = default;
            matrix.SetTRS(graphicVector, rotQuat, scaleVector);
        }

        internal static void DrawParams(DoorExpandedDef thingDef, Vector3 drawPos, Rot4 rotation, out Mesh mesh,
            out Matrix4x4 matrix, float mod = 1, bool flipped = false)
        {
            var verticalRotation = rotation.IsHorizontal;
            Vector3 offsetVector;
            if (!flipped)
            {
                offsetVector = new Vector3(-1f, 0f, 0f);
                mesh = MeshPool.plane10;
            }
            else
            {
                offsetVector = new Vector3(1f, 0f, 0f);
                mesh = MeshPool.plane10Flip;
            }

            var rotQuat = rotation.AsQuat;
            offsetVector = rotQuat * offsetVector;

            var graphicVector = drawPos;
            graphicVector.y = Altitudes.AltitudeFor(AltitudeLayer.DoorMoveable);
            graphicVector += offsetVector * mod;

            var drawSize = thingDef.graphicData.drawSize;
            var persMod = thingDef.fixedPerspective ? 2f : 1f;
            var scaleVector = verticalRotation
                ? new Vector3(drawSize.x * persMod, 1f, drawSize.y * persMod)
                : new Vector3(drawSize.x, 1f, drawSize.y);

            matrix = default;
            matrix.SetTRS(graphicVector, rotQuat, scaleVector);
        }

        private static void DrawFrameParams(DoorExpandedDef thingDef, Vector3 drawPos, Rot4 rotation, bool split, out Mesh mesh,
            out Matrix4x4 matrix)
        {
            var mod = (VisualDoorOffsetStart + VisualDoorOffsetEnd * 1) * thingDef.Size.x;
            var verticalRotation = rotation.IsHorizontal;
            var offsetVector = new Vector3(-1f, 0f, 0f);
            mesh = MeshPool.plane10;

            if (thingDef.doorFrameSplit != null)
            {
                if (rotation == Rot4.West)
                {
                    offsetVector.x = 1f;
                    //offsetVector.z *= -1f;
                    //offsetVector.y *= -1f;
                }
            }

            var rotQuat = rotation.AsQuat;
            offsetVector = rotQuat * offsetVector;

            var graphicVector = drawPos;
            graphicVector.y = Altitudes.AltitudeFor(AltitudeLayer.Blueprint);
            if (rotation == Rot4.North || rotation == Rot4.South)
                graphicVector.y = Altitudes.AltitudeFor(AltitudeLayer.PawnState);
            if (!verticalRotation)
                graphicVector.x += mod;
            if (rotation == Rot4.East)
            {
                graphicVector.z -= mod;
                if (split)
                    graphicVector.y = Altitudes.AltitudeFor(AltitudeLayer.DoorMoveable);
            }

            if (rotation == Rot4.West)
            {
                graphicVector.z += mod;
                if (split)
                    graphicVector.y = Altitudes.AltitudeFor(AltitudeLayer.DoorMoveable);
            }

            graphicVector += offsetVector * mod;

            var drawSize = thingDef.doorFrame.drawSize;
            var persMod = thingDef.fixedPerspective ? 2f : 1f;
            var scaleVector = verticalRotation
                ? new Vector3(drawSize.x * persMod, 1f, drawSize.y * persMod)
                : new Vector3(drawSize.x, 1f, drawSize.y);

            var offset = thingDef.doorFrameOffset;
            if (thingDef.doorFrameSplit != null)
            {
                if (rotation == Rot4.West)
                {
                    rotQuat = Quaternion.Euler(0, 270, 0); //new Quaternion(0, 0.7f, 0, -0.7f);
                    graphicVector.z -= 2.7f;
                    mesh = MeshPool.plane10Flip;
                    offset = thingDef.doorFrameSplitOffset;
                }
            }

            graphicVector += offset;

            matrix = default;
            matrix.SetTRS(graphicVector, rotQuat, scaleVector);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            if (Faction == Faction.OfPlayer)
            {
                yield return new Command_Toggle
                {
                    defaultLabel = "CommandToggleDoorHoldOpen".Translate(),
                    defaultDesc = "CommandToggleDoorHoldOpenDesc".Translate(),
                    hotKey = KeyBindingDefOf.Misc3,
                    icon = TexCommand.HoldOpen,
                    isActive = () => holdOpenInt,
                    toggleAction = () => holdOpenInt = !holdOpenInt,
                };
                if (DebugSettings.godMode)
                {
                    yield return new Command_Toggle
                    {
                        defaultLabel = "DEV: openInt",
                        defaultDesc = "debug".Translate(),
                        hotKey = KeyBindingDefOf.Misc3,
                        icon = TexCommand.HoldOpen,
                        isActive = () => openInt,
                        toggleAction = () => openInt = !openInt,
                    };
                }
            }
        }

        private void ClearReachabilityCache(Map map)
        {
            map.reachability.ClearCache();
            freePassageWhenClearedReachabilityCache = FreePassage;
        }
    }
}
