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
        protected int ticksSinceOpen;
        private bool freePassageWhenClearedReachabilityCache;
        private bool lastForbiddenState;

        public DoorExpandedDef Def => (DoorExpandedDef)def;

        public List<Building_DoorRegionHandler> InvisDoors => invisDoors;

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

        public int TicksTillFullyOpened
        {
            get
            {
                var ticksTillFullyOpened = TicksToOpenNow - ticksSinceOpen;
                if (ticksTillFullyOpened < 0)
                {
                    ticksTillFullyOpened = 0;
                }
                return ticksTillFullyOpened;
            }
        }

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
            // Non-1x1 rotations change the footprint of the blueprint, so this needs to be done before that footprint is
            // cached in various ways in base.SpawnSetup.
            // Fortunately once rotated, no further non-1x1 rotations will change the footprint further.
            // Restricted rotation logic in both patched Designator_Place and Blueprint shouldn't allow invalid rotations by
            // this point, but better safe than sorry, especially if this is spawned without Designator_Place or Blueprint.
            Rotation = DoorRotationAt(Def, Position, Rotation, map);

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
            var spawnedInvisDoors = invisDoors.Where(invisDoor => invisDoor.Spawned).ToArray();
            foreach (var invisDoor in spawnedInvisDoors)
            {
                invisDoor.DeSpawn(mode);
            }
            invisDoors.Clear();
            var map = Map;
            // Note: To be safe, this room notifying is done after all invis doors are despawned.
            // See also HarmonyPatches.InvisDoorRoomNotifyContainedThingSpawnedOrDespawnedPrefix.
            foreach (var invisDoor in spawnedInvisDoors)
            {
                // Following partially copied from Thing.DeSpawn.
                map.regionGrid.GetValidRegionAt_NoRebuild(Position)?.Room?.Notify_ContainedThingSpawnedOrDespawned(invisDoor);
            }
            // Following conditions copied from Building.DeSpawn.
            if (mode != DestroyMode.WillReplace && def.MakeFog)
            {
                // FogGrid.Notify_FogBlockerRemoved needs to be called on all invis door positions (same as OccupiedRect()),
                // or else there can be some cases where despawning a large door doesn't properly defog rooms.
                // Building.DeSpawn only calls this on the single Position location.
                // See also HarmonyPatches.InvisDoorMakeFogTranspiler.
                // Following is kinda inefficient, but this isn't perfomance critical code, so it shouldn't matter.
                foreach (var c in this.OccupiedRect())
                {
                    map.fogGrid.Notify_FogBlockerRemoved(c);
                }
            }
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
                ticksSinceOpen = TicksToOpenNow;
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
                if (ticksSinceOpen > 0)
                {
                    ticksSinceOpen--;
                }
                var closedTempLeakRate = Def.tempLeakRate;
                if ((Find.TickManager.TicksGame + thingIDNumber.HashOffset()) % closedTempLeakRate == 0)
                {
                    GenTemperature.EqualizeTemperaturesThroughBuilding(this, TemperatureTuning.Door_TempEqualizeRate, twoWay: false);
                }
            }
            else
            {
                if (ticksSinceOpen < TicksToOpenNow)
                {
                    ticksSinceOpen++;
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
            var pawnCanOpen = PawnCanOpen(p);
            if (pawnCanOpen || Open)
            {
                // Following is kinda inefficient, but this isn't perfomance critical code, so it shouldn't matter.
                foreach (var invisDoor in invisDoors)
                {
                    Map.fogGrid.Notify_PawnEnteringDoor(invisDoor, p);
                }
            }
            if (pawnCanOpen && !SlowsPawns)
            {
                var ticksToClose = Mathf.Max(ApproachCloseDelayTicks, moveCost + 1);
                DoorOpen(ticksToClose);
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

        // This exists to expose draw vectors for debugging purposes.
        internal class DebugDrawVectors
        {
            public float percentOpen;
            public Vector3 offsetVector, scaleVector, graphicVector;
        }

        internal DebugDrawVectors debugDrawVectors = new DebugDrawVectors();

        public override void Draw()
        {
            var ticksToOpenNow = TicksToOpenNow;
            var percentOpen = ticksToOpenNow == 0 ? 1f : Mathf.Clamp01((float)ticksSinceOpen / ticksToOpenNow);
            var def = Def;

            var rotation = DoorRotationAt(def, Position, Rotation, Map);
            Rotation = rotation;

            var drawPos = DrawPos;
            drawPos.y = AltitudeLayer.DoorMoveable.AltitudeFor();

            for (var i = 0; i < 2; i++)
            {
                var flipped = i != 0;
                var material = (!flipped && def.doorAsync is GraphicData doorAsyncGraphic)
                    ? doorAsyncGraphic.GraphicColoredFor(this).MatAt(rotation)
                    : Graphic.MatAt(rotation);
                Draw(def, material, drawPos, rotation, percentOpen, flipped,
                    i == 0 && MoreDebugViewSettings.writeDoors ? debugDrawVectors : null);
                if (def.singleDoor)
                    break;
            }

            if (def.doorFrame is GraphicData)
            {
                DrawFrameParams(def, drawPos, rotation, false, out var fMesh, out var fMatrix);
                Graphics.DrawMesh(fMesh, fMatrix, def.doorFrame.GraphicColoredFor(this).MatAt(rotation), 0);

                if (def.doorFrameSplit is GraphicData)
                {
                    DrawFrameParams(def, drawPos, rotation, true, out fMesh, out fMatrix);
                    Graphics.DrawMesh(fMesh, fMatrix, def.doorFrameSplit.GraphicColoredFor(this).MatAt(rotation), 0);
                }
            }

            Comps_PostDraw();
        }

        internal static void Draw(DoorExpandedDef def, Material material, Vector3 drawPos, Rot4 rotation, float percentOpen,
            bool flipped, DebugDrawVectors drawVectors = null)
        {
            Mesh mesh;
            Quaternion rotQuat;
            Vector3 offsetVector, scaleVector;
            switch (def.doorType)
            {
                // There's no difference between Stretch and StretchVertical except for stretchOpenSize's default value.
                case DoorType.Stretch:
                case DoorType.StretchVertical:
                    DrawStretchParams(def, rotation, percentOpen, flipped,
                        out mesh, out rotQuat, out offsetVector, out scaleVector);
                    break;
                case DoorType.DoubleSwing:
                    // TODO: Should drawPos.y be set to Mathf.Max(drawPos.y, AltitudeLayer.BuildingOnTop.AltitudeFor())
                    // since AltitudeLayer.DoorMoveable is only used to hide sliding doors behind adjacent walls?
                    DrawDoubleSwingParams(def, drawPos, rotation, percentOpen, flipped,
                        out mesh, out rotQuat, out offsetVector, out scaleVector);
                    break;
                default:
                    DrawStandardParams(def, rotation, percentOpen, flipped,
                        out mesh, out rotQuat, out offsetVector, out scaleVector);
                    break;
            }
            var graphicVector = drawPos + offsetVector;
            var matrix = Matrix4x4.TRS(graphicVector, rotQuat, scaleVector);
            Graphics.DrawMesh(mesh, matrix, material, layer: 0);

            if (drawVectors != null)
            {
                drawVectors.percentOpen = percentOpen;
                drawVectors.offsetVector = offsetVector;
                drawVectors.scaleVector = scaleVector;
                drawVectors.graphicVector = graphicVector;
            }
        }

        private static void DrawStretchParams(DoorExpandedDef def, Rot4 rotation,
            float percentOpen, bool flipped, out Mesh mesh, out Quaternion rotQuat,
            out Vector3 offsetVector, out Vector3 scaleVector)
        {
            var drawSize = def.graphicData.drawSize;
            var closeSize = def.stretchCloseSize;
            var openSize = def.stretchOpenSize;
            var offset = def.stretchOffset.Value;

            var verticalRotation = rotation.IsHorizontal;
            var persMod = verticalRotation && def.fixedPerspective ? 2f : 1f;

            offsetVector = new Vector3(offset.x * percentOpen * persMod, 0f, offset.y * percentOpen * persMod);

            var scaleX = Mathf.LerpUnclamped(openSize.x, closeSize.x, 1 - percentOpen) / closeSize.x * drawSize.x * persMod;
            var scaleZ = Mathf.LerpUnclamped(openSize.y, closeSize.y, 1 - percentOpen) / closeSize.y * drawSize.y * persMod;
            scaleVector = new Vector3(scaleX, 1f, scaleZ);

            // South-facing stretch animation should have same vertical direction as north-facing one.
            if (rotation == Rot4.South)
                offsetVector.z = -offsetVector.z;

            if (!flipped)
            {
                mesh = MeshPool.plane10;
            }
            else
            {
                offsetVector.x = -offsetVector.x;
                mesh = MeshPool.plane10Flip;
            }

            rotQuat = rotation.AsQuat;
            offsetVector = rotQuat * offsetVector;
        }

        private static void DrawDoubleSwingParams(DoorExpandedDef def, Vector3 drawPos, Rot4 rotation,
            float percentOpen, bool flipped, out Mesh mesh, out Quaternion rotQuat,
            out Vector3 offsetVector, out Vector3 scaleVector)
        {
            var verticalRotation = rotation.IsHorizontal;
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

            if (verticalRotation)
                rotQuat = Quaternion.AngleAxis(rotation.AsAngle + (percentOpen * (flipped ? 90f : -90f)), Vector3.up);
            else
                rotQuat = rotation.AsQuat;
            offsetVector = rotQuat * offsetVector;

            var offsetMod = (VisualDoorOffsetStart + def.doorOpenMultiplier * percentOpen) * def.Size.x;
            offsetVector *= offsetMod;

            if (verticalRotation)
            {
                if (!flipped && rotation == Rot4.East
                    || flipped && rotation == Rot4.West)
                {
                    offsetVector.y = Mathf.Max(0f, AltitudeLayer.BuildingOnTop.AltitudeFor() - drawPos.y);
                }
            }

            var drawSize = def.graphicData.drawSize;
            var persMod = verticalRotation && def.fixedPerspective ? 2f : 1f;
            scaleVector = new Vector3(drawSize.x * persMod, 1f, drawSize.y * persMod);
        }

        private static void DrawStandardParams(DoorExpandedDef def, Rot4 rotation,
            float percentOpen, bool flipped, out Mesh mesh, out Quaternion rotQuat,
            out Vector3 offsetVector, out Vector3 scaleVector)
        {
            var verticalRotation = rotation.IsHorizontal;
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

            rotQuat = rotation.AsQuat;
            offsetVector = rotQuat * offsetVector;

            var offsetMod = (VisualDoorOffsetStart + def.doorOpenMultiplier * percentOpen) * def.Size.x;
            offsetVector *= offsetMod;

            var drawSize = def.graphicData.drawSize;
            var persMod = verticalRotation && def.fixedPerspective ? 2f : 1f;
            scaleVector = new Vector3(drawSize.x * persMod, 1f, drawSize.y * persMod);
        }

        private static void DrawFrameParams(DoorExpandedDef def, Vector3 drawPos, Rot4 rotation, bool split,
            out Mesh mesh, out Matrix4x4 matrix)
        {
            var verticalRotation = rotation.IsHorizontal;
            var offsetVector = new Vector3(-1f, 0f, 0f);
            mesh = MeshPool.plane10;

            if (def.doorFrameSplit != null)
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

            var offsetMod = (VisualDoorOffsetStart + def.doorOpenMultiplier * 1f) * def.Size.x;
            offsetVector *= offsetMod;

            var drawSize = def.doorFrame.drawSize;
            var persMod = verticalRotation && def.fixedPerspective ? 2f : 1f;
            var scaleVector = new Vector3(drawSize.x * persMod, 1f, drawSize.y * persMod);

            var graphicVector = drawPos;
            graphicVector.y = AltitudeLayer.Blueprint.AltitudeFor();
            if (rotation == Rot4.North || rotation == Rot4.South)
                graphicVector.y = AltitudeLayer.PawnState.AltitudeFor();
            if (!verticalRotation)
                graphicVector.x += offsetMod;
            if (rotation == Rot4.East)
            {
                graphicVector.z -= offsetMod;
                if (split)
                    graphicVector.y = AltitudeLayer.DoorMoveable.AltitudeFor();
            }
            else if (rotation == Rot4.West)
            {
                graphicVector.z += offsetMod;
                if (split)
                    graphicVector.y = AltitudeLayer.DoorMoveable.AltitudeFor();
            }
            graphicVector += offsetVector;

            var frameOffsetVector = def.doorFrameOffset;
            if (def.doorFrameSplit != null)
            {
                if (rotation == Rot4.West)
                {
                    rotQuat = Quaternion.Euler(0f, 270f, 0f);
                    graphicVector.z -= 2.7f;
                    mesh = MeshPool.plane10Flip;
                    frameOffsetVector = def.doorFrameSplitOffset;
                }
            }
            graphicVector += frameOffsetVector;

            matrix = Matrix4x4.TRS(graphicVector, rotQuat, scaleVector);
        }

        public static Rot4 DoorRotationAt(DoorExpandedDef def, IntVec3 loc, Rot4 rot, Map map)
        {
            if (!def.rotatable)
            {
                var size = def.Size;
                if ((size.x == 1 && size.z == 1) || def.doorType == DoorType.StretchVertical || def.doorType == DoorType.Stretch)
                    rot = Building_Door.DoorRotationAt(loc, map);
            }
            if (!def.rotatesSouth && rot == Rot4.South)
                rot = Rot4.North;
            return rot;
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
