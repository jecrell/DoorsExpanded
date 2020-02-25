using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace DoorsExpanded
{
    public class Building_DoorRemoteButton : Building
    {
        private List<Building_DoorRemote> linkedDoors = new List<Building_DoorRemote>();
        private CompPowerTrader powerComp;
        private bool buttonOn = false;
        private bool needsToBeSwitched = false;

        public List<Building_DoorRemote> LinkedDoors => linkedDoors;

        public bool ButtonOn
        {
            get => buttonOn;
            set => buttonOn = value;
        }

        public bool NeedsToBeSwitched
        {
            get => needsToBeSwitched;
            set => needsToBeSwitched = value;
        }

        // base.Graphic is off; fullGraveGraphicData.Graphic is on.
        public override Graphic Graphic => !ButtonOn ? base.Graphic : def.building.fullGraveGraphicData.Graphic;

        public bool IsEnabled => powerComp == null || powerComp.PowerOn;

        private bool NeedsPower => powerComp != null && !powerComp.PowerOn;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            powerComp = GetComp<CompPowerTrader>();
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            if (linkedDoors == null)
                return;
            for (var i = 0; i < linkedDoors.Count; i++)
            {
                GenDraw.DrawLineBetween(this.TrueCenter(), linkedDoors[i].TrueCenter());
            }
        }

        public void Notify_Linked(Building_DoorRemote remoteDoor)
        {
            // ButtonOn state is set to the first linked door's HoldOpen state.
            // From that point on, the relationship is reversed such that all linked door's HoldOpen state are synchronized
            // with this button's ButtonOn state.
            if (linkedDoors.Count == 0)
                ButtonOn = remoteDoor.HoldOpen;
            linkedDoors.Add(remoteDoor);
        }

        public void Notify_Unlinked(Building_DoorRemote remoteDoor)
        {
            linkedDoors.Remove(remoteDoor);
            if (linkedDoors.Count == 0)
                ButtonOn = false;
        }

        public void PushButton()
        {
            if (NeedsToBeSwitched)
                NeedsToBeSwitched = false;
            SoundDefOf.Tick_Tiny.PlayOneShot(this);
            ButtonOn = !ButtonOn;
            if (linkedDoors != null)
            {
                linkedDoors.RemoveAll(door => !door.Spawned);
                foreach (var linkedDoor in linkedDoors)
                    linkedDoor.Notify_ButtonPushed();
            }
        }

        private void GivePushJob(Pawn pawn)
        {
            var job = new Job(DefDatabase<JobDef>.GetNamed("PH_PushButton"), this);
            pawn.jobs.TryTakeOrderedJob(job);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var gizmo in base.GetGizmos())
                yield return gizmo;

            yield return new Command_Toggle
            {
                defaultLabel = "PH_UseButtonOrLever".Translate(),
                defaultDesc = "PH_UseButtonOrLeverDesc".Translate(),
                icon = TexButton.UseButtonOrLever,
                disabled = linkedDoors == null || linkedDoors.Count == 0 || NeedsPower,
                disabledReason = GetDisabledReason(),
                isActive = () => NeedsToBeSwitched,
                toggleAction = () => NeedsToBeSwitched = !NeedsToBeSwitched,
            };
        }

        private string GetDisabledReason()
        {
            if (linkedDoors == null || linkedDoors?.Count() == 0)
            {
                return "PH_NeedsLinkedDoorsFirst".Translate();
            }
            if (NeedsPower)
            {
                return "PH_PowerSourceRequired".Translate();
            }
            return "";
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            foreach (var floatMenuOpton in base.GetFloatMenuOptions(selPawn))
                yield return floatMenuOpton;

            if (IsEnabled)
            {
                if (linkedDoors != null && linkedDoors.Count > 0)
                {
                    if (selPawn.health != null && selPawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                    {
                        yield return new FloatMenuOption
                        (
                            label: "PH_ButtonPress".Translate(def.label),
                            action: () => GivePushJob(selPawn)
                        );
                    }
                    else
                    {
                        var disabled0 = new FloatMenuOption
                        (
                            label: "PH_ButtonPressManipulationFailure".Translate(selPawn.Label),
                            action: null
                        );
                        disabled0.Disabled = true;
                        yield return disabled0;

                    }
                }
                else
                {
                    var disabled1 = new FloatMenuOption
                    (
                        label: "PH_ButtonPressNoConnection".Translate(),
                        action: null
                    );
                    disabled1.Disabled = true;
                    yield return disabled1;

                }
            }
            else
            {
                var disabled2 = new FloatMenuOption
                (
                    label: "PH_ButtonPressNoPower".Translate(),
                    action: null
                );
                disabled2.Disabled = true;
                yield return disabled2;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref linkedDoors, nameof(linkedDoors), LookMode.Reference);
            Scribe_Values.Look(ref needsToBeSwitched, nameof(needsToBeSwitched), false);
            Scribe_Values.Look(ref buttonOn, nameof(buttonOn), false);
        }
    }
}
