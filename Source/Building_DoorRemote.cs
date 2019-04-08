using RimWorld;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace DoorsExpanded
{
    public enum DoorRemote_State
    {
        Free = 0,
        ForcedOpen = 1,
        ForcedClose = 2
    }

    public class Building_DoorRemote : Building_DoorExpanded
    {
        private Building_DoorRemoteButton button = null;
        private DoorRemote_State remoteState = DoorRemote_State.Free;
        private bool securedRemotely = true;
        
        public bool SecuredRemotely
        {
            get => securedRemotely;
            set
            {
                if (value == false && remoteState == DoorRemote_State.ForcedClose)
                    remoteState = DoorRemote_State.Free;
                securedRemotely = value;
            }
        }

        public override bool WillCloseSoon => remoteState != DoorRemote_State.ForcedOpen && base.WillCloseSoon;

        public override bool PawnCanOpen(Pawn p)
        {
            return remoteState != DoorRemote_State.ForcedClose && base.PawnCanOpen(p);
        }

        public override bool ShouldKeepDoorOpen()
        {
            return remoteState == DoorRemote_State.ForcedOpen || base.ShouldKeepDoorOpen();
        }

        public override void DrawExtraSelectionOverlays()
        {
            if (button != null)
                GenDraw.DrawLineBetween(this.TrueCenter(), button.TrueCenter());
            base.DrawExtraSelectionOverlays();
        }

        

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
                yield return g;

            yield return new Command_Action()
            {
                defaultLabel = "PH_ButtonConnect".Translate(),
                defaultDesc = "PH_ButtonConnectDesc".Translate(),
                icon = TexButton.ConnectToButton,
                disabled = false,
                disabledReason = "",
                action = ConnectToButton
            };
            
            yield return new Command_Toggle()
            {
                defaultLabel = "PH_RemoteDoorSecuredRemotely".Translate(),
                defaultDesc = "PH_RemoteDoorSecuredRemotelyDesc".Translate(),
                icon = TexButton.SecuredRemotely,
                disabled = false,
                disabledReason = "",
                toggleAction = (()=> SecuredRemotely = !SecuredRemotely), 
                isActive = (()=> SecuredRemotely)
            };
        }

        public void Notify_ButtonPushed()
        {
            if (this.Open)
            {
                this.DoorTryClose();
                if (!securedRemotely)
                    remoteState = DoorRemote_State.Free;
                else
                    remoteState = DoorRemote_State.ForcedClose;
            }
            else
            {
                this.DoorOpen(int.MaxValue);
                remoteState = DoorRemote_State.ForcedOpen;
            }
        }

        private void ConnectToButton()
        {
            TargetingParameters tp = new TargetingParameters();
            Predicate<TargetInfo> validator = delegate (TargetInfo t) { return t.Thing is Building_DoorRemoteButton bt; };
            tp.validator = validator;
            tp.canTargetBuildings = true;
            tp.canTargetPawns = false;
            Find.Targeter.BeginTargeting(tp, delegate (LocalTargetInfo t)
            {
                if (t.Thing is Building_DoorRemoteButton newButton)
                {
                    if (button != null)
                    {
                        if (button.Spawned)
                            Messages.Message(
                                "PH_ButtonUnlinked".Translate(button.PositionHeld.ToString()), button, MessageTypeDefOf.SilentInput);
                        else
                            Messages.Message(
                                "PH_ButtonUnlinkedUnspawned".Translate(button.PositionHeld.ToString()), MessageTypeDefOf.SilentInput);
                    }
                    newButton.Notify_Linked(this);
                    button = newButton;
                    Messages.Message(
                        "PH_ButtonConnectSuccess".Translate(newButton.PositionHeld.ToString()), newButton, MessageTypeDefOf.PositiveEvent);
                }
                else
                {
                    Messages.Message("PH_ButtonConnectFailed".Translate(t.ToString()), MessageTypeDefOf.RejectInput);
                }
            }, null);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref this.button, "button");
            Scribe_Values.Look(ref this.remoteState, "remoteState", DoorRemote_State.Free);
            Scribe_Values.Look(ref this.securedRemotely, "strictPolicy", true);
        }
    }
}
