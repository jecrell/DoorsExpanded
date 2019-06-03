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
        private bool securedRemotely = false;

        public DoorRemote_State RemoteState => remoteState;

        public bool SecuredRemotely
        {
            get => securedRemotely;
            set
            {
                if (value == false && remoteState == DoorRemote_State.ForcedClose)
                {
                    remoteState = DoorRemote_State.Free;
                    foreach (var invisDoor in InvisDoors)
                    {
                        invisDoor.SetForbidden(false);
                    }
                }

                if (value == true)
                {
                    var error = "";
                    if (button == null)
                        error = "PH_ButtonNeeded".Translate();

                    if (!this.DoorPowerOn)
                        error = "PH_PowerNeeded".Translate();

                    if (error != "")
                    {
                        Messages.Message(error, MessageTypeDefOf.RejectInput);
                        securedRemotely = false;
                        return;
                    }
                    if (remoteState == DoorRemote_State.Free && !this.Open)
                    {
                        remoteState = DoorRemote_State.ForcedClose;
                        foreach (var invisDoor in InvisDoors)
                        {
                            invisDoor.SetForbidden(true);
                        }
                    }
                }
                securedRemotely = value;
            }
        }


        public override void Draw()
        {

            if (!this.Spawned) return;
            if (SecuredRemotely && remoteState == DoorRemote_State.ForcedClose)
            {
                var drawLoc = this.DrawPos;
                drawLoc.y = Altitudes.AltitudeFor(AltitudeLayer.MetaOverlays) + 0.28125f;
                var num = (Time.realtimeSinceStartup + 397f * (float)(this.thingIDNumber % 571)) * 1.5f;
                var num2 = ((float)Math.Sin((double)num) + 1f) * 0.3f;
                num2 = 0.3f + num2 * 0.7f;
                var mesh = MeshPool.plane05;
                var mat = TexOverlay.LockedOverlay;
                var material = FadedMaterialPool.FadedVersionOf(mat, num2);
                Graphics.DrawMesh(mesh, drawLoc, Quaternion.identity, material, 0);
            }
            base.Draw();
        }
        
        public override bool WillCloseSoon => remoteState != DoorRemote_State.ForcedOpen && base.WillCloseSoon;

        public override void Notify_PawnApproaching(Pawn p)
        {
            if (remoteState != DoorRemote_State.ForcedOpen && SecuredRemotely)
                return;
            base.Notify_PawnApproaching(p);
        }

        public override bool BlocksPawn(Pawn p)
        {
            return base.BlocksPawn(p) || (SecuredRemotely && remoteState != DoorRemote_State.ForcedOpen);
        }

        public override bool PawnCanOpenSpecialCases(Pawn p)
        {
            return (remoteState == DoorRemote_State.Free || remoteState == DoorRemote_State.ForcedOpen) && base.PawnCanOpenSpecialCases(p);
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

            if (button != null)
            {
                yield return new Command_Action()
                {
                    defaultLabel = "PH_ButtonDisconnect".Translate(),
                    defaultDesc = "PH_ButtonDisconnectDesc".Translate(),
                    icon = TexButton.DisconnectButton,
                    disabled = false,
                    disabledReason = "",
                    action = ClearButton
                };
            }

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
            if (this.PowerComp != null && !this.DoorPowerOn)
            {
                Messages.Message("PH_CannotOpenRemotelyWithoutPower".Translate(this.Label), this, MessageTypeDefOf.RejectInput);
                return;
            }

            if (this.Open)
            {
                holdOpenInt = false;
                this.DoorTryClose();
                if (!SecuredRemotely)
                    remoteState = DoorRemote_State.Free;
                else
                {
                    remoteState = DoorRemote_State.ForcedClose;
                    foreach (var invisDoor in InvisDoors)
                    {
                        invisDoor.SetForbidden(true);
                    }
                }
            }
            else
            {
                this.DoorOpen(int.MaxValue);
                holdOpenInt = true;
                remoteState = DoorRemote_State.ForcedOpen;
                foreach (var invisDoor in InvisDoors)
                {
                    invisDoor.SetForbidden(false);
                }
            }
        }

        private void ClearButton()
        {
            if (this.button != null)
                button.Notify_Unlinked(this);
            button = null;
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
            Scribe_Values.Look(ref this.securedRemotely, "securedRemotely", true);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (SecuredRemotely && remoteState == DoorRemote_State.ForcedClose)
                {
                    this.DoorTryClose();
                    foreach (var invisDoor in InvisDoors)
                    {
                        invisDoor.SetForbidden(true);
                    }
                }
                if (remoteState == DoorRemote_State.ForcedOpen)
                {
                    this.DoorOpen(int.MaxValue);
                    holdOpenInt = true;
                }
            }
        }
    }
}
