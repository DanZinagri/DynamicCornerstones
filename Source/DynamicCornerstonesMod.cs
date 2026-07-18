using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using VFETribals;

namespace DynamicCornerstones
{
    public class DynamicCornerstonesSettings : ModSettings
    {
        public const float DefaultPotency = 0.10f;

        public HashSet<string> disabledStats = new HashSet<string>();
        // Generated cornerstones: stat defName -> potency (absent = DefaultPotency).
        public Dictionary<string, float> statPotencies = new Dictionary<string, float>();
        // Authored cornerstones: cornerstone defName -> scale (absent = 1.0).
        public Dictionary<string, float> cornerstoneScales = new Dictionary<string, float>();

        public float PotencyFor(string statDefName)
        {
            return statPotencies.TryGetValue(statDefName, out var value) ? value : DefaultPotency;
        }

        public void SetPotency(string statDefName, float value)
        {
            if (Mathf.Abs(value - DefaultPotency) < 0.001f)
            {
                statPotencies.Remove(statDefName);
            }
            else
            {
                statPotencies[statDefName] = value;
            }
        }

        public float ScaleFor(string cornerstoneDefName)
        {
            return cornerstoneScales.TryGetValue(cornerstoneDefName, out var value) ? value : 1f;
        }

        public void SetScale(string cornerstoneDefName, float value)
        {
            if (Mathf.Abs(value - 1f) < 0.001f)
            {
                cornerstoneScales.Remove(cornerstoneDefName);
            }
            else
            {
                cornerstoneScales[cornerstoneDefName] = value;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref disabledStats, "disabledStats", LookMode.Value);
            Scribe_Collections.Look(ref statPotencies, "statPotencies", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref cornerstoneScales, "cornerstoneScales", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                disabledStats ??= new HashSet<string>();
                statPotencies ??= new Dictionary<string, float>();
                cornerstoneScales ??= new Dictionary<string, float>();
            }
        }
    }

    public class DynamicCornerstonesMod : Mod
    {
        public static DynamicCornerstonesSettings Settings;

        private const string AuthoredSectionKey = "__authored";

        private Vector2 scrollPos;
        private string search = "";
        private List<StatDef> cachedStats;
        private List<CornerstoneDef> cachedAuthored;
        // Session-only UI state; sections start collapsed for easier navigation.
        private readonly HashSet<string> expandedSections = new HashSet<string>();

        private const float RowHeight = 28f;
        private const float HeaderHeight = 32f;

        public DynamicCornerstonesMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<DynamicCornerstonesSettings>();
        }

        public override string SettingsCategory() => "DynCS.ModTitle".Translate();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            var top = new Rect(inRect.x, inRect.y, inRect.width, 92f);
            listing.Begin(top);

            GUI.color = Color.yellow;
            listing.Label("DynCS.RestartNote".Translate());
            GUI.color = Color.white;

            search = listing.TextEntryLabeled("DynCS.Filter".Translate() + ": ", search);

            var buttons = listing.GetRect(28f);
            var x = buttons.x;
            if (DrawButton(ref x, buttons.y, "DynCS.ExpandAll".Translate()))
            {
                expandedSections.Add(AuthoredSectionKey);
                foreach (var stat in Stats())
                {
                    expandedSections.Add(CategoryLabel(stat));
                }
            }
            if (DrawButton(ref x, buttons.y, "DynCS.CollapseAll".Translate()))
            {
                expandedSections.Clear();
            }
            if (DrawButton(ref x, buttons.y, "DynCS.EnableAll".Translate()))
            {
                Settings.disabledStats.Clear();
            }
            if (DrawButton(ref x, buttons.y, "DynCS.DisableAll".Translate()))
            {
                foreach (var stat in Stats())
                {
                    Settings.disabledStats.Add(stat.defName);
                }
            }
            if (DrawButton(ref x, buttons.y, "DynCS.ResetSliders".Translate()))
            {
                Settings.statPotencies.Clear();
                Settings.cornerstoneScales.Clear();
            }
            listing.End();

