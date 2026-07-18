using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using VFETribals;

namespace DynamicCornerstones
{
    [StaticConstructorOnStartup]
    public static class CornerstoneGenerator
    {
        public const string DefNamePrefix = "DynCS_";

        // Rules are loaded from CornerstoneRulesDef XML (Defs/CornerstoneRules.xml)
        // so users and other mods can patch or extend them without a recompile.
        private static readonly HashSet<string> Categories = new HashSet<string>();
        private static readonly HashSet<string> UseNegativeValue = new HashSet<string>();
        private static readonly HashSet<string> StatBlacklist = new HashSet<string>();

        static CornerstoneGenerator()
        {
            LoadRules();
            RescaleAuthoredCornerstones();
            Generate();
        }

        // Union of every CornerstoneRulesDef in the DefDatabase, so additional
        // rules defs shipped by other mods merge in automatically.
        private static void LoadRules()
        {
            foreach (var rules in DefDatabase<CornerstoneRulesDef>.AllDefsListForReading)
            {
                if (rules.categories != null)
                {
                    Categories.UnionWith(rules.categories);
                }
                if (rules.useNegativeValue != null)
                {
                    UseNegativeValue.UnionWith(rules.useNegativeValue);
                }
                if (rules.statBlacklist != null)
                {
                    StatBlacklist.UnionWith(rules.statBlacklist);
                }
            }
            if (Categories.Count == 0)
            {
                Log.Warning("[Dynamic Cornerstones] No CornerstoneRulesDef with categories found; no cornerstones will be generated.");
            }
        }

        // Each XML cornerstone has its own scale slider in settings (default 100% = untouched). VFE Tribals reads these lists live on every
        // stat query, so this covers cornerstones already unlocked in saves too.
        private static void RescaleAuthoredCornerstones()
        {
            var settings = DynamicCornerstonesMod.Settings;
            foreach (var def in AuthoredCornerstones())
            {
                var scale = settings.ScaleFor(def.defName);
                if (Mathf.Abs(scale - 1f) < 0.005f)
                {
                    continue;
                }
                var changed = false;
                if (def.statOffsets != null)
                {
                    foreach (var mod in def.statOffsets)
                    {
                        mod.value *= scale;
                        changed = true;
                    }
                }
                if (def.statFactors != null)
                {
                    foreach (var mod in def.statFactors)
                    {
                        mod.value = 1f + (mod.value - 1f) * scale;
                        changed = true;
                    }
                }
                if (changed)
                {
                    def.description += "\n\n" + "DynCS.ScaledEffectNote".Translate($"{scale:0.##}").Resolve();
                }
            }
        }

        private static void Generate()
        {
            var settings = DynamicCornerstonesMod.Settings;
            var added = 0;
            foreach (var stat in EligibleStats())
            {
                if (settings.disabledStats.Contains(stat.defName))
                {
                    continue;
                }
                DefGenerator.AddImpliedDef(BuildDef(stat, settings.PotencyFor(stat.defName)));
                added++;
            }
            Log.Message($"[Dynamic Cornerstones] Generated {added} cornerstones from loaded stat defs.");
        }

        public static List<CornerstoneDef> AuthoredCornerstones()
        {
            var result = new List<CornerstoneDef>();
            foreach (var def in DefDatabase<CornerstoneDef>.AllDefsListForReading)
            {
                if (!def.defName.StartsWith(DefNamePrefix))
                {
                    result.Add(def);
                }
            }
            result.SortBy(d => d.label ?? d.defName);
            return result;
        }

        public static List<StatDef> EligibleStats()
        {
            var covered = StatsCoveredByExistingCornerstones();
            var result = new List<StatDef>();
            foreach (var stat in DefDatabase<StatDef>.AllDefsListForReading)
            {
                if (IsEligible(stat) && !covered.Contains(stat))
                {
                    result.Add(stat);
                }
            }
            result.SortBy(s => s.category?.displayOrder ?? int.MaxValue, s => s.label ?? s.defName);
            return result;
        }

        private static bool IsEligible(StatDef stat)
        {
            if (stat == null || StatBlacklist.Contains(stat.defName))
            {
                return false;
            }
            if (stat.category == null || !Categories.Contains(stat.category.defName))
            {
                return false;
            }
            if (stat.alwaysHide || stat.forInformationOnly || !stat.showOnHumanlikes)
            {
                return false;
            }
            return true;
        }

        // Stats already touched by (XML) cornerstones, so the window doesn't fill with duplicates of VFE Tribals' own offerings.
        private static HashSet<StatDef> StatsCoveredByExistingCornerstones()
        {
            var covered = new HashSet<StatDef>();
            foreach (var def in AuthoredCornerstones())
            {
                if (def.statOffsets != null)
                {
                    foreach (var mod in def.statOffsets)
                    {
                        covered.Add(mod.stat);
                    }
                }
                if (def.statFactors != null)
                {
                    foreach (var mod in def.statFactors)
                    {
                        covered.Add(mod.stat);
                    }
                }
            }
            return covered;
        }

        public static bool UseNegative(StatDef stat) => UseNegativeValue.Contains(stat.defName);

        // Multiplying works for any stat whose baseline is nonzero; stats that idle at zero (toxic resistance, body size offset, ...) need a flat offset instead.
        public static bool UsesFactor(StatDef stat) => Mathf.Abs(stat.defaultBaseValue) > 0.0001f;

        public static string EffectDescription(StatDef stat, float potency)
        {
            if (UsesFactor(stat))
            {
                var key = UseNegative(stat) ? "DynCS.EffectReducedBy" : "DynCS.EffectIncreasedBy";
                return key.Translate(stat.LabelForFullStatListCap, potency.ToStringPercent()).Resolve();
            }
            var offset = (UseNegative(stat) ? -1f : 1f) * potency;
            return "DynCS.EffectOffset".Translate(stat.LabelForFullStatListCap,
                stat.ValueToString(offset, ToStringNumberSense.Offset)).Resolve();
        }

        private static CornerstoneDef BuildDef(StatDef stat, float potency)
        {
            var def = new CornerstoneDef
            {
                defName = DefNamePrefix + stat.defName,
                label = stat.LabelForFullStatListCap,
                description = EffectDescription(stat, potency),
                blurb = "DynCS.Blurb".Translate("{0}", stat.label ?? stat.defName).Resolve(),
                generated = true,
            };
            if (UsesFactor(stat))
            {
                var factor = UseNegative(stat) ? 1f - potency : 1f + potency;
                def.statFactors = new List<StatModifier> { new StatModifier { stat = stat, value = factor } };
            }
            else
            {
                var offset = (UseNegative(stat) ? -1f : 1f) * potency;
                def.statOffsets = new List<StatModifier> { new StatModifier { stat = stat, value = offset } };
            }
            return def;
        }
    }
}
