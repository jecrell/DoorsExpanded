using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using UnityEngine;
using Verse.Sound;
using Verse.AI.Group;
using Verse.AI;
using System.Diagnostics;
using System.Globalization;
using Harmony;
//using Reloader;


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
        private List<Building_DoorRegionHandler> invisDoors;
        
        public List<Building_DoorRegionHandler> InvisDoors => invisDoors ?? (invisDoors = new List<Building_DoorRegionHandler>());

        public List<Pawn> crossingPawns;
        
        public DoorExpandedDef Def => this.def as DoorExpandedDef;

        public void SpawnDoors()
        {
            InvisDoors.Clear();
            foreach (IntVec3 c in this.OccupiedRect().Cells)
            {
                Building_DoorRegionHandler thing = (Building_DoorRegionHandler)ThingMaker.MakeThing(HeronDefOf.HeronInvisibleDoor);
                thing.ParentDoor = this;
                GenSpawn.Spawn(thing, c, MapHeld);
                thing.SetFaction(this.Faction);
                AccessTools.Field(typeof(Building_DoorRegionHandler), "holdOpenInt").SetValue(thing, true);
                InvisDoors.Add(thing);
            }
            this.ClearReachabilityCache(this.MapHeld);
            if (this.BlockedOpenMomentary)
                this.DoorOpen(60);
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            crossingPawns = new List<Pawn>();

            base.SpawnSetup(map, respawningAfterLoad);
            this.powerComp = base.GetComp<CompPowerTrader>();
            
        }

        public override bool BlocksPawn(Pawn p)
        {
            return Def.doorType != DoorType.FreePassage && !this.openInt && !this.PawnCanOpen(p);
        }

        public bool Open
        {
            get
            {
                return Def.doorType == DoorType.FreePassage || this.openInt;
            }
        }

        public override void DeSpawn()
        {
            if (invisDoors?.Count > 0)
            {
                foreach (Building_DoorRegionHandler door in invisDoors)
                {
                    door.Destroy(DestroyMode.Vanish);
                }
            }
            base.DeSpawn();
        }
        
        //[ReloadMethod]
        public override void Draw()
        {
            float num = Mathf.Clamp01((float)this.visualTicksOpen / (float)this.VisualTicksToOpen);
            float num2 = this.def.Size.x;
            float d = (0f + 0.45f * num) * num2;
            Rot4 rotation = base.Rotation;
            if (!Def.rotatesSouth && this.Rotation == Rot4.South) rotation = Rot4.North;
            for (int i = 0; i < 2; i++)
            {
                bool flipped = (i != 0) ? true : false;
                Mesh mesh;
                Matrix4x4 matrix;

                switch (Def.doorType)
                {
                    case DoorType.Stretch:
                        DrawStretchParams(Def, this.DrawPos, rotation, out mesh, out matrix, d, flipped);
                        break;
                    case DoorType.DoubleSwing:
                        DrawDoubleSwingParams(Def, this.DrawPos, rotation, out mesh, out matrix, d, flipped);
                        break;
                    default:
                        DrawParams(Def, this.DrawPos, rotation, out mesh, out matrix, d, flipped);
                        break;
                }
                Material matToDraw = (!flipped && Def.doorAsync is GraphicData dA) ? dA.GraphicColoredFor(this).MatAt(rotation) : this.Graphic.MatAt(rotation);
                Graphics.DrawMesh(mesh, matrix, matToDraw, 0);
                if (Def.singleDoor)
                    break;
            }
            if (Def.doorFrame is GraphicData f)
            {
                Mesh fMesh;
                Matrix4x4 fMatrix;
                DrawFrameParams(Def, this.DrawPos, rotation, false, out fMesh, out fMatrix);

                //Rot4 currRot = (Def.fixedPerspective && this.Rotation == Rot4.West) ? Rot4.East : base.Rotation;
                Graphics.DrawMesh(fMesh, fMatrix, Def.doorFrame.GraphicColoredFor(this).MatAt(rotation), 0);
                if (Def.doorFrameSplit is GraphicData ff)
                { 
                    DrawFrameParams(Def, this.DrawPos, rotation, true, out fMesh, out fMatrix);
                    Graphics.DrawMesh(fMesh, fMatrix, Def.doorFrameSplit.GraphicColoredFor(this).MatAt(rotation), 0);
                }
            }
            base.Comps_PostDraw();
        }
        
        //[ReloadMethod]
        public void DrawFrameParams(DoorExpandedDef thingDef, Vector3 drawPos, Rot4 rotation, bool split, out Mesh mesh, out Matrix4x4 matrix)
        {
            float d = (0f + 0.45f * 1) * thingDef.Size.x;
            bool verticalRotation = rotation.IsHorizontal;
            Vector3 rotationVector = default(Vector3);
            rotationVector = new Vector3(-1f, 0f, 0f);
            mesh = MeshPool.plane10;


            if (thingDef.doorFrameSplit != null)
            {
                if (rotation == Rot4.West)
                {
                    //;
                    rotationVector.x = 1f;
                    //rotationVector.z *= -1f;
                    //rotationVector.y *= -1f;
                }
            }

            Quaternion rotQuat = rotation.AsQuat;
            rotationVector = rotQuat * rotationVector;


            Vector3 graphicVector = drawPos;
            graphicVector.y = Altitudes.AltitudeFor(AltitudeLayer.Blueprint);
            if (rotation == Rot4.North || rotation == Rot4.South) graphicVector.y = Altitudes.AltitudeFor(AltitudeLayer.PawnState);
            if (!verticalRotation) graphicVector.x += d;
            if (rotation == Rot4.East) { graphicVector.z -= d; if (split) graphicVector.y = Altitudes.AltitudeFor(AltitudeLayer.DoorMoveable); }
            if (rotation == Rot4.West) { graphicVector.z += d; if (split) graphicVector.y = Altitudes.AltitudeFor(AltitudeLayer.DoorMoveable); }


            graphicVector += rotationVector * d;


            float persMod = (thingDef.fixedPerspective) ? 2f : 1f;
            Vector3 scaleVector = (verticalRotation) ?
                new Vector3(thingDef.doorFrame.drawSize.x * persMod, 1f, thingDef.doorFrame.drawSize.y * persMod) :
                new Vector3(thingDef.doorFrame.drawSize.x, 1f, thingDef.doorFrame.drawSize.y);

            Vector3 offset = thingDef.doorFrameOffset;
            if (thingDef.doorFrameSplit != null)
            {
                if (rotation == Rot4.West)
                {
                    rotQuat = Quaternion.Euler(0, 270, 0); //new Quaternion(0, 0.7f, 0, -0.7f);// Euler(0, 270, 0);
                    graphicVector.z -= 2.7f;
                    mesh = MeshPool.plane10Flip;
                    offset = thingDef.doorFrameSplitOffset;
                }
            }
            graphicVector += offset;
            
            
            matrix = default(Matrix4x4);
            matrix.SetTRS(graphicVector, rotQuat, scaleVector);
        }
        
        public static void DrawParams(DoorExpandedDef thingDef, Vector3 drawPos, Rot4 rotation, out Mesh mesh, out Matrix4x4 matrix, float mod = 1, bool flipped = false)
        {
            bool verticalRotation = rotation.IsHorizontal;
            Vector3 rotationVector = default(Vector3);
            if (!flipped)
            {
                rotationVector = new Vector3(-1f, 0f, 0f);
                mesh = MeshPool.plane10;
            }
            else
            {

                rotationVector = new Vector3(1f, 0f, 0f);
                mesh = MeshPool.plane10Flip;
            }

            Quaternion rotQuat = rotation.AsQuat;
            rotationVector = rotQuat * rotationVector;

            Vector3 graphicVector = drawPos;
            graphicVector.y = Altitudes.AltitudeFor(AltitudeLayer.DoorMoveable);
            graphicVector += rotationVector * mod;

            //Vector3 scaleVector = new Vector3(thingDef.graphicData.drawSize.x, 1f, thingDef.graphicData.drawSize.y);
            float persMod = (thingDef.fixedPerspective) ? 2f : 1f;
            Vector3 scaleVector = (verticalRotation) ?
                new Vector3(thingDef.graphicData.drawSize.x * persMod, 1f, thingDef.graphicData.drawSize.y * persMod) :
                new Vector3(thingDef.graphicData.drawSize.x, 1f, thingDef.graphicData.drawSize.y);


            matrix = default(Matrix4x4);
            matrix.SetTRS(graphicVector, rotQuat, scaleVector);
        }

        //[ReloadMethod]
        public void DrawStretchParams(DoorExpandedDef thingDef, Vector3 drawPos, Rot4 rotation, out Mesh mesh, out Matrix4x4 matrix, float mod = 1, bool flipped = false)
        {
            base.Rotation = Building_Door.DoorRotationAt(base.Position, base.Map);
            bool verticalRotation = base.Rotation.IsHorizontal;
            Vector3 rotationVector = default(Vector3);
            if (!flipped)
            {
                rotationVector = new Vector3(0f, 0f, -1f);
                mesh = MeshPool.plane10;
            }
            else
            {
                rotationVector = new Vector3(0f, 0f, 1f);
                mesh = MeshPool.plane10Flip;
            }
            rotation.Rotate(RotationDirection.Clockwise);
            rotationVector = rotation.AsQuat * rotationVector;
            
            Vector3 graphicVector = drawPos;
            graphicVector.y = Altitudes.AltitudeFor(AltitudeLayer.DoorMoveable);
            graphicVector += rotationVector * (mod * 1.15f);

            //Vector3 scaleVector = new Vector3(thingDef.graphicData.drawSize.x, 1f, thingDef.graphicData.drawSize.y);
            float persMod = (thingDef.fixedPerspective) ? 2f : 1f;
            Vector3 scaleVector = (verticalRotation) ?
                new Vector3((thingDef.graphicData.drawSize.x * persMod)- mod * 1.3f, 1f, thingDef.graphicData.drawSize.y * persMod) :
                new Vector3((thingDef.graphicData.drawSize.x)- mod * 1.3f, 1f, thingDef.graphicData.drawSize.y);


            matrix = default(Matrix4x4);
            matrix.SetTRS(graphicVector, base.Rotation.AsQuat, scaleVector);
        }


        public static void DrawDoubleSwingParams(DoorExpandedDef thingDef, Vector3 drawPos, Rot4 rotation, out Mesh mesh, out Matrix4x4 matrix, float mod = 1, bool flipped = false)
        {
            bool verticalRotation = rotation.IsHorizontal;
            rotation = (rotation == Rot4.South) ? Rot4.North : rotation;
            Vector3 rotationVector = default(Vector3);
            mesh = null;
            if (!flipped)
            {
                rotationVector = new Vector3(-1f, 0f, 0f);
                if (verticalRotation)
                    rotationVector = new Vector3(1.4f, 0f, 1.1f);
                mesh = MeshPool.plane10;
            }
            else
            {
                rotationVector = new Vector3(1f, 0f, 0f);
                if (verticalRotation)
                    rotationVector = new Vector3(-1.4f, 0f, 1.1f);
                mesh = MeshPool.plane10Flip;
            }

            Quaternion rotQuat = rotation.AsQuat;
            if (verticalRotation)
            {
                rotQuat = (!flipped) ?
                    Quaternion.AngleAxis(rotation.AsAngle + (mod * -100f), Vector3.up) :
                    Quaternion.AngleAxis(rotation.AsAngle + (mod * 100f), Vector3.up);

            }
            rotationVector = rotQuat * rotationVector;


            Vector3 graphicVector = drawPos;
            graphicVector.y = Altitudes.AltitudeFor(AltitudeLayer.DoorMoveable);
            if (verticalRotation)
            {
                if (!flipped && rotation == Rot4.East
                   || flipped && rotation == Rot4.West)
                    graphicVector.y = Altitudes.AltitudeFor(AltitudeLayer.BuildingOnTop);
            }

            //if (!verticalRotation)
            //if (verticalRotation) mod *= 2f;
            graphicVector += rotationVector * mod;

            float persMod = (thingDef.fixedPerspective) ? 2f : 1f; 
            Vector3 scaleVector = (verticalRotation) ?
                new Vector3(thingDef.graphicData.drawSize.x * persMod, 1f, thingDef.graphicData.drawSize.y * persMod) :
                new Vector3(thingDef.graphicData.drawSize.x, 1f, thingDef.graphicData.drawSize.y);

            matrix = default(Matrix4x4);
            matrix.SetTRS(graphicVector, rotQuat, scaleVector);
        }

        #region Building_Door Copy
        private bool openInt;
        private bool holdOpenInt;
        protected int ticksUntilClose;
        private int lastFriendlyTouchTick = -9999;
        protected int visualTicksOpen;
        private bool freePassageWhenClearedReachabilityCache;
        public CompPowerTrader powerComp;

        // RimWorld.Building_Door
        public bool SlowsPawns
        {
            get
            {
                return Def.doorType != DoorType.FreePassage && this.TicksToOpenNow > 20;
                //return !this.DoorPowerOn || this.TicksToOpenNow > 20;
            }
        }


        // RimWorld.Building_Door
        public virtual bool PawnCanOpen(Pawn p)
        {
            Lord lord = p.GetLord();
            return Def.doorType == DoorType.FreePassage || (lord != null && lord.LordJob != null && lord.LordJob.CanOpenAnyDoor(p)) || 
                   (p.IsWildMan() && !p.mindState.wildManEverReachedOutside) || base.Faction == null || 
                   (p.guest != null && p.guest.Released) || GenAI.MachinesLike(base.Faction, p);
        }


        // RimWorld.Building_Door
        public void Notify_PawnApproaching(Pawn p)
        {
            //Log.Message("PawnPawn!");
            if (crossingPawns.Contains(p)) return;
            //Log.Message("PawnPawn!2");
            //Log.Message("PawnPawn!3");

            if (!p.HostileTo(this))
            {
                this.FriendlyTouched(p);
            }
            
            if (this.PawnCanOpen(p))
            {

                //base.Map.fogGrid.Notify_PawnEnteringDoor(this, p);
                if (!crossingPawns.Contains(p))
                    crossingPawns.Add(p);

                //Log.Message("PawnPawn!4");

                //base.Map.fogGrid.Notify_PawnEnteringDoor(this, p);
                if (!this.SlowsPawns)
                {
                    //Log.Message("PawnPawn!5");

                    this.DoorOpen(120);
                    //Log.Message("PawnPawn!6");

                }
            }
        }


        public void DoorOpen(int ticksToClose = 60)
        {
            this.ticksUntilClose = ticksToClose;
            if (!this.openInt)
            {
                this.openInt = true;
                if (this.DoorPowerOn)
                {
                    var buildingSoundDoorOpenPowered = this.def?.building?.soundDoorOpenPowered;
                    if (buildingSoundDoorOpenPowered != null)
                        buildingSoundDoorOpenPowered.PlayOneShot(new TargetInfo(base.Position, base.Map,
                            false));
                }
                else
                {
                    var buildingSoundDoorOpenManual = this.def?.building?.soundDoorOpenManual;
                    if (buildingSoundDoorOpenManual != null)
                        buildingSoundDoorOpenManual.PlayOneShot(
                            new TargetInfo(base.Position, base.Map, false));
                }
                foreach (Building_DoorRegionHandler door in invisDoors)
                {
                    door.OpenMe(ticksToClose * Mathf.Max(Def.Size.x, Def.Size.z) * 2);
                    //AccessTools.Method(typeof(Building_Door), "DoorOpen").Invoke(door, new object[] { ticksToClose * Mathf.Max(Def.Size.x, Def.Size.z) * 2});
                }
            }
        }

        private bool CanCloseAutomatically
        {
            get
            {
                return this.DoorPowerOn && this.FriendlyTouchedRecently;
            }
        }

        public bool WillCloseSoon
        {
            get
            {
                if (!base.Spawned)
                {
                    return true;
                }
                if (!this.openInt)
                {
                    return true;
                }
                if (this.holdOpenInt)
                {
                    return false;
                }
                if (this.ticksUntilClose > 0 && this.ticksUntilClose <= 60 && this.CanCloseAutomatically)
                {
                    return true;
                }
                for (int i = 0; i < 5; i++)
                {
                    IntVec3 c = base.Position + GenAdj.CardinalDirectionsAndInside[i];
                    if (c.InBounds(base.Map))
                    {
                        List<Thing> thingList = c.GetThingList(base.Map);
                        for (int j = 0; j < thingList.Count; j++)
                        {
                            Pawn pawn = thingList[j] as Pawn;
                            if (pawn != null && !pawn.HostileTo(this))
                            {
                                if (pawn.Position == base.Position || (pawn.pather.MovingNow && pawn.pather.nextCell == base.Position))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
                return false;
            }
        }


        public bool FreePassage
        {
            get
            {
                return Def.doorType == DoorType.FreePassage || this.openInt && (this.holdOpenInt || !this.WillCloseSoon);
            }
        }


        private readonly int friendlyTouchTicks = 97;
        private float friendlyTouchTicksFactor = 1f;
        private bool FriendlyTouchedRecently
        {
            get
            {
                return Find.TickManager.TicksGame < this.lastFriendlyTouchTick + (int)(friendlyTouchTicks * friendlyTouchTicksFactor);
            }
        }

        public bool DoorPowerOn
        {
            get
            {
                return this.powerComp != null && this.powerComp.PowerOn;
            }
        }
        
       

        public override void PostMake()
        {
            base.PostMake();
            this.powerComp = base.GetComp<CompPowerTrader>();
        }

        public int TicksToOpenNow
        {
            get
            {
                float num = 45f / this.GetStatValue(StatDefOf.DoorOpenSpeed, true);
                if (this.DoorPowerOn)
                {
                    num *= 0.25f;
                }
                num *= Def.doorOpenSpeedRate;
                if (Def.doorType == DoorType.FreePassage)
                {
                    num *= 0.01f;
                }
                return Mathf.RoundToInt(num);
            }
        }

        public int VisualTicksToOpen
        {
            get
            {
                return this.TicksToOpenNow;
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
            this.friendlyTouchTicksFactor = 1.0f;
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
            this.lastFriendlyTouchTick = Find.TickManager.TicksGame;
        }

        private void ClearReachabilityCache(Map map)
        {
            map.reachability.ClearCache();
            this.freePassageWhenClearedReachabilityCache = this.FreePassage;
        }

        List<Pawn> temp = new List<Pawn>();
        private bool lastForbidSetting = false;
        public override void Tick()
        {
            base.Tick();
            if (!this.Spawned || this.DestroyedOrNull())
                return;
            if (this.invisDoors.NullOrEmpty())
                this.SpawnDoors();

            if (this.TryGetComp<CompForbiddable>() is CompForbiddable compForbiddable && compForbiddable.Forbidden != lastForbidSetting)
            {
                lastForbidSetting = compForbiddable.Forbidden;
                foreach (var doorToForbid in this.invisDoors)
                    doorToForbid.SetForbidden(compForbiddable.Forbidden);
            }

            int closedTempLeakRate = Def?.tempLeakRate ?? 375;
            if (Find.TickManager.TicksGame % friendlyTouchTicks == 0 && crossingPawns.Count > 0)
            {
                temp.Clear();
                temp = new List<Pawn>(crossingPawns);
                foreach (Pawn p in temp)
                    if (!p.PositionHeld.IsInside(this))
                        crossingPawns.Remove(p);
            }
            else
                this.DoorTryClose();
            
            if (this.FreePassage != this.freePassageWhenClearedReachabilityCache)
                this.ClearReachabilityCache(base.Map);
            
            if (!this.openInt)
            {
                if (this.visualTicksOpen > 0)
                    this.visualTicksOpen--;
                if ((Find.TickManager.TicksGame + this.thingIDNumber.HashOffset()) % closedTempLeakRate == 0)
                    GenTemperature.EqualizeTemperaturesThroughBuilding(this, 1f, false);
            }
            else if (this.openInt)
            {
                if (this.visualTicksOpen < this.VisualTicksToOpen)
                    this.visualTicksOpen++;
                if (!this.holdOpenInt)
                {
                    bool isPawnPresent = false;
                    foreach (var door in invisDoors)
                    {
                        if (!base.Map.thingGrid.CellContains(door.PositionHeld, ThingCategory.Pawn)) continue;
                        door.OpenValue = true;
                        door.TicksUntilClose = 60;
                        isPawnPresent = true;
                    }
                    if (!isPawnPresent)
                    {
                        this.ticksUntilClose--;
                        if (this.ticksUntilClose <= 0 && this.CanCloseAutomatically)
                        {
                            this.DoorTryClose();
                            foreach (var door in invisDoors)
                            {
                                AccessTools.Method(typeof(Building_Door), "DoorTryClose").Invoke(door, null);
                            }
                        }
                    }
                    else
                    {
                        this.ticksUntilClose = 60;
                    }
                }
                if ((Find.TickManager.TicksGame + this.thingIDNumber.HashOffset()) % 22 == 0)
                {
                    GenTemperature.EqualizeTemperaturesThroughBuilding(this, 1f, false);
                }
            }
        }


        public bool BlockedOpenMomentary => !InvisDoors.NullOrEmpty() && Enumerable.Any(InvisDoors, r => r.BlockedOpenMomentary);

        public void DoorTryClose()
        {
            if (!this.openInt || holdOpenInt || this.BlockedOpenMomentary || FriendlyTouchedRecently)
            {
                return;
            }
         
            foreach (Building_DoorRegionHandler handler in InvisDoors)
            {
                if (handler.Open) AccessTools.Field(typeof(Building_Door), "openInt").SetValue(handler, false); //AccessTools.Method(typeof(Building_Door), "DoorTryClose").Invoke(handler, null);
            }
            this.openInt = false;
            if (this.DoorPowerOn)
            {
                var buildingSoundDoorClosePowered = this.def?.building?.soundDoorClosePowered;
                if (buildingSoundDoorClosePowered != null)
                    buildingSoundDoorClosePowered.PlayOneShot(new TargetInfo(base.Position, base.Map, false));
            }
            else
            {
                var buildingSoundDoorCloseManual = this.def?.building?.soundDoorCloseManual;
                if (buildingSoundDoorCloseManual != null)
                    buildingSoundDoorCloseManual.PlayOneShot(new TargetInfo(base.Position, base.Map, false));
            }
        }

        // RimWorld.Building_Door
        public void StartManualOpenBy(Pawn opener)
        {
            //if (PawnCanOpen(opener))
            this.DoorOpen(60);
        }

        // RimWorld.Building_Door
        public void StartManualCloseBy(Pawn closer)
        {
            this.DoorTryClose();
        }

        [DebuggerHidden]
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
            {
                yield return g;
            }
            if (base.Faction == Faction.OfPlayer)
            {
                yield return new Command_Toggle
                {
                    defaultLabel = "CommandToggleDoorHoldOpen".Translate(),
                    defaultDesc = "CommandToggleDoorHoldOpenDesc".Translate(),
                    hotKey = KeyBindingDefOf.Misc3,
                    icon = TexCommand.HoldOpen,
                    isActive = (() => this.holdOpenInt),
                    toggleAction = delegate
                    {
                        this.holdOpenInt = !this.holdOpenInt;
                    }
                };
                if (DebugSettings.godMode)
                {
                    yield return new Command_Toggle
                    {
                        defaultLabel = "DEV: openInt",
                        defaultDesc = "debug".Translate(),
                        hotKey = KeyBindingDefOf.Misc3,
                        icon = TexCommand.HoldOpen,
                        isActive = (() => this.openInt),
                        toggleAction = delegate
                        {
                            this.openInt = !this.openInt;
                        }
                    };   
                }
            }
        }


        public override void ExposeData()
        {
            base.ExposeData();
            //Scribe_Collections.Look<Building_DoorRegionHandler>(ref this.invisDoors, "invisDoors", LookMode.Reference);
            Scribe_Collections.Look<Pawn>(ref this.crossingPawns, "crossingPawns", LookMode.Reference);
            Scribe_Values.Look<bool>(ref this.openInt, "open", false, false);
            Scribe_Values.Look<bool>(ref this.holdOpenInt, "holdOpen", false, false);
            Scribe_Values.Look<int>(ref this.lastFriendlyTouchTick, "lastFriendlyTouchTick", 0, false);
/*            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (this.invisDoors != null && this.invisDoors?.Count > 0)
                {
                    foreach (Building_DoorRegionHandler invisDoor in invisDoors)
                    {
                        invisDoor.Destroy(DestroyMode.Vanish);
                    }
                    invisDoors = null;
                }
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (this.invisDoors != null && this.invisDoors?.Count > 0)
                {
                    foreach (Building_DoorRegionHandler invisDoor in invisDoors)
                    {
                        invisDoor.Destroy(DestroyMode.Vanish);
                    }
                    invisDoors = null;
                    this.SpawnDoors();
                }

                if (this.openInt)
                    this.visualTicksOpen = this.VisualTicksToOpen;
            }*/
        }
        #endregion Building_Door Copy
        
    }
}
