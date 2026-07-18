using System.Collections.Generic;
using Verse;

namespace DynamicCornerstones
{
    // XML-editable rules for the stat sweep. All CornerstoneRulesDef instances in
    // the DefDatabase are merged (union), so other mods can extend the lists by
    // shipping their own def, and users can trim ours with PatchOperationRemove.
    public class CornerstoneRulesDef : Def
    {
        // Stat categories eligible for cornerstone generation.
        public List<string> categories = new List<string>();
        // Stats where a smaller number is the buff; the generated modifier is inverted.
        public List<string> useNegativeValue = new List<string>();
        // Stats never offered as cornerstones.
        public List<string> statBlacklist = new List<string>();
    }
}
