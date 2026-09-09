using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Verse;

namespace GlueRimworld
{
    /// <summary>
    /// Loads the RimWorld-specific adapter/seed JSON under Seeds/ (need-map, schedule-map,
    /// skill-taxonomy) directly off disk, relative to this mod's own ModContentPack.RootDir --
    /// the same "seed file lives with the game-integration project, not the shared engine
    /// repo" convention GlueActors uses for its own colony/pawn-* seed data.
    /// </summary>
    public sealed class RimworldSeedCatalog
    {
        public Dictionary<string, string> NeedMap { get; } = new();
        public Dictionary<string, double> NeedDecayRates { get; } = new();
        public Dictionary<string, string> ScheduleMap { get; } = new();
        public Dictionary<string, JObject> Skills { get; } = new();
        public JArray CandidatePack { get; } = new();
        public Dictionary<string, JObject> NativeJobMappings { get; } = new();

        public static RimworldSeedCatalog LoadFromModContent(ModContentPack content)
        {
            var catalog = new RimworldSeedCatalog();
            var seedsDir = Path.Combine(content.RootDir, "Seeds");

            catalog.LoadNeedMap(Path.Combine(seedsDir, "rimworld-need-map.json"));
            catalog.LoadScheduleMap(Path.Combine(seedsDir, "rimworld-schedule-map.json"));
            catalog.LoadSkills(Path.Combine(seedsDir, "rimworld-pawn-skill-taxonomy.json"));
            catalog.LoadCandidatePack(Path.Combine(seedsDir, "rimworld-work-candidates.json"));

            return catalog;
        }

        private void LoadNeedMap(string path)
        {
            var root = ReadJson(path);
            if (root == null) return;
            foreach (var entry in (root["needMappings"] as JArray) ?? new JArray())
            {
                var rwDef = entry["rimworldNeedDef"]?.Value<string>();
                var glueKey = entry["glueNeedKey"]?.Value<string>();
                if (string.IsNullOrEmpty(rwDef) || string.IsNullOrEmpty(glueKey)) continue;
                NeedMap[rwDef!] = glueKey!;
                NeedDecayRates[glueKey!] = entry["defaultDecayRatePerHour"]?.Value<double>() ?? 0.02;
            }
        }

        private void LoadScheduleMap(string path)
        {
            var root = ReadJson(path);
            if (root == null) return;
            foreach (var entry in (root["scheduleMappings"] as JArray) ?? new JArray())
            {
                var rwDef = entry["rimworldTimeAssignmentDef"]?.Value<string>();
                var mode = entry["glueScheduleMode"]?.Value<string>();
                if (string.IsNullOrEmpty(rwDef) || string.IsNullOrEmpty(mode)) continue;
                ScheduleMap[rwDef!] = mode!;
            }
        }

        private void LoadSkills(string path)
        {
            var root = ReadJson(path);
            if (root == null) return;
            foreach (var entry in (root["skills"] as JArray) ?? new JArray())
            {
                var defName = entry["defName"]?.Value<string>();
                if (string.IsNullOrEmpty(defName)) continue;
                Skills[defName!] = (JObject)entry;
            }
        }

        private void LoadCandidatePack(string path)
        {
            var root = ReadJson(path);
            if (root == null) return;
            if (root["nativeJobMappings"] is JObject mappings)
            {
                foreach (var mapping in mappings.Properties())
                {
                    if (mapping.Value is JObject mappingObject)
                        NativeJobMappings[mapping.Name] = (JObject)mappingObject.DeepClone();
                }
            }
            foreach (var candidate in (root["candidates"] as JArray) ?? new JArray())
            {
                if (candidate is JObject candidateObject)
                    CandidatePack.Add(candidateObject.DeepClone());
            }
        }

        private static JObject? ReadJson(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    Log.Warning($"[GlueRimworld] Seed file missing: {path}");
                    return null;
                }
                return JObject.Parse(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Log.Error($"[GlueRimworld] Failed to parse seed file {path}: {ex.Message}");
                return null;
            }
        }

        public string GlueScheduleModeFor(string rimworldTimeAssignmentDefName) =>
            ScheduleMap.TryGetValue(rimworldTimeAssignmentDefName, out var mode) ? mode : "anything";

        public string Summary() =>
            $"needMap={NeedMap.Count} scheduleMap={ScheduleMap.Count} skills={Skills.Count} candidates={CandidatePack.Count} nativeMappings={NativeJobMappings.Count}";
    }
}