            DrawEntryList(new Rect(inRect.x, top.yMax + 6f, inRect.width, inRect.height - top.height - 6f));
        }

        private static bool DrawButton(ref float x, float y, string label)
        {
            var pressed = Widgets.ButtonText(new Rect(x, y, 130f, 28f), label);
            x += 138f;
            return pressed;
        }

        private List<StatDef> Stats()
        {
            return cachedStats ??= CornerstoneGenerator.EligibleStats();
        }

        private List<CornerstoneDef> Authored()
        {
            return cachedAuthored ??= CornerstoneGenerator.AuthoredCornerstones();
        }

        private bool MatchesSearch(string label, string defName)
        {
            return search.NullOrEmpty()
                || (label != null && label.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0)
                || defName.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // While a search filter is active, every section is forced open so no
        // matches are hidden; toggling is disabled until the filter is cleared.
        private bool SectionOpen(string key)
        {
            return !search.NullOrEmpty() || expandedSections.Contains(key);
        }

        private void DrawEntryList(Rect outRect)
        {
            var authored = new List<CornerstoneDef>();
            foreach (var def in Authored())
            {
                if (MatchesSearch(def.label, def.defName))
                {
                    authored.Add(def);
                }
            }
            var statGroups = new List<KeyValuePair<string, List<StatDef>>>();
            foreach (var stat in Stats())
            {
                if (!MatchesSearch(stat.label, stat.defName))
                {
                    continue;
                }
                var cat = CategoryLabel(stat);
                if (statGroups.Count == 0 || statGroups[statGroups.Count - 1].Key != cat)
                {
                    statGroups.Add(new KeyValuePair<string, List<StatDef>>(cat, new List<StatDef>()));
                }
                statGroups[statGroups.Count - 1].Value.Add(stat);
            }

            var viewHeight = 0f;
            if (authored.Count > 0)
            {
                viewHeight += HeaderHeight;
                if (SectionOpen(AuthoredSectionKey))
                {
                    viewHeight += authored.Count * RowHeight;
                }
            }
            foreach (var group in statGroups)
            {
                viewHeight += HeaderHeight;
                if (SectionOpen(group.Key))
                {
                    viewHeight += group.Value.Count * RowHeight;
                }
            }

            var viewRect = new Rect(0f, 0f, outRect.width - 20f, viewHeight);
            Widgets.BeginScrollView(outRect, ref scrollPos, viewRect);
            var y = 0f;

            if (authored.Count > 0
                && DrawSectionHeader(viewRect, ref y, AuthoredSectionKey, "DynCS.ExistingCornerstones".Translate(), authored.Count))
            {
                foreach (var def in authored)
                {
                    DrawAuthoredRow(viewRect, ref y, def);
                }
            }

            foreach (var group in statGroups)
            {
                if (DrawSectionHeader(viewRect, ref y, group.Key, group.Key, group.Value.Count))
                {
                    foreach (var stat in group.Value)
                    {
                        DrawGeneratedRow(viewRect, ref y, stat);
                    }
                }
            }
            Widgets.EndScrollView();
        }

        private bool DrawSectionHeader(Rect viewRect, ref float y, string key, string label, int count)
        {
            var rect = new Rect(0f, y, viewRect.width, HeaderHeight);
            var open = SectionOpen(key);

            Widgets.DrawLightHighlight(rect);
            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
            }

            var arrowRect = new Rect(rect.x + 6f, rect.y + (HeaderHeight - 18f) / 2f, 18f, 18f);
            GUI.DrawTexture(arrowRect, open ? TexButton.Collapse : TexButton.Reveal);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(arrowRect.xMax + 8f, rect.y, rect.width - 80f, rect.height), $"{label} ({count})");
            Text.Font = GameFont.Small;

            if (Widgets.ButtonInvisible(rect) && search.NullOrEmpty())
            {
                if (open)
                {
                    expandedSections.Remove(key);
                    SoundDefOf.TabClose.PlayOneShotOnCamera();
                }
                else
                {
                    expandedSections.Add(key);
                    SoundDefOf.TabOpen.PlayOneShotOnCamera();
                }
            }

            y += HeaderHeight;
            return SectionOpen(key);
        }

        private void DrawAuthoredRow(Rect viewRect, ref float y, CornerstoneDef def)
        {
            var rowRect = new Rect(0f, y, viewRect.width, RowHeight);
            SplitRow(rowRect, out var labelRect, out var sliderRect, out var pctRect);

            Widgets.Label(labelRect, def.LabelCap);
            var scale = Settings.ScaleFor(def.defName);
            var newScale = Widgets.HorizontalSlider(sliderRect, scale, 0f, 2f, true, null, null, null, 0.05f);
            if (newScale != scale)
            {
                Settings.SetScale(def.defName, newScale);
            }
            Widgets.Label(pctRect, newScale.ToStringPercent());

            if (Mouse.IsOver(rowRect))
            {
                Widgets.DrawHighlight(rowRect);
                TooltipHandler.TipRegion(rowRect,
                    def.description + "\n\n" + "DynCS.EffectScaleTip".Translate(newScale.ToStringPercent()).Resolve());
            }
            y += RowHeight;
        }

        private void DrawGeneratedRow(Rect viewRect, ref float y, StatDef stat)
        {
            var rowRect = new Rect(0f, y, viewRect.width, RowHeight);
            SplitRow(rowRect, out var labelRect, out var sliderRect, out var pctRect);

            var enabled = !Settings.disabledStats.Contains(stat.defName);
            var was = enabled;
            Widgets.CheckboxLabeled(labelRect.LeftPart(0.96f), stat.LabelForFullStatListCap, ref enabled);
            if (enabled != was)
            {
                if (enabled)
                {
                    Settings.disabledStats.Remove(stat.defName);
                }
                else
                {
                    Settings.disabledStats.Add(stat.defName);
                }
            }

            var potency = Settings.PotencyFor(stat.defName);
            var newPotency = Widgets.HorizontalSlider(sliderRect, potency, 0.01f, 0.25f, true, null, null, null, 0.01f);
            if (newPotency != potency)
            {
                Settings.SetPotency(stat.defName, newPotency);
            }
            Widgets.Label(pctRect, newPotency.ToStringPercent());

            if (Mouse.IsOver(rowRect))
            {
                Widgets.DrawHighlight(rowRect);
                var tip = CornerstoneGenerator.EffectDescription(stat, newPotency);
                if (!stat.description.NullOrEmpty())
                {
                    tip += "\n\n" + stat.description;
                }
                TooltipHandler.TipRegion(rowRect, tip);
            }
            y += RowHeight;
        }

        private static void SplitRow(Rect rowRect, out Rect labelRect, out Rect sliderRect, out Rect pctRect)
        {
            const float pctWidth = 48f;
            var sliderWidth = Mathf.Min(220f, rowRect.width * 0.32f);
            labelRect = new Rect(rowRect.x + 12f, rowRect.y, rowRect.width - sliderWidth - pctWidth - 30f, rowRect.height);
            sliderRect = new Rect(labelRect.xMax + 6f, rowRect.y + 4f, sliderWidth, rowRect.height - 8f);
            pctRect = new Rect(sliderRect.xMax + 6f, rowRect.y + 2f, pctWidth, rowRect.height);
        }

        private static string CategoryLabel(StatDef stat)
        {
            return stat.category?.LabelCap.ToString() ?? "DynCS.OtherCategory".Translate().Resolve();
        }
    }
}
