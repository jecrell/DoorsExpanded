using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        // All of these constants are currently the same as those in Building_Door.
        private const float OpenTicks = 45f;
        private const int CloseDelayTicks = 110;
        private const int WillCloseSoonThreshold = 111;
        private const int ApproachCloseDelayTicks = 300;
        private const int MaxTicksSinceFriendlyTouchToAutoClose = 120;
        private const float PowerOffDoorOpenSpeedFactor = 0.25f;
        private const float VisualDoorOffsetStart = 0f;
        internal const float VisualDoorOffsetEnd = 0.45f;

        private List<Building_DoorRegionHandler> invisDoors = new List<Building_DoorRegionHandler>();
        private CompProperties_DoorExpanded props;
        private CompPowerTrader powerComp;
        private CompForbiddable forbiddenComp;
        private bool openInt;
        private bool holdOpenInt;
        private int lastFriendlyTouchTick = -9999;
        protected int ticksUntilClose;
        protected int ticksSinceOpen;
        private bool freePassageWhenClearedReachabilityCache;
        private bool lastForbiddenState;
        private bool preventDoorOpenRecursion;
        private bool preventDoorTryCloseRecursion;

        [Obsolete("Use Props instead")]
        public DoorExpandedDef Def => def as DoorExpandedDef;

        public CompProperties_DoorExpanded Props =>
            props ??= def.GetDoorExpandedProps() ?? throw new Exception("Missing " + typeof(CompProperties_DoorExpanded));

        public List<Building_DoorRegionHandler> InvisDoors => invisDoors;

        public bool Open => props.doorType == DoorType.FreePassage || openInt;

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

        public int TicksUntilClose => ticksUntilClose;

        public int TicksSinceOpen => ticksSinceOpen;

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

        public int TicksToOpenNow => DoorOpenTicks(StatRequest.For(this), DoorPowerOn);

        internal bool CanTryCloseAutomatically => FriendlyTouchedRecently && !HoldOpen;

        internal protected virtual bool FriendlyTouchedRecently =>
            Find.TickManager.TicksGame < lastFriendlyTouchTick + MaxTicksSinceFriendlyTouchToAutoClose;

        public override bool FireBulwark => !Open && base.FireBulwark;

        public virtual bool Forbidden => forbiddenComp?.Forbidden ?? false;

        // This method works for both Building_Door and Building_DoorExpanded.
        private static int DoorOpenTicks(StatRequest statRequest, bool doorPowerOn, bool applyPostProcess = true)
        {
            if (statRequest.Def.GetDoorExpandedProps() is CompProperties_DoorExpanded props && props.doorType == DoorType.FreePassage)
            {
                return 0;
            }
            var ticksToOpenNow = OpenTicks / StatDefOf.DoorOpenSpeed.Worker.GetValue(statRequest, applyPostProcess);
            if (doorPowerOn)
            {
                ticksToOpenNow *= PowerOffDoorOpenSpeedFactor;
            }
            return Mathf.RoundToInt(ticksToOpenNow);
        }

        public static float DoorOpenTime(StatRequest statRequest, bool doorPowerOn, bool applyPostProcess)
        {
            return GenTicks.TicksToSeconds(DoorOpenTicks(statRequest, doorPowerOn, applyPostProcess));
        }

        // This method works for both Building_Door and Building_DoorExpanded.
        public static string DoorOpenTimeExplanation(StatRequest statRequest, bool doorPowerOn, StatDef stat)
        {
            var explanation = new StringBuilder();

            // Treat powered door open speed as the "normal" speed.
            // This is done partly due to the lack of a vanilla "has power" translation key.
            var defaultSpeed = OpenTicks * PowerOffDoorOpenSpeedFactor;
            explanation.AppendLine("StatsReport_BaseValue".Translate() + ": " + stat.ValueToString(defaultSpeed / GenTicks.TicksPerRealSecond));

            if (statRequest.Def.GetDoorExpandedProps() is CompProperties_DoorExpanded props && props.doorType == DoorType.FreePassage)
            {
                explanation.AppendLine($"{DoorType.FreePassage}: x0");
                return explanation.ToString();
            }

            var doorOpenSpeedStat = StatDefOf.DoorOpenSpeed;
            var doorOpenSpeed = doorOpenSpeedStat.Worker.GetValue(statRequest);
            explanation.AppendLine($"{doorOpenSpeedStat.LabelCap}:");
            var doorOpenSpeedExplanation = doorOpenSpeedStat.Worker.GetExplanationFull(statRequest, doorOpenSpeedStat.toStringNumberSense, doorOpenSpeed);
            explanation.AppendLine("    " + string.Join("\n    ",
                doorOpenSpeedExplanation.Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries)));
            explanation.AppendLine($"    1/x => x{(1f / doorOpenSpeed).ToStringPercent()}");

            if (!doorPowerOn)
            {
                explanation.AppendLine($"{"NoPower".Translate()}: x{(1f / PowerOffDoorOpenSpeedFactor).ToStringPercent()}");
            }

            return explanation.ToString();
        }

        // This method works for both Building_Door and Building_DoorExpanded.
        public static bool DoorNeedsPower(ThingDef def) => def.HasComp(typeof(CompPowerTrader));

        // This method works for both Building_Door and Building_DoorExpanded.
        public static bool? DoorIsPoweredOn(Thing thing)
        {
            if (thing is Building_Door door)
                return door.DoorPowerOn;
            else if (thing is Building_DoorExpanded doorEx)
                return doorEx.DoorPowerOn;
            return null;
        }

        public override void PostMake()
        {
            base.PostMake();
            _ = Props; // ensures props is initialized
            powerComp = GetComp<CompPowerTrader>();
            forbiddenComp = GetComp<CompForbiddable>();
        }

        public override void PostMapInit()
        {
            if (Spawned)
                SpawnInvisDoorsAsNeeded(Map, this.OccupiedRect());
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            TLog.Log(this);
            // Non-1x1 rotations change the footprint of the blueprint, so this needs to be done before that footprint is
            // cached in various ways in base.SpawnSetup.
            // Fortunately once rotated, no further non-1x1 rotations will change the footprint further.
            // Restricted rotation logic in both patched Designator_Place and Blueprint shouldn't allow invalid rotations by
            // this point, but better safe than sorry, especially if this is spawned without Designator_Place or Blueprint.
            Rotation = DoorRotationAt(def, props, Position, Rotation, map);

            // Note: We only want invisDoors to register in the edificeGrid (during its SpawnSetup).
            // A harmony patch to EdificeGrid.Register, which is called in Building.SpawnSetup,
            // prevents this Building_DoorExpanded from being registered in the edificeGrid.
            base.SpawnSetup(map, respawningAfterLoad);

            // During game loading/initialization, we can't tell whether another door (including invis door) is actually spawned
            // (and thus listed in GridsUtility.GetThingList()), since things are initially set to unspawned state and then spawned
            // as the game is being initialized, so another door may have SpawnSetup called later than this.
            // Therefore, we delay spawning (and the involved sanity checks) of invis doors until game is initialized.
            if (!respawningAfterLoad)
            {
                SpawnInvisDoorsAsNeeded(map, this.OccupiedRect());
            }

            powerComp = GetComp<CompPowerTrader>();
            forbiddenComp = GetComp<CompForbiddable>();
            SetForbidden(Forbidden);
            ClearReachabilityCache(map);
            if (BlockedOpenMomentary)
            {
                DoorOpen();
            }
        }

        // Note: This method is also filled with sanity checks for invis doors that manifest as warnings.
        private void SpawnInvisDoorsAsNeeded(Map map, CellRect occupiedRect)
        {
            if (TLog.Enabled)
                TLog.Log(this, $"{this} (#invisDoors={invisDoors.Count}/{occupiedRect.Area})");
            var invisDoorsToRespawn = new List<Building_DoorRegionHandler>();
            var invisDoorsToReposition = new List<Building_DoorRegionHandler>();
            var spawnedInvisDoors = new List<Building_DoorRegionHandler>();
            var errors = new List<string>();

            if (invisDoors.Count > 0)
            {
                var invisDoorCells = new HashSet<IntVec3>(); // only for detecting multiple existing invis doors at same cell
                for (var i = invisDoors.Count - 1; i >= 0; i--)
                {
                    var invisDoor = invisDoors[i];
                    var removeInvisDoor = false;
                    if (invisDoor == null)
                    {
                        errors.Add($"{this}.invisDoors[{i}] is unexpectedly null - removing it");
                        removeInvisDoor = true;
                    }
                    else if (invisDoor.Destroyed)
                    {
                        errors.Add($"{invisDoor} is unexpectedly destroyed - removing it");
                        removeInvisDoor = true;
                    }
                    else
                    {
                        if (!invisDoor.Spawned)
                        {
                            // Error msg is added later in this case.
                            removeInvisDoor = true;
                            invisDoorsToRespawn.Add(invisDoor);
                        }
                        else
                        {
                            var cell = invisDoor.Position;
                            if (!occupiedRect.Contains(cell))
                            {
                                errors.Add($"{invisDoor} has position {cell} outside of {occupiedRect} - destroying it");
                                removeInvisDoor = true;
                                invisDoor.Destroy();
                            }
                            else if (!invisDoorCells.Add(cell))
                            {
                                var existingInvisDoors = cell.GetThingList(map).OfType<Building_DoorRegionHandler>();
                                errors.Add($"{invisDoor} has position {cell} taken by multiple invis doors " +
                                    $"({existingInvisDoors.ToStringSafeEnumerable()}) - destroying it");
                                removeInvisDoor = true;
                                invisDoor.Destroy();
                            }
                        }
                    }

                    if (removeInvisDoor)
                    {
                        invisDoors.RemoveAt(i);
                    }
                    else
                    {
                        if (invisDoor.ParentDoor == null)
                        {
                            errors.Add($"{invisDoor} has no parent - reparenting it to {this}");
                            invisDoor.ParentDoor = this;
                        }
                        else if (invisDoor.ParentDoor != this)
                        {
                            errors.Add($"{invisDoor} has different parent - removing it from {this}");
                            // Don't destroy this invis door here - let the invis door's parent handle it.
                            invisDoors.RemoveAt(i);
                        }

                        if (invisDoor.Faction != Faction)
                        {
                            errors.Add($"{invisDoor} has different faction ({invisDoor.Faction}) - setting it to {Faction}");
                            invisDoor.SetFactionDirect(Faction);
                        }
                    }
                }
            }

            foreach (var cell in occupiedRect)
            {
                var thingList = cell.GetThingList(map);
                // Another spawned overlapping doorEx shouldn't ever by possible due to GenSpawn.SpawningWipes checks, but just in case...
                foreach (var existingThing in thingList)
                {
                    if (existingThing != this && existingThing is Building_DoorExpanded existingDoorEx)
                    {
                        var existingOccupiedRect = existingDoorEx.OccupiedRect();
                        if (occupiedRect.Overlaps(existingOccupiedRect))
                        {
                            errors.Add($"Unexpected {existingDoorEx} (occupying {existingOccupiedRect}) overlaps {this} " +
                                $"(occupying {occupiedRect}) - refunding it");
                            // This should also destroy all the invis doors associated with the refunded doorEx.
                            GenSpawn.Refund(existingDoorEx, map, occupiedRect);
                        }
                    }
                }

                Building_DoorRegionHandler invisDoor = null;
                var existingInvisDoors = thingList.OfType<Building_DoorRegionHandler>();
                foreach (var existingInvisDoor in existingInvisDoors)
                {
                    if (existingInvisDoor.ParentDoor != this)
                    {
                        errors.Add($"Unexpected {invisDoor} already spawned at {cell} - destroying it");
                        // By this point, it's impossible for another spawned doorEx to overlap this doorEx,
                        // so if existing invis door's parent is another doorEx, assume it's either unspawned or in another location,
                        // so don't destroy that doorEx. Just destroy
                        invisDoor.Destroy();
                    }
                    else if (invisDoor != null)
                    {
                        // This isn't redundant with the earlier multiple invis doors check, since it's technically possible for
                        // some existing invis doors at the cell to not all be within invisDoors, thus warranting this sanity check.
                        errors.Add($"{invisDoor} has position {cell} taken by multiple invis doors " +
                            $"({existingInvisDoors.ToStringSafeEnumerable()}) - destroying it");
                        invisDoor.Destroy();
                    }
                    else
                    {
                        invisDoor = existingInvisDoor;
                    }
                }

                if (invisDoor == null)
                {
                    if (invisDoorsToRespawn.Count > 0)
                    {
                        invisDoor = invisDoorsToRespawn.Pop();
                        errors.Add($"{invisDoor} is unexpectedly unspawned - respawning it at {cell}");
                    }
                    else
                    {
                        invisDoor = (Building_DoorRegionHandler)ThingMaker.MakeThing(HeronDefOf.HeronInvisibleDoor);
                        invisDoor.ParentDoor = this;
                        invisDoor.SetFactionDirect(Faction);
                    }
                    GenSpawn.Spawn(invisDoor, cell, map);
                    spawnedInvisDoors.Add(invisDoor);
                }

                // Open is the only field that needs to be manually synced with the parent door;
                // all other fields in the invis door are unused.
                invisDoor.Open = Open;
            }

            foreach (var invisDoor in invisDoorsToRespawn)
            {
                errors.Add($"{invisDoor} is unexpectedly unspawned and is extraneous - destroying it");
                invisDoor.Destroy();
            }

            invisDoors.AddRange(spawnedInvisDoors);

            if (errors.Count > 0)
            {
                var errorMsg = $"[Doors Expanded] Encountered errors when spawning invis doors for {this}:\n" + errors.ToLineList("\t");
                if (spawnedInvisDoors.Count > 0)
                    errorMsg += "\nSpawned invis doors:\n" + spawnedInvisDoors.Select(invisDoor => invisDoor.ToString()).ToLineList("\t");
                Log.Error(errorMsg);
            }
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            TLog.Log(this);
            var spawnedInvisDoors = invisDoors.Where(invisDoor => invisDoor.Spawned).ToArray();
            foreach (var invisDoor in spawnedInvisDoors)
            {
                // If this parent door is respawned later, it will always recreate the invis doors,
                // so destroy (rather than just despawn) existing invis doors.
                // And invis doors are always despawned/destroyed in the default Vanish mode.
                invisDoor.Destroy();
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

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            TLog.Log(this);
            base.Destroy(mode);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref invisDoors, nameof(invisDoors), LookMode.Reference);
            invisDoors ??= new List<Building_DoorRegionHandler>(); // in case a save file somehow has missing or null invisDoors
            Scribe_Values.Look(ref openInt, "open", false);
            Scribe_Values.Look(ref holdOpenInt, "holdOpen", false);
            Scribe_Values.Look(ref lastFriendlyTouchTick, nameof(lastFriendlyTouchTick), 0);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // Ensure props is available for Open usage since PostMake hasn't been called yet.
                // base.ExposeData() ensures that def is available, which is required for props initialization.
                _ = Props;
                if (Open)
                {
                    ticksSinceOpen = TicksToOpenNow;
                }
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
            // Workaround for MinifyEverything issue where reinstalling doors sometimes causes a transient and harmless NRE
            // in GridsUtility.GetThingList. This also effects vanilla doors, which are fixed in a harmony patch
            // (see HarmonyPatches.BuildingDoorTickPrefix).
            if (!Spawned)
                return;

            base.Tick();

            var occupiedRect = this.OccupiedRect();

            // Periodic sanity checks.
            if (invisDoors.Where(invisDoor => invisDoor != null && invisDoor.Spawned).Count() != occupiedRect.Area ||
                this.IsHashIntervalTick(GenTicks.TickLongInterval))
            {
                SpawnInvisDoorsAsNeeded(Map, occupiedRect);
            }

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
                if ((Find.TickManager.TicksGame + thingIDNumber.HashOffset()) % TemperatureTuning.Door_TempEqualizeIntervalClosed == 0)
                {
                    GenTemperature.EqualizeTemperaturesThroughBuilding(this, props.tempEqualizeRate, twoWay: false);
                }
            }
            else
            {
                if (ticksSinceOpen < TicksToOpenNow)
                {
                    ticksSinceOpen++;
                }
                var isPawnPresent = false;
                foreach (var c in occupiedRect)
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
            // For compatibility with other mods that patch Building_Door.DoorOpen,
            // which is only internally called within Building_Door, need to call DoorOpen on each invis door.
            // However doing so would end up calling this DoorOpen recursively.
            // preventDoorOpenRecursion is used to prevent this unwanted recursion.
            if (preventDoorOpenRecursion)
                return;
            preventDoorOpenRecursion = true;
            try
            {
                foreach (var invisDoor in invisDoors)
                    invisDoor.DoorOpen(ticksToClose);
            }
            finally
            {
                preventDoorOpenRecursion = false;
            }

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
            // For compatibility with other mods that patch Building_Door.DoorTryClose,
            // which is only internally called within Building_Door, need to call DoorTryClose on each invis door.
            // However doing so would end up calling this DoorTryClose recursively.
            // preventDoorTryCloseRecursion is used to prevent this unwanted recursion.
            if (preventDoorTryCloseRecursion)
                return false;
            preventDoorTryCloseRecursion = true;
            try
            {
                foreach (var invisDoor in invisDoors)
                    invisDoor.DoorTryClose();
            }
            finally
            {
                preventDoorTryCloseRecursion = false;
            }

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

            var rotation = DoorRotationAt(def, props, Position, Rotation, Map);
            Rotation = rotation;

            var drawPos = DrawPos;
            drawPos.y = AltitudeLayer.DoorMoveable.AltitudeFor();

            for (var i = 0; i < 2; i++)
            {
                var flipped = i != 0;
                var material = (!flipped && props.doorAsync is GraphicData doorAsyncGraphic)
                    ? doorAsyncGraphic.GraphicColoredFor(this).MatAt(rotation)
                    : Graphic.MatAt(rotation);
                Draw(def, props, material, drawPos, rotation, percentOpen, flipped,
                    i == 0 && MoreDebugViewSettings.writeDoors ? debugDrawVectors : null);
                if (props.singleDoor)
                    break;
            }

            if (props.doorFrame is GraphicData)
            {
                DrawFrameParams(def, props, drawPos, rotation, false, out var fMesh, out var fMatrix);
                Graphics.DrawMesh(fMesh, fMatrix, props.doorFrame.GraphicColoredFor(this).MatAt(rotation), 0);

                if (props.doorFrameSplit is GraphicData)
                {
                    DrawFrameParams(def, props, drawPos, rotation, true, out fMesh, out fMatrix);
                    Graphics.DrawMesh(fMesh, fMatrix, props.doorFrameSplit.GraphicColoredFor(this).MatAt(rotation), 0);
                }
            }

            Comps_PostDraw();
        }

        internal static void Draw(ThingDef def, CompProperties_DoorExpanded props,
            Material material, Vector3 drawPos, Rot4 rotation, float percentOpen,
            bool flipped, DebugDrawVectors drawVectors = null)
        {
            Mesh mesh;
            Quaternion rotQuat;
            Vector3 offsetVector, scaleVector;
            switch (props.doorType)
            {
                // There's no difference between Stretch and StretchVertical except for stretchOpenSize's default value.
                case DoorType.Stretch:
                case DoorType.StretchVertical:
                    DrawStretchParams(def, props, rotation, percentOpen, flipped,
                        out mesh, out rotQuat, out offsetVector, out scaleVector);
                    break;
                case DoorType.DoubleSwing:
                    // TODO: Should drawPos.y be set to Mathf.Max(drawPos.y, AltitudeLayer.BuildingOnTop.AltitudeFor())
                    // since AltitudeLayer.DoorMoveable is only used to hide sliding doors behind adjacent walls?
                    DrawDoubleSwingParams(def, props, drawPos, rotation, percentOpen, flipped,
                        out mesh, out rotQuat, out offsetVector, out scaleVector);
                    break;
                default:
                    DrawStandardParams(def, props, rotation, percentOpen, flipped,
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

        private static void DrawStretchParams(ThingDef def, CompProperties_DoorExpanded props,
            Rot4 rotation, float percentOpen, bool flipped, out Mesh mesh, out Quaternion rotQuat,
            out Vector3 offsetVector, out Vector3 scaleVector)
        {
            var drawSize = def.graphicData.drawSize;
            var closeSize = props.stretchCloseSize;
            var openSize = props.stretchOpenSize;
            var offset = props.stretchOffset.Value;

            var verticalRotation = rotation.IsHorizontal;
            var persMod = verticalRotation && props.fixedPerspective ? 2f : 1f;

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

        private static void DrawDoubleSwingParams(ThingDef def, CompProperties_DoorExpanded props,
            Vector3 drawPos, Rot4 rotation, float percentOpen, bool flipped, out Mesh mesh, out Quaternion rotQuat,
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

            var offsetMod = (VisualDoorOffsetStart + props.doorOpenMultiplier * percentOpen) * def.Size.x;
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
            var persMod = verticalRotation && props.fixedPerspective ? 2f : 1f;
            scaleVector = new Vector3(drawSize.x * persMod, 1f, drawSize.y * persMod);
        }

        private static void DrawStandardParams(ThingDef def, CompProperties_DoorExpanded props,
            Rot4 rotation, float percentOpen, bool flipped, out Mesh mesh, out Quaternion rotQuat,
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

            var offsetMod = (VisualDoorOffsetStart + props.doorOpenMultiplier * percentOpen) * def.Size.x;
            offsetVector *= offsetMod;

            var drawSize = def.graphicData.drawSize;
            var persMod = verticalRotation && props.fixedPerspective ? 2f : 1f;
            scaleVector = new Vector3(drawSize.x * persMod, 1f, drawSize.y * persMod);
        }

        private static void DrawFrameParams(ThingDef def, CompProperties_DoorExpanded props,
            Vector3 drawPos, Rot4 rotation, bool split,
            out Mesh mesh, out Matrix4x4 matrix)
        {
            var verticalRotation = rotation.IsHorizontal;
            var offsetVector = new Vector3(-1f, 0f, 0f);
            mesh = MeshPool.plane10;

            if (props.doorFrameSplit != null)
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

            var offsetMod = (VisualDoorOffsetStart + props.doorOpenMultiplier * 1f) * def.Size.x;
            offsetVector *= offsetMod;

            var drawSize = props.doorFrame.drawSize;
            var persMod = verticalRotation && props.fixedPerspective ? 2f : 1f;
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
                    graphicVector.y = AltitudeLayer.BuildingOnTop.AltitudeFor();
            }
            else if (rotation == Rot4.West)
            {
                graphicVector.z += offsetMod;
                if (split)
                    graphicVector.y = AltitudeLayer.BuildingOnTop.AltitudeFor();
            }
            graphicVector += offsetVector;

            var frameOffsetVector = props.doorFrameOffset;
            if (props.doorFrameSplit != null)
            {
                if (rotation == Rot4.West)
                {
                    rotQuat = Quaternion.Euler(0f, 270f, 0f);
                    graphicVector.z -= 2.7f;
                    mesh = MeshPool.plane10Flip;
                    frameOffsetVector = props.doorFrameSplitOffset;
                }
            }
            graphicVector += frameOffsetVector;

            matrix = Matrix4x4.TRS(graphicVector, rotQuat, scaleVector);
        }

        public static Rot4 DoorRotationAt(ThingDef def, CompProperties_DoorExpanded props, IntVec3 loc, Rot4 rot, Map map)
        {
            if (!def.rotatable)
            {
                var size = def.Size;
                if ((size.x == 1 && size.z == 1) || props.doorType == DoorType.StretchVertical || props.doorType == DoorType.Stretch)
                    rot = Building_Door.DoorRotationAt(loc, map);
            }
            if (!props.rotatesSouth && rot == Rot4.South)
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
