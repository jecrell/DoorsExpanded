using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
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
    // TODO: Since we're spending so much effort copying and patching things such that a door that's not a
    // Building_Door acts like Building_Door with extra features just to avoid RimWorld limitations regarding doors,
    // at this point, shouldn't we consider attacking those limitations directly rather than all these workarounds?
    public class Building_DoorExpanded : Building
    {
        private const float OpenTicks = 45f;
        private const int CloseDelayTicks = 110;
        private const int WillCloseSoonThreshold = 111;
        private const int ApproachCloseDelayTicks = 300;
        private const int MaxTicksSinceFriendlyTouchToAutoClose = 120;
        private const float PowerOffDoorOpenSpeedFactor = 0.25f;
        private const float VisualDoorOffsetStart = 0f;
        internal const float VisualDoorOffsetEnd = 0.45f;

        private List<Building_DoorRegionHandler> invisDoors = new List<Building_DoorRegionHandler>();
        private CompPowerTrader powerComp;
        private CompForbiddable forbiddenComp;
        private bool openInt;
        private bool holdOpenInt;
        private int lastFriendlyTouchTick = -9999;
        protected int ticksUntilClose;
        protected int visualTicksOpen;
        private bool freePassageWhenClearedReachabilityCache;
        private bool lastForbiddenState;

        public DoorExpandedDef Def => (DoorExpandedDef)def;

        public bool Open => Def.doorType == DoorType.FreePassage || openInt;

        protected virtual bool OpenInt
        {
            get => openInt;
            set
            {
                if (openInt == value)
                    return;
                openInt = value;
                foreach (var invisDoor in invisDoors)
                {
                    invisDoor.Open = value;
                }
            }
        }

        public virtual bool HoldOpen => holdOpenInt;

        public bool FreePassage => Open && (HoldOpen || !WillCloseSoon);

        public bool WillCloseSoon
        {
            get
            {
                if (!Spawned)
                {
                    return true;
                }
                if (!Open)
                {
                    return true;
                }
                if (HoldOpen)
                {
                    return false;
                }
                if (ticksUntilClose > 0 && ticksUntilClose <= WillCloseSoonThreshold && !BlockedOpenMomentary)
                {
                    return true;
                }
                if (CanTryCloseAutomatically && !BlockedOpenMomentary)
                {
                    return true;
                }
                var searchRect = this.OccupiedRect().ExpandedBy(1);
                foreach (var c in searchRect)
                {
                    if (!searchRect.IsCorner(c) && c.InBounds(Map))
                    {
                        var thingList = c.GetThingList(Map);
                        for (var j = 0; j < thingList.Count; j++)
                        {
                            if (thingList[j] is Pawn pawn && !pawn.HostileTo(this) && !pawn.Downed &&
                                (pawn.Position == Position || (pawn.pather.Moving && pawn.pather.nextCell == Position)))
                            {
                                return true;
                            }
                        }
                    }
                }
                return true;
            }
        }

        public bool BlockedOpenMomentary
        {
            get
            {
                foreach (var c in this.OccupiedRect())
                {
                    var thingList = c.GetThingList(Map);
                    for (var i = 0; i < thingList.Count; i++)
                    {
                        var thing = thingList[i];
                        if (thing.def.category == ThingCategory.Item || thing.def.category == ThingCategory.Pawn)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
        }

        public bool DoorPowerOn => powerComp != null && powerComp.PowerOn;

        public bool DoorPowerOff => powerComp != null && !powerComp.PowerOn;

        public bool SlowsPawns => /*!DoorPowerOn ||*/ TicksToOpenNow > 20;

        public int TicksToOpenNow
        {
            get
            {
                if (Def.doorType == DoorType.FreePassage)
                {
                    return 0;
                }
                var ticksToOpenNow = OpenTicks / this.GetStatValue(StatDefOf.DoorOpenSpeed);
                if (DoorPowerOn)
                {
                    ticksToOpenNow *= PowerOffDoorOpenSpeedFactor;
                }
                ticksToOpenNow *= Def.doorOpenSpeedRate;
                return Mathf.RoundToInt(ticksToOpenNow);
            }
        }

        internal bool CanTryCloseAutomatically => FriendlyTouchedRecently && !HoldOpen;

        internal protected virtual bool FriendlyTouchedRecently =>
            Find.TickManager.TicksGame < lastFriendlyTouchTick + MaxTicksSinceFriendlyTouchToAutoClose;

        internal int VisualTicksToOpen => TicksToOpenNow;

        public override bool FireBulwark => !Open && base.FireBulwark;

        public virtual bool Forbidden => forbiddenComp?.Forbidden ?? false;

        public override void PostMake()
        {
            base.PostMake();
            powerComp = GetComp<CompPowerTrader>();
            forbiddenComp = GetComp<CompForbiddable>();
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);

            powerComp = GetComp<CompPowerTrader>();
            if (invisDoors.Count == 0)
            {
                // Note: We only want invisDoors to register in the edificeGrid (during its SpawnSetup).
                // A harmony patch prevents this Building_DoorExpanded from being registered in the edificeGrid.
                SpawnDoors(map);
            }
            forbiddenComp = GetComp<CompForbiddable>();
            SetForbidden(Forbidden);
            ClearReachabilityCache(map);
            if (BlockedOpenMomentary)
            {
                DoorOpen();
            }
        }

        private void SpawnDoors(Map map)
        {
            foreach (var c in this.OccupiedRect())
            {
                var invisDoor = c.GetThingList(map).OfType<Building_DoorRegionHandler>().FirstOrDefault();
                var spawnInvisDoor = true;
                if (invisDoor != null)
                {
                    // Spawn over another door? Let's erase that door and add our own invis doors.
                    if (invisDoor.ParentDoor != this && (invisDoor.ParentDoor?.Spawned ?? false))
                    {
                        invisDoor.ParentDoor.DeSpawn();
                    }
                    else
                    {
                        spawnInvisDoor = false;
                    }
                }
                if (spawnInvisDoor)
                {
                    invisDoor = (Building_DoorRegionHandler)ThingMaker.MakeThing(HeronDefOf.HeronInvisibleDoor);
                    invisDoor.ParentDoor = this;
                    invisDoor.SetFactionDirect(Faction);
                    GenSpawn.Spawn(invisDoor, c, map);
                }
                else
                {
                    invisDoor.ParentDoor = this;
                    invisDoor.SetFaction(Faction);
                }
                invisDoor.Open = Open;
                invisDoors.Add(invisDoor);
            }
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            foreach (var invisDoor in invisDoors)
            {
                if (invisDoor.Spawned)
                    invisDoor.DeSpawn(mode);
            }
            invisDoors.Clear();
            var map = Map;
            base.DeSpawn(mode);
            ClearReachabilityCache(map);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref invisDoors, nameof(invisDoors), LookMode.Reference);
            Scribe_Values.Look(ref openInt, "open", false);
            Scribe_Values.Look(ref holdOpenInt, "holdOpen", false);
            Scribe_Values.Look(ref lastFriendlyTouchTick, nameof(lastFriendlyTouchTick), 0);
            if (Scribe.mode == LoadSaveMode.LoadingVars && Open)
            {
                visualTicksOpen = VisualTicksToOpen;
            }
        }

        public override void SetFaction(Faction newFaction, Pawn recruiter = null)
        {
            // This check prevents redundant calls from all the invis doors' SetFaction.
            if (Faction == newFaction)
                return;
            base.SetFaction(newFaction, recruiter);
            foreach (var invisDoor in invisDoors)
            {
                invisDoor.SetFaction(newFaction, recruiter);
            }
            if (Spawned)
            {
                ClearReachabilityCache(Map);
            }
        }

        public override void Tick()
        {
            base.Tick();
            if (FreePassage != freePassageWhenClearedReachabilityCache)
            {
                ClearReachabilityCache(Map);
            }
            if (!Open)
            {
                if (visualTicksOpen > 0)
                {
                    visualTicksOpen--;
                }
                var closedTempLeakRate = Def.tempLeakRate;
                if ((Find.TickManager.TicksGame + thingIDNumber.HashOffset()) % closedTempLeakRate == 0)
                {
                    GenTemperature.EqualizeTemperaturesThroughBuilding(this, TemperatureTuning.Door_TempEqualizeRate, twoWay: false);
                }
            }
            else
            {
                if (visualTicksOpen < VisualTicksToOpen)
                {
                    visualTicksOpen++;
                }
                var isPawnPresent = false;
                foreach (var c in this.OccupiedRect())
                {
                    var thingList = c.GetThingList(Map);
                    for (var i = 0; i < thingList.Count; i++)
                    {
                        if (thingList[i] is Pawn pawn)
                        {
                            CheckFriendlyTouched(pawn);
                            isPawnPresent = true;
                        }
                    }
                }
                if (ticksUntilClose > 0)
                {
                    if (isPawnPresent)
                    {
                        ticksUntilClose = CloseDelayTicks;
                    }
                    ticksUntilClose--;
                    if (ticksUntilClose <= 0 && !HoldOpen && !DoorTryClose())
                    {
                        ticksUntilClose = 1;
                    }
                }
                else if (CanTryCloseAutomatically)
                {
                    ticksUntilClose = CloseDelayTicks;
                }
                if ((Find.TickManager.TicksGame + thingIDNumber.HashOffset()) % TemperatureTuning.Door_TempEqualizeIntervalOpen == 0)
                {
                    GenTemperature.EqualizeTemperaturesThroughBuilding(this, TemperatureTuning.Door_TempEqualizeRate, twoWay: false);
                }
            }
        }

        public void CheckFriendlyTouched(Pawn p)
        {
            if (!p.HostileTo(this) && PawnCanOpen(p))
            {
                lastFriendlyTouchTick = Find.TickManager.TicksGame;
            }
        }

        public void Notify_PawnApproaching(Pawn p, int moveCost)
        {
            CheckFriendlyTouched(p);
            if (PawnCanOpen(p))
            {
                // Following is kinda inefficient, but this isn't perfomance critical code, so it shouldn't matter.
                foreach (var invisDoor in invisDoors)
                {
                    Map.fogGrid.Notify_PawnEnteringDoor(invisDoor, p);
                }
                if (!SlowsPawns)
                {
                    var ticksToClose = Mathf.Max(ApproachCloseDelayTicks, moveCost + 1);
                    DoorOpen(ticksToClose);
                }
            }
        }

        public void Notify_ForbiddenInputChanged()
        {
            var forbidden = Forbidden;
            if (forbidden != lastForbiddenState)
            {
                SetForbidden(forbidden);
            }
        }

        private void SetForbidden(bool forbidden)
        {
            lastForbiddenState = forbidden;
            foreach (var invisDoor in invisDoors)
            {
                invisDoor.SetForbidden(forbidden);
            }
        }

        public bool CanPhysicallyPass(Pawn p)
        {
            return FreePassage || PawnCanOpen(p) || (Open && p.HostileTo(this));
        }

        public virtual bool PawnCanOpen(Pawn p)
        {
            var lord = p.GetLord();
            if (lord != null && lord.LordJob != null && lord.LordJob.CanOpenAnyDoor(p))
            {
                return true;
            }
            if (WildManUtility.WildManShouldReachOutsideNow(p))
            {
                return true;
            }
            if (Faction == null)
            {
                return true;
            }
            if (p.guest != null && p.guest.Released)
            {
                return true;
            }
            return GenAI.MachinesLike(Faction, p);
        }

        public override bool BlocksPawn(Pawn p) => !Open && !PawnCanOpen(p);

        protected internal void DoorOpen(int ticksToClose = CloseDelayTicks)
        {
            if (Open)
            {
                ticksUntilClose = ticksToClose;
            }
            else
            {
                ticksUntilClose = TicksToOpenNow + ticksToClose;
                OpenInt = true;
                CheckClearReachabilityCacheBecauseOpenedOrClosed();
                if (DoorPowerOn)
                {
                    def.building.soundDoorOpenPowered?.PlayOneShot(new TargetInfo(Position, Map));
                }
                else
                {
                    def.building.soundDoorOpenManual?.PlayOneShot(new TargetInfo(Position, Map));
                }
            }
        }

        protected internal bool DoorTryClose()
        {
            if (HoldOpen || BlockedOpenMomentary)
            {
                return false;
            }
            OpenInt = false;
            CheckClearReachabilityCacheBecauseOpenedOrClosed();
            if (DoorPowerOn)
            {
                def.building.soundDoorClosePowered?.PlayOneShot(new TargetInfo(Position, Map));
            }
            else
            {
                def.building.soundDoorCloseManual?.PlayOneShot(new TargetInfo(Position, Map));
            }
            return true;
        }

        public void StartManualOpenBy(Pawn opener)
        {
            DoorOpen();
        }

        public void StartManualCloseBy(Pawn closer)
        {
            ticksUntilClose = CloseDelayTicks;
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
                        defaultLabel = "DEV: Open",
                        defaultDesc = "debug".Translate(),
                        hotKey = KeyBindingDefOf.Misc3,
                        icon = TexCommand.HoldOpen,
                        isActive = () => OpenInt,
                        toggleAction = () => OpenInt = !OpenInt,
                    };
                }
            }
        }

        private void ClearReachabilityCache(Map map)
        {
            map.reachability.ClearCache();
            freePassageWhenClearedReachabilityCache = FreePassage;
        }

        private void CheckClearReachabilityCacheBecauseOpenedOrClosed()
        {
            if (Spawned)
            {
                Map.reachability.ClearCacheForHostile(this);
            }
        }
    }
}
