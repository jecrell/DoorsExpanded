using System.Collections.Generic;
using Verse;

namespace DoorsExpanded
{
    public class CompPostProcessText : ThingComp
    {
        public CompProperties_PostProcessText Props => (CompProperties_PostProcessText)props;
    }

    // ThingDefs with this comp will have their label and description updated after translation def injection.
    public class CompProperties_PostProcessText : CompProperties
    {
        public ThingDef defaultLabelAndDescriptionFrom;
        public bool appendSizeToLabel;

        [Unsaved(false)]
        private string origLabel;

        public CompProperties_PostProcessText()
        {
            compClass = typeof(CompPostProcessText);
        }

        public override void ResolveReferences(ThingDef parentDef)
        {
            // Note: ResolveReferences can run more than once for a single def, so ensure it's idempotent.
            if (defaultLabelAndDescriptionFrom != null)
            {
                if (parentDef.label.NullOrEmpty())
                    parentDef.label = defaultLabelAndDescriptionFrom.GetCompProperties<CompProperties_PostProcessText>()?.origLabel ??
                        defaultLabelAndDescriptionFrom.label;
                if (parentDef.description.NullOrEmpty())
                    parentDef.description = defaultLabelAndDescriptionFrom.description;
            }
            if (appendSizeToLabel && origLabel == null)
            {
                origLabel = parentDef.label;
                var size = parentDef.Size;
                parentDef.label = $"{parentDef.label} ({size.x}x{size.z})";
            }
        }

        // This serves as a useful hook for cleaning up after ourselves.
        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            // Comps add overhead, so since we're done, remove ourselves.
            parentDef.comps.Remove(this);
            yield break;
        }
    }
}
