using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using Verse.AI;

namespace DoorsExpanded
{
    public class JobDriver_PushButton : JobDriver
    {
        protected Building_DoorRemoteButton Button => (Building_DoorRemoteButton)job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn pawn = base.pawn;
            LocalTargetInfo target = Button;
            Job job = base.job;
            bool errorOnFailed2 = errorOnFailed;
            return pawn.Reserve(target, job, 1, -1, null, errorOnFailed2);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Do(delegate
            {
                Button.PushButton();
            });
        }
    }

}
