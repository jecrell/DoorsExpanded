using System.Collections.Generic;
using Verse.AI;

namespace DoorsExpanded
{
    public class JobDriver_PushButton : JobDriver
    {
        protected Building_DoorRemoteButton Button => (Building_DoorRemoteButton)job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Button, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            yield return Toils_General.Do(() => Button.PushButton());
        }
    }
}
