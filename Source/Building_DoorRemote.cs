using System;
using System.Collections.Generic;
using RimWorld;
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
        private Building_DoorRemoteButton button;
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
                    if (!DoorPowerOn)
                        error = "PH_PowerNeeded".Translate();
                    if (error != "")
                    {
                        Messages.Message(error, MessageTypeDefOf.RejectInput);
                        securedRemotely = false;
                        return;
                    }

                    if (remoteState == DoorRemote_State.Free && !Open)
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

        public override bool WillCloseSoon => remoteState != DoorRemote_State.ForcedOpen && base.WillCloseSoon;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref button, nameof(button));
            Scribe_Values.Look(ref remoteState, nameof(remoteState), DoorRemote_State.Free);
            Scribe_Values.Look(ref securedRemotely, nameof(securedRemotely), true);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (SecuredRemotely && remoteState == DoorRemote_State.ForcedClose)
                {
                    DoorTryClose();
                    foreach (var invisDoor in InvisDoors)
                    {
                        invisDoor.SetForbidden(true);
                    }
                }
                if (remoteState == DoorRemote_State.ForcedOpen)
                {
                    DoorOpen(int.MaxValue);
                    holdOpenInt = true;
                }
            }
        }

        public override void Notify_PawnApproaching(Pawn p)
        {
            if (remoteState != DoorRemote_State.ForcedOpen && SecuredRemotely)
                return;
            base.Notify_PawnApproaching(p);
        }

        public override bool PawnCanOpenSpecialCases(Pawn p)
        {
            return (remoteState == DoorRemote_State.Free || remoteState == DoorRemote_State.ForcedOpen) && base.PawnCanOpenSpecialCases(p);
        }

        public override bool BlocksPawn(Pawn p)
        {
            return base.BlocksPawn(p) || (SecuredRemotely && remoteState != DoorRemote_State.ForcedOpen);
        }

        protected override bool ShouldKeepDoorOpen()
        {
            return remoteState == DoorRemote_State.ForcedOpen || base.ShouldKeepDoorOpen();
        }

        private const float LockPulseFrequency = 1.5f; // OverlayDrawer.PulseFrequency is 4f
        private const float LockPulseAmplitude = 0.7f; // same as OverlayDrawer.PulseAmplitude

        public override void Draw()
        {
            // TODO: Buildings never tick when unspawned - remove this check.
            if (!Spawned)
                return;
            if (SecuredRemotely && remoteState == DoorRemote_State.ForcedClose)
            {
                // This is based off OverlayDrawer.RenderQuestionMarkOverlay/RenderPulsingOverlayInternal, with customized parameters.
                var drawLoc = DrawPos;
                drawLoc.y = Altitudes.AltitudeFor(AltitudeLayer.MetaOverlays) + Altitudes.AltInc * 6;
                var sineInput = (Time.realtimeSinceStartup + 397f * (thingIDNumber % 571)) * LockPulseFrequency;
                var alpha = ((float)Math.Sin(sineInput) + 1f) * 0.3f;
                alpha = 0.3f + alpha * LockPulseAmplitude;
                var material = FadedMaterialPool.FadedVersionOf(TexOverlay.LockedOverlay, alpha);
                Graphics.DrawMesh(MeshPool.plane05, drawLoc, Quaternion.identity, material, 0);
            }
            base.Draw();
        }

        public override void DrawExtraSelectionOverlays()
        {
            if (button != null)
                GenDraw.DrawLineBetween(this.TrueCenter(), button.TrueCenter());
            base.DrawExtraSelectionOverlays();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var gizmo in base.GetGizmos())
                yield return gizmo;

            yield return new Command_Action
            {
                defaultLabel = "PH_ButtonConnect".Translate(),
                defaultDesc = "PH_ButtonConnectDesc".Translate(),
                icon = TexButton.ConnectToButton,
                disabled = false,
                disabledReason = "",
                action = ButtonConnect,
            };

            if (button != null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "PH_ButtonDisconnect".Translate(),
                    defaultDesc = "PH_ButtonDisconnectDesc".Translate(),
                    icon = TexButton.DisconnectButton,
                    disabled = false,
                    disabledReason = "",
                    action = ButtonDisconnect,
                };
            }

            yield return new Command_Toggle
            {
                defaultLabel = "PH_RemoteDoorSecuredRemotely".Translate(),
                defaultDesc = "PH_RemoteDoorSecuredRemotelyDesc".Translate(),
                icon = TexButton.SecuredRemotely,
                disabled = false,
                disabledReason = "",
                isActive = () => SecuredRemotely,
                toggleAction = () => SecuredRemotely = !SecuredRemotely, 
            };
        }

        public void Notify_ButtonPushed()
        {
            if (PowerComp != null && !DoorPowerOn)
            {
                Messages.Message("PH_CannotOpenRemotelyWithoutPower".Translate(Label), this, MessageTypeDefOf.RejectInput);
                return;
            }

            if (Open)
            {
                holdOpenInt = false;
                DoorTryClose();
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
                DoorOpen(int.MaxValue);
                holdOpenInt = true;
                remoteState = DoorRemote_State.ForcedOpen;
                foreach (var invisDoor in InvisDoors)
                {
                    invisDoor.SetForbidden(false);
                }
            }
        }

        private void ButtonConnect()
        {
            var tp = new TargetingParameters
            {
                validator = t => t.Thing is Building_DoorRemoteButton bt,
                canTargetBuildings = true,
                canTargetPawns = false,
            };
            Find.Targeter.BeginTargeting(tp, t =>
            {
                if (t.Thing is Building_DoorRemoteButton otherButton)
                {
                    if (button != null)
                    {
                        if (button.Spawned)
                            Messages.Message("PH_ButtonUnlinked".Translate(button.PositionHeld.ToString()), button,
                                MessageTypeDefOf.SilentInput);
                        else
                            Messages.Message("PH_ButtonUnlinkedUnspawned".Translate(button.PositionHeld.ToString()),
                                MessageTypeDefOf.SilentInput);
                    }
                    otherButton.Notify_Linked(this);
                    button = otherButton;
                    Messages.Message("PH_ButtonConnectSuccess".Translate(otherButton.PositionHeld.ToString()), otherButton,
                        MessageTypeDefOf.PositiveEvent);
                }
                else
                {
                    Messages.Message("PH_ButtonConnectFailed".Translate(t.ToString()), MessageTypeDefOf.RejectInput);
                }
            }, null);
        }

        private void ButtonDisconnect()
        {
            if (button != null)
                button.Notify_Unlinked(this);
            button = null;
        }
    }
}
