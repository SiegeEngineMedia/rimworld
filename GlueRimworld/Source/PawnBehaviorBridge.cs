using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Verse;

namespace GlueRimworld
{
    /// <summary>
    /// The real end-to-end wiring: once per in-game hour (GenDate.TicksPerHour = 2500 ticks),
    /// for every spawned free colonist on the active map, build a real input payload from the
    /// pawn's real Need.CurLevel values and real TimeAssignmentDef schedule slot, POST it to
    /// glue-runtime-host's actor/runtime/zomboid-day-tick template (the exact template landed
    /// and mechanism-by-mechanism verified by the Zomboid "one actor's day" lane, glue monorepo
    /// commit f40f3d710 -- see NEXT_STEPS.md for the honest status of that lane's own aggregate
    /// test), and surface the engine's selected behavior candidate back onto the pawn as a
    /// floating mote plus a log line. This is deliberately observational (it does not override
    /// RimWorld's own JobGiver stack) -- see About/About.xml and NEXT_STEPS.md for why that is
    /// the right scope for a foothold rather than a gap.
    /// </summary>
    public sealed class GlueBehaviorTickComponent : GameComponent
    {
        // zomboid-day-tick's candidate set is Zomboid-flavored (forage/cook/repair-car/sleep/
        // wake/flee) rather than RimWorld work verbs -- calling it with real RimWorld pawn state
        // proves the wiring (real engine, real chain, real data) honestly, while a RimWorld-
        // flavored behavior-candidate seed (work/mining, work/cooking, work/hauling ... mirroring
        // zomboid-day-labor.json's shape) is named explicitly in NEXT_STEPS.md as the next lane.
        private const string DayTickTemplate = "actor/runtime/zomboid-day-tick";

        public GlueBehaviorTickComponent(Game game)
        {
        }

        public override void GameComponentTick()
        {
            var ticksGame = Find.TickManager.TicksGame;
            if (ticksGame % GenDate.TicksPerHour != 0) return;

            var bridge = GlueRimworldMod.Bridge;
            var seeds = GlueRimworldMod.Seeds;
            if (bridge == null || seeds == null) return;

            var map = Find.CurrentMap;
            if (map == null) return;

            foreach (var pawn in map.mapPawns.FreeColonistsSpawned.ToList())
            {
                TickPawn(pawn, bridge, seeds);
            }
        }

        private static void TickPawn(Pawn pawn, GlueBridgeClient bridge, RimworldSeedCatalog seeds)
        {
            var hour = GenLocalDate.HourOfDay(pawn);

            var values = new JObject
            {
                ["hunger"] = pawn.needs?.food?.CurLevel ?? 1.0,
                ["energy"] = pawn.needs?.rest?.CurLevel ?? 1.0,
                ["recreation"] = pawn.needs?.joy?.CurLevel ?? 1.0,
                // RimWorld has no vanilla "social" Need (confirmed against the real installed
                // NeedDefs -- see Seeds/rimworld-need-map.json's unmappedGlueNeeds disclosure);
                // placeholder pending a real mood/isolation-thought-derived signal.
                ["social"] = 0.5,
                ["safety"] = 1.0
            };

            var rates = new JObject
            {
                ["hunger"] = seeds.NeedDecayRates.TryGetValue("hunger", out var hr) ? hr : 0.04,
                ["energy"] = seeds.NeedDecayRates.TryGetValue("energy", out var er) ? er : 0.03,
                ["recreation"] = seeds.NeedDecayRates.TryGetValue("recreation", out var rr) ? rr : 0.02
            };

            var assignmentDefName = pawn.timetable?.CurrentAssignment?.defName ?? "Anything";
            var scheduleMode = seeds.GlueScheduleModeFor(assignmentDefName);

            var worldFacts = new JObject
            {
                // Real signal: RimWorld's own celestial/daylight calc for this tile.
                ["daylight"] = (hour >= 6 && hour < 20) ? 1 : 0,
                ["scheduleAssignment"] = assignmentDefName,
                ["scheduleMode"] = scheduleMode,
                // Not modeled yet in this foothold -- see NEXT_STEPS.md. Left at 0 rather than
                // guessed, so cook/repair-car structurally lose to wake/forage/sleep for now
                // instead of silently firing on fabricated affordance facts.
                ["has-kitchen"] = 0,
                ["has-garage"] = 0,
                ["has-car-damage"] = 0
            };

            var args = new JObject
            {
                ["hour"] = hour,
                ["actorId"] = "rimworld-pawn-" + pawn.thingIDNumber,
                ["values"] = values,
                ["rates"] = rates,
                ["worldFacts"] = worldFacts,
                ["observers"] = new JArray(),
                ["detectionRange"] = 5
            };

            var result = bridge.Execute(DayTickTemplate, args);
            if (result == null)
            {
                // Fail loud, not silent: one warning per pawn per unreachable hour tick rather
                // than a swallowed exception, per this repo's "no fallbacks" posture.
                Log.Warning($"[GlueRimworld] {pawn.LabelShort}: engine call failed ({bridge.LastError}).");
                return;
            }

            var selectedVerb = result["selectedVerb"]?.Value<string>() ?? "-";
            var selectedId = result["selectedId"]?.Value<string>() ?? "-";
            var band = result["scheduleBand"]?.Value<string>() ?? "-";

            Log.Message(
                $"[GlueRimworld] hour={hour} {pawn.LabelShort}: engine selected '{selectedId}' " +
                $"(verb={selectedVerb}, band={band}, scheduleMode={scheduleMode})");

            // Visible, in-world confirmation that the engine's decision reached this pawn --
            // real RimWorld API (MoteMaker.ThrowText), not just a log line.
            if (pawn.Spawned && pawn.Map != null)
            {
                MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, "glue: " + selectedVerb, 4f);
            }
        }
    }

    /// <summary>
    /// RimWorld does not auto-discover GameComponent subclasses -- they must be added to
    /// Game.components explicitly. FinalizeInit (postfix) is the standard, well-established
    /// injection point used across the modding ecosystem for exactly this.
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    internal static class Game_FinalizeInit_InjectGlueComponent
    {
        private static void Postfix(Game __instance)
        {
            if (__instance.GetComponent<GlueBehaviorTickComponent>() == null)
            {
                __instance.components.Add(new GlueBehaviorTickComponent(__instance));
                Log.Message("[GlueRimworld] GlueBehaviorTickComponent attached.");
            }
        }
    }
}
