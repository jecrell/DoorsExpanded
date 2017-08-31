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
using Harmony;
using Reloader;


namespace ProjectHeron
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
        public List<Building_DoorRegionHandler> invisDoors;

        public DoorExpandedDef Def => this.def as DoorExpandedDef;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            invisDoors = new List<Building_DoorRegionHandler>();
            foreach (IntVec3 c in this.OccupiedRect().Cells)
            {
                Building_DoorRegionHandler thing = (Building_DoorRegionHandler)ThingMaker.MakeThing(HeronDefOf.HeronInvisibleDoor, null);
                thing.ParentDoor = this;
                GenSpawn.Spawn(thing, c, map);
                invisDoors.Add(thing as Building_DoorRegionHandler);
            }
            base.SpawnSetup(map, respawningAfterLoad);
            this.powerComp = base.GetComp<CompPowerTrader>();
            this.ClearReachabilityCache(map);
            if (this.BlockedOpenMomentary)
            {
                this.DoorOpen(60);
            }
        }

        public override bool BlocksPawn(Pawn p)
        {
            return !this.openInt && !this.PawnCanOpen(p);
        }

        public bool Open
        {
            get
            {
                return this.openInt;
            }
        }

        public override void DeSpawn()
        {
            if (!invisDoors.NullOrEmpty())
            {
                foreach (Building_DoorRegionHandler door in invisDoors)
                {
                    door.Destroy(DestroyMode.Vanish);
                }
            }
            invisDoors.Clear();
            invisDoors = null;
            base.DeSpawn();
        }
        
        [ReloadMethod]
        public override void Draw()
        {
            float num = Mathf.Clamp01((float)this.visualTicksOpen / (float)this.VisualTicksToOpen);
            float num2 = this.def.Size.x;
            float d = (0f + 0.45f * num) * num2;
            for (int i = 0; i < 2; i++)
            {
                bool flipped = (i != 0) ? true : false;
                Mesh mesh;
                Matrix4x4 matrix;
                if (Def.doorSwing)
                    DrawSwingParams(Def, this.DrawPos, this.Rotation, out mesh, out matrix, d, flipped);
                else
                    DrawParams(Def, this.DrawPos, this.Rotation, out mesh, out matrix, d, flipped);

                Material matToDraw = (!flipped && Def.doorAsync is GraphicData dA) ? dA.GraphicColoredFor(this).MatAt(base.Rotation) : this.Graphic.MatAt(base.Rotation);
                Graphics.DrawMesh(mesh, matrix, matToDraw, 0);
                
            }
            if (Def.doorFrame is GraphicData f)
            {
                Mesh fMesh;
                Matrix4x4 fMatrix;
                DrawFrameParams(Def, this.DrawPos, this.Rotation, out fMesh, out fMatrix);
                Graphics.DrawMesh(MeshPool.plane10, fMatrix, Def.doorFrame.GraphicColoredFor(this).MatAt(base.Rotation), 0);
            }
            base.Comps_PostDraw();
        }

        [ReloadMethod]
        public static void DrawFrameParams(DoorExpandedDef thingDef, Vector3 drawPos, Rot4 rotation, out Mesh mesh, out Matrix4x4 matrix)
        {
            float d = (0f + 0.45f * 1) * thingDef.Size.x;
            bool verticalRotation = rotation.IsHorizontal;
            Vector3 rotationVector = default(Vector3);
            rotationVector = new Vector3(-1f, 0f, 0f);
            mesh = MeshPool.plane10;

            Quaternion rotQuat = rotation.AsQuat;
            rotationVector = rotQuat * rotationVector;

            Vector3 graphicVector = drawPos;
            graphicVector.y = Altitudes.AltitudeFor(AltitudeLayer.MoteOverhead);
            if (!verticalRotation) graphicVector.x += d;
            if (rotation == Rot4.East) graphicVector.z -= d;
            if (rotation == Rot4.West) graphicVector.z += d;
            graphicVector += rotationVector * d;

            float persMod = (thingDef.fixedPerspective) ? 2f : 1f;
            Vector3 scaleVector = (verticalRotation) ?
                new Vector3(thingDef.doorFrame.drawSize.x * persMod, 1f, thingDef.doorFrame.drawSize.y * persMod) :
                new Vector3(thingDef.doorFrame.drawSize.x, 1f, thingDef.doorFrame.drawSize.y);

            matrix = default(Matrix4x4);
            matrix.SetTRS(graphicVector, rotQuat, scaleVector);
        }

        [ReloadMethod]
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

        [ReloadMethod]
        public static void DrawSwingParams(DoorExpandedDef thingDef, Vector3 drawPos, Rot4 rotation, out Mesh mesh, out Matrix4x4 matrix, float mod = 1, bool flipped = false)
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
                return !this.DoorPowerOn || this.TicksToOpenNow > 20;
            }
        }


        // RimWorld.Building_Door
        public virtual bool PawnCanOpen(Pawn p)
        {
            Lord lord = p.GetLord();
            return (lord != null && lord.LordJob != null && lord.LordJob.CanOpenAnyDoor(p)) || base.Faction == null || GenAI.MachinesLike(base.Faction, p);
        }


        // RimWorld.Building_Door
        public void Notify_PawnApproaching(Pawn p)
        {
            if (this.PawnCanOpen(p))
            {
                //base.Map.fogGrid.Notify_PawnEnteringDoor(this, p);
                if (!this.SlowsPawns)
                {
                    this.DoorOpen(300);
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
                    this.def.building.soundDoorOpenPowered.PlayOneShot(new TargetInfo(base.Position, base.Map, false));
                }
                else
                {
                    this.def.building.soundDoorOpenManual.PlayOneShot(new TargetInfo(base.Position, base.Map, false));
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
                return this.openInt && (this.holdOpenInt || !this.WillCloseSoon);
            }
        }


        private bool FriendlyTouchedRecently
        {
            get
            {
                return Find.TickManager.TicksGame < this.lastFriendlyTouchTick + 200;
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




        public void FriendlyTouched()
        {
            this.lastFriendlyTouchTick = Find.TickManager.TicksGame;
        }

        private void ClearReachabilityCache(Map map)
        {
            map.reachability.ClearCache();
            this.freePassageWhenClearedReachabilityCache = this.FreePassage;
        }

        public override void Tick()
        {
            base.Tick();
            if (this.FreePassage != this.freePassageWhenClearedReachabilityCache)
            {
                this.ClearReachabilityCache(base.Map);
            }
            if (!this.openInt)
            {
                if (this.visualTicksOpen > 0)
                {
                    this.visualTicksOpen--;
                }
                if ((Find.TickManager.TicksGame + this.thingIDNumber.HashOffset()) % 375 == 0)
                {
                    GenTemperature.EqualizeTemperaturesThroughBuilding(this, 1f, false);
                }
            }
            else if (this.openInt)
            {
                if (this.visualTicksOpen < this.VisualTicksToOpen)
                {
                    this.visualTicksOpen++;
                }
                if (!this.holdOpenInt)
                {
                    if (base.Map.thingGrid.CellContains(base.Position, ThingCategory.Pawn))
                    {
                        this.ticksUntilClose = 60;
                        
                    }
                    else
                    {
                        this.ticksUntilClose--;
                        if (this.ticksUntilClose <= 0 && this.CanCloseAutomatically)
                        {
                            this.DoorTryClose();
                            foreach (Building_DoorRegionHandler door in invisDoors)
                            {
                                AccessTools.Method(typeof(Building_Door), "DoorTryClose").Invoke(door, null);
                            }
                        }
                    }
                }
                if ((Find.TickManager.TicksGame + this.thingIDNumber.HashOffset()) % 22 == 0)
                {
                    GenTemperature.EqualizeTemperaturesThroughBuilding(this, 1f, false);
                }
            }
        }


        public bool BlockedOpenMomentary
        {
            get
            {
                List<Thing> thingList = base.Position.GetThingList(base.Map);
                for (int i = 0; i < thingList.Count; i++)
                {
                    Thing thing = thingList[i];
                    if (thing.def.category == ThingCategory.Item || thing.def.category == ThingCategory.Pawn)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public void DoorTryClose()
        {
            if (this.holdOpenInt || this.BlockedOpenMomentary)
            {
                return;
            }
            foreach (Building_DoorRegionHandler handler in invisDoors)
            {
                if (handler.Open) AccessTools.Field(typeof(Building_Door), "openInt").SetValue(handler, false); //AccessTools.Method(typeof(Building_Door), "DoorTryClose").Invoke(handler, null);
            }
            this.openInt = false;
            if (this.DoorPowerOn)
            {
                this.def.building.soundDoorClosePowered.PlayOneShot(new TargetInfo(base.Position, base.Map, false));
            }
            else
            {
                this.def.building.soundDoorCloseManual.PlayOneShot(new TargetInfo(base.Position, base.Map, false));
            }
        }

        // RimWorld.Building_Door
        public void StartManualOpenBy(Pawn opener)
        {
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
                yield return new Command_Toggle
                {
                    defaultLabel = "openInt".Translate(),
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


        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look<Building_DoorRegionHandler>(ref this.invisDoors, "invisDoors", LookMode.Reference);
            Scribe_Values.Look<bool>(ref this.openInt, "open", false, false);
            Scribe_Values.Look<bool>(ref this.holdOpenInt, "holdOpen", false, false);
            Scribe_Values.Look<int>(ref this.lastFriendlyTouchTick, "lastFriendlyTouchTick", 0, false);
            if (Scribe.mode == LoadSaveMode.LoadingVars && this.openInt)
            {
                this.visualTicksOpen = this.VisualTicksToOpen;
            }
        }
        #endregion Building_Door Copy
        
    }
}
