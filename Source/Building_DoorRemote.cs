using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DoorsExpanded
{
    public class Building_DoorRemote : Building_DoorExpanded
    {
        private Building_DoorRemoteButton button;
        private bool securedRemotely = false;

        public Building_DoorRemoteButton Button
        {
            get => button;
            protected set
            {
                if (button != value)
                {
                    var oldButton = button;
                    button = value;
                    oldButton?.Notify_Unlinked(this);
                    button?.Notify_Linked(this);
                }
            }
        }

        public bool SecuredRemotely
        {
            get => securedRemotely;
            set
            {
                securedRemotely = value;
                // Reactive update due to Forbidden depending on SecuredRemotely (via ForcedClosed).
                Notify_ForbiddenInputChanged();
            }
        }

        protected override bool OpenInt
        {
            set
            {
                base.OpenInt = value;
                // Reactive update due to Forbidden depending on OpenInt (via Open via ForcedClosed).
                Notify_ForbiddenInputChanged();
            }
        }

        // When secured remotely, only care about whether its held open remotely;
        // else can be held open either remotely or by gizmo.
        public override bool HoldOpen => securedRemotely ? HoldOpenRemotely : HoldOpenRemotely || base.HoldOpen;

        public bool HoldOpenRemotely => Button != null && Button.ButtonOn;

        public bool ForcedClosed => SecuredRemotely && !Open;

        public override bool Forbidden => ForcedClosed || base.Forbidden;

        // For purposes of determining whether a door can be closed automatically,
        // treat a powered door that's linked to an enabled button as always being "friendly touched".
        internal protected override bool FriendlyTouchedRecently =>
            (!button?.NeedsPower ?? false) && DoorPowerOn || base.FriendlyTouchedRecently;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref button, nameof(button));
            Scribe_Values.Look(ref securedRemotely, nameof(securedRemotely), false);
        }

        private const float LockPulseFrequency = 1.5f; // OverlayDrawer.PulseFrequency is 4f
        private const float LockPulseAmplitude = 0.7f; // same as OverlayDrawer.PulseAmplitude

        public override void Draw()
        {
            if (ForcedClosed)
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
            if (Button != null)
                GenDraw.DrawLineBetween(this.TrueCenter(), Button.TrueCenter());
            base.DrawExtraSelectionOverlays();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var gizmo in base.GetGizmos())
            {
                if (gizmo is Command_Toggle command && command.defaultLabel == "CommandToggleDoorHoldOpen".Translate())
                {
                    // Disable hold open toggle gizmo if secured remotely.
                    if (SecuredRemotely)
                    {
                        gizmo.Disable("PH_RemoteDoorSecuredRemotely".Translate());
                    }
                    yield return gizmo;

                    // Insert all our custom gizmos right after hold open toggle gizmo,
                    // with the secured remotely gizmo being the first.

                    var toggle = new Command_Toggle
                    {
                        defaultLabel = "PH_RemoteDoorSecuredRemotely".Translate(),
                        defaultDesc = "PH_RemoteDoorSecuredRemotelyDesc".Translate(),
                        icon = TexButton.SecuredRemotely,
                        isActive = () => SecuredRemotely,
                        toggleAction = () => SecuredRemotely = !SecuredRemotely,
                    };
                    if (Button == null)
                        toggle.Disable("PH_ButtonNeeded".Translate());
                    if (!DoorPowerOn)
                        toggle.Disable("PH_PowerNeeded".Translate());
                    yield return toggle;

                    yield return new Command_Action
                    {
                        defaultLabel = "PH_ButtonConnect".Translate(),
                        defaultDesc = "PH_ButtonConnectDesc".Translate(),
                        icon = TexButton.ConnectToButton,
                        action = ButtonConnect,
                    };

                    if (Button != null)
                    {
                        yield return new Command_Action
                        {
                            defaultLabel = "PH_ButtonDisconnect".Translate(),
                            defaultDesc = "PH_ButtonDisconnectDesc".Translate(),
                            icon = TexButton.DisconnectButton,
                            action = ButtonDisconnect,
                        };
                    }
                }
                else
                {
                    yield return gizmo;
                }
            }
        }

        public void Notify_ButtonPushed()
        {
            if (DoorPowerOff)
            {
                Messages.Message("PH_CannotOpenRemotelyWithoutPower".Translate(Label), this, MessageTypeDefOf.RejectInput);
                return;
            }
            UpdateOpenStateFromButtonEvent();
        }

        private void UpdateOpenStateFromButtonEvent()
        {
            if (Button.ButtonOn != Open)
            {
                if (Open)
                {
                    DoorTryClose();
                }
                else
                {
                    DoorOpen();
                }
            }
        }

        private void ButtonConnect()
        {
            var tp = new TargetingParameters
            {
                validator = t => t.Thing is Building_DoorRemoteButton,
                canTargetBuildings = true,
                canTargetPawns = false,
            };
            Find.Targeter.BeginTargeting(tp, t =>
            {
                if (t.Thing is Building_DoorRemoteButton otherButton)
                {
                    if (Button != otherButton)
                    {
                        if (Button != null)
                        {
                            DisplayUnlinkedMessage();
                        }
                        Button = otherButton;
                        Messages.Message("PH_ButtonConnectSuccess".Translate(otherButton.PositionHeld.ToString()), otherButton,
                            MessageTypeDefOf.PositiveEvent);
                        UpdateOpenStateFromButtonEvent();
                    }
                }
                else
                {
                    Messages.Message("PH_ButtonConnectFailed".Translate(t.ToString()), MessageTypeDefOf.RejectInput);
                }
            }, null);
        }

        private void ButtonDisconnect()
        {
            DisplayUnlinkedMessage();
            Button = null;
            SecuredRemotely = false;
        }

        private void DisplayUnlinkedMessage()
        {
            if (Button.Spawned)
                Messages.Message("PH_ButtonUnlinked".Translate(Button.PositionHeld.ToString()), Button, MessageTypeDefOf.SilentInput);
            else
                Messages.Message("PH_ButtonUnlinkedUnspawned".Translate(Button.PositionHeld.ToString()), MessageTypeDefOf.SilentInput);
        }
    }
}
