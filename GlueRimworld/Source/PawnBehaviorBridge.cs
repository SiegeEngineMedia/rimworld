using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

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
    /// floating mote plus a log line. Native execution is opt-in and only attempts a declarative
    /// candidate mapping when RimWorld reports the pawn is free; existing jobs are never
    /// interrupted, and every terminal native outcome is written through the shared receipt
    /// workflow.
    /// </summary>
    public sealed class GlueBehaviorTickComponent : GameComponent
    {
        // The shared day-tick route owns the status, arbitration, affordance, receipt, and
        // readback pipeline. RimWorld supplies its own declarative candidate pack through the
        // seed catalog, so the same embedded editor/runtime surface can change content without
        // changing this adapter's route or the Zomboid consumer's route.
        private const string DayTickTemplate = "actor/runtime/zomboid-day-tick";
        private const string RimworldActionKind = "rimworld-day-tick";
        private const string RimworldReceiptKeyPrefix = "workflow-state/actor-execution-receipt/rimworld-day/";
        private const string MountHeartbeatTemplate = "actor/runtime/rimworld-session-mount-heartbeat";
        private const string MountPolicyRef = "lens://rimworld";
        private const string MountWorldRefPrefix = "rimworld-map-";
        private const string NativeReceiptTemplate = "workflow/actors/persist-actor-execution-receipt";
        private const long NativeReceiptEpochMs = 1735689600000L;
        private const int MaxConcurrentRequests = 2;
        private static bool LifecycleProbeEnabled =>
            string.Equals(Environment.GetEnvironmentVariable("GLUE_RIMWORLD_LIFECYCLE_PROBE"), "1", StringComparison.Ordinal);
        private static bool LifecycleLoadEnabled =>
            string.Equals(Environment.GetEnvironmentVariable("GLUE_RIMWORLD_LIFECYCLE_LOAD"), "1", StringComparison.Ordinal);
        private static string LifecycleSaveName =>
            Environment.GetEnvironmentVariable("GLUE_RIMWORLD_LIFECYCLE_SAVE_NAME") ?? "glue-lifecycle-probe";
        private static bool NativeJobsEnabled =>
            string.Equals(Environment.GetEnvironmentVariable("GLUE_RIMWORLD_ENABLE_NATIVE_JOBS"), "1", StringComparison.Ordinal);
        private static readonly Dictionary<int, PendingPawnCall> PendingCalls = new Dictionary<int, PendingPawnCall>();
        private static readonly Dictionary<int, NativeIntent> PendingNativeIntents = new Dictionary<int, NativeIntent>();
        private static readonly Dictionary<string, PendingNativeReceipt> PendingNativeReceipts = new Dictionary<string, PendingNativeReceipt>();
        private static FieldInfo? _jobTrackerPawnField;
        private static bool _lifecycleLoadIssued;
        private bool _lifecycleSaveRequested;
        private static int _hookAttemptCount;
        private static int _hookFiredCount;
        private static int _hookNotFiredCount;
        private static int _lastAttemptTick = -1;
        private static int _lastMapId = int.MinValue;
        private static string _lastHookState = "not-fired: component has not reached an hourly boundary";
        private Task<bool>? _bridgeLivenessRequest;
        private bool _bridgeLivenessReported;
        private PendingMountCall? _pendingMountHeartbeat;
        private PendingMountCall? _pendingMountProjection;
        private string _mountSessionKey = "";
        private int _mountWorldRevision;
        private int _lastMountHeartbeatTick = -1;
        private int _mountRequestedTick = -1;
        private string _lastMountState = "not-fired: mount heartbeat has not run";

        public GlueBehaviorTickComponent(Game game)
        {
        }

        public override void ExposeData()
        {
            // Preserve the mount epoch in RimWorld's own save stream. LoadedGame then
            // advances it before the first post-load heartbeat, so a remounted native map
            // cannot be mistaken for the pre-load projection even though its stable session
            // key remains rimworld-colony-<mapId>.
            Scribe_Values.Look(ref _mountWorldRevision, "glueMountWorldRevision", 0);
        }

        public static int HookAttemptCount => _hookAttemptCount;
        public static int HookFiredCount => _hookFiredCount;
        public static int HookNotFiredCount => _hookNotFiredCount;
        public static int LastAttemptTick => _lastAttemptTick;
        public static string LastHookState => _lastHookState;
        public static int PendingRequestCount => PendingCalls.Count + PendingNativeIntents.Count + PendingNativeReceipts.Count;
        internal static bool HasPendingNativeIntents => PendingNativeIntents.Count > 0;

        internal static bool NativeIntentMayReplaceVanillaJob(Pawn pawn)
        {
            return PendingNativeIntents.TryGetValue(pawn.thingIDNumber, out var intent) &&
                string.Equals(
                    NativeMappingFor(intent.SelectedId)?["priorityPolicy"]?.Value<string>(),
                    "replace-free-job",
                    StringComparison.Ordinal);
        }

        public override void StartedNewGame()
        {
            ResetRuntimeState("new game");
        }

        public override void LoadedGame()
        {
            ResetRuntimeState("save loaded");
            if (LifecycleLoadEnabled)
                Log.Message("[GlueRimworld] lifecycle LoadedGame hook observed; runtime state reset.");
        }

        public override void GameComponentTick()
        {
            var ticksGame = Find.TickManager.TicksGame;

            CompletePendingCalls();
            CompletePendingNativeReceipts();
            CompletePendingMountCalls();
            TryLoadLifecycleProbe();

            var bridge = GlueRimworldMod.Bridge;
            CheckBridgeLiveness(bridge);

            if (bridge != null && Find.CurrentMap != null)
                TryQueueRequestedMount(Find.CurrentMap, bridge);

            if (ticksGame % GenDate.TicksPerHour != 0) return;

            _lastAttemptTick = ticksGame;
            _hookAttemptCount++;

            var seeds = GlueRimworldMod.Seeds;
            if (bridge == null || seeds == null)
            {
                MarkNotFired("bridge or seed catalog unavailable");
                return;
            }

            var map = Find.CurrentMap;
            if (map == null)
            {
                MarkNotFired("no current map");
                return;
            }

            if (_lastMapId != map.uniqueID)
            {
                if (_lastMapId != int.MinValue)
                {
                    ResetPendingCalls($"map changed from {_lastMapId} to {map.uniqueID}");
                    ResetMountRequests($"map changed from {_lastMapId} to {map.uniqueID}");
                    _mountWorldRevision++;
                }
                _lastMapId = map.uniqueID;
                _mountSessionKey = "rimworld-colony-" + map.uniqueID;
            }

            _mountRequestedTick = ticksGame;

            foreach (var pawn in map.mapPawns.FreeColonistsSpawned.ToList())
            {
                QueuePawn(pawn, bridge, seeds, ticksGame);
            }

            TryQueueRequestedMount(map, bridge);

            _hookFiredCount++;
            _lastHookState = "fired: queued pawn observation requests";
        }

        private static void QueuePawn(Pawn pawn, GlueBridgeClient bridge, RimworldSeedCatalog seeds, int ticksGame)
        {
            var map = pawn.Map;
            if (map == null)
            {
                MarkNotFired($"pawn {pawn.thingIDNumber} has no map");
                return;
            }

            var pawnId = pawn.thingIDNumber;
            if (PendingCalls.ContainsKey(pawnId))
            {
                MarkNotFired($"pawn {pawnId} still has an in-flight request");
                return;
            }

            if (PendingCalls.Count >= MaxConcurrentRequests)
            {
                MarkNotFired($"concurrency cap {MaxConcurrentRequests} reached");
                return;
            }

            var hour = GenLocalDate.HourOfDay(pawn);

            var values = new JObject
            {
                ["hunger"] = pawn.needs?.food?.CurLevel ?? 1.0,
                ["energy"] = pawn.needs?.rest?.CurLevel ?? 1.0,
                ["recreation"] = pawn.needs?.joy?.CurLevel ?? 1.0,
                // These are relational affordance signals rather than vanilla NeedDefs:
                // social availability is derived from nearby free colonists, while safety
                // is derived from RimWorld's active-hostile-threat query.
                ["social"] = SocialAvailability(map, pawn),
                ["safety"] = SafetyAvailability(map, pawn)
            };

            var rates = new JObject
            {
                ["hunger"] = seeds.NeedDecayRates.TryGetValue("hunger", out var hr) ? hr : 0.04,
                ["energy"] = seeds.NeedDecayRates.TryGetValue("energy", out var er) ? er : 0.03,
                ["recreation"] = seeds.NeedDecayRates.TryGetValue("recreation", out var rr) ? rr : 0.02
            };

            var assignmentDefName = pawn.timetable?.CurrentAssignment?.defName ?? "Anything";
            var scheduleMode = seeds.GlueScheduleModeFor(assignmentDefName);
            var mealTarget = FindEdibleMeal(pawn);
            var hasRestingSpot = pawn.ownership?.OwnedBed != null;
            var daylight = GenCelestial.IsDaytime(GenCelestial.CurCelestialSunGlow(map));
            var hasKitchen = HasKitchen(map);
            var hasDesignatedHaul = map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.Haul);
            var constructionTarget = FindConstructionTarget(pawn);

            var worldFacts = new JObject
            {
                // Real signals: RimWorld's own celestial glow and meal-source building
                // definitions. The adapter exposes facts; the declarative pack decides
                // whether a candidate should use them.
                ["daylight"] = daylight ? 1 : 0,
                ["scheduleAssignment"] = assignmentDefName,
                ["scheduleMode"] = scheduleMode,
                ["has-edible-meal"] = mealTarget == null ? 0 : 1,
                ["has-resting-spot"] = hasRestingSpot ? 1 : 0,
                ["has-kitchen"] = hasKitchen ? 1 : 0,
                // Vanilla RimWorld has no car system; these remain explicit unsupported
                // facts until a runtime-specific vehicle affordance supplies them.
                ["has-garage"] = 0,
                ["has-car-damage"] = 0,
                ["has-designated-haul"] = hasDesignatedHaul ? 1 : 0,
                // Only an actionable native Frame counts here. The WorkGiver remains the
                // authority for blueprint/material legality, reservation, reachability,
                // and the final Job instance.
                ["has-designated-construction"] = constructionTarget == null ? 0 : 1
            };
            AddSkillFacts(pawn, seeds, worldFacts);

            var args = new JObject
            {
                ["hour"] = hour,
                ["actorId"] = "rimworld-pawn-" + pawn.thingIDNumber,
                ["values"] = values,
                ["rates"] = rates,
                ["worldFacts"] = worldFacts,
                ["observers"] = new JArray(),
                ["detectionRange"] = 5,
                ["candidatePack"] = FilterCandidatePack(seeds.CandidatePack, worldFacts),
                ["actionKind"] = RimworldActionKind,
                ["receiptKeyPrefix"] = RimworldReceiptKeyPrefix
            };

            var cancellation = new CancellationTokenSource();
            PendingCalls[pawnId] = new PendingPawnCall
            {
                Pawn = pawn,
                AttemptTick = ticksGame,
                ActorId = "rimworld-pawn-" + pawnId,
                Hour = hour,
                ScheduleMode = scheduleMode,
                Cancellation = cancellation,
                Request = bridge.ExecuteAsync(DayTickTemplate, args, cancellation.Token)
            };
        }

        private static void CompletePendingCalls()
        {
            foreach (var pair in PendingCalls.ToList())
            {
                var pending = pair.Value;
                if (!pending.Request.IsCompleted) continue;

                PendingCalls.Remove(pair.Key);
                pending.Cancellation.Dispose();
                GlueBridgeResult response;
                try
                {
                    // Unity's embedded Mono can load Task<T> but fails to resolve the
                    // generated TaskAwaiter<T> field when this netstandard2.1 mod is
                    // invoked from a GameComponent tick. The request is already
                    // complete here, so read Result directly and keep the async HTTP
                    // work, cancellation, and game-thread completion boundary intact.
                    response = pending.Request.Result;
                }
                catch (Exception ex)
                {
                    response = GlueBridgeResult.Fail(ex.Message);
                }

                if (!response.Succeeded || response.Payload == null)
                {
                    MarkNotFired($"pawn {pending.ActorId}: {response.Error ?? "request faulted"}");
                    Log.Warning($"[GlueRimworld] pawn={pending.ActorId}: engine call failed ({response.Error ?? "request faulted"}).");
                    continue;
                }

                ApplyEngineResult(pending.Pawn, response.Payload, pending.Hour, pending.ScheduleMode);
            }
        }

        private static void ApplyEngineResult(Pawn pawn, JObject result, int hour, string scheduleMode)
        {
            _lastHookState = "fired: consumed engine result";

            var selectedVerb = result["selectedVerb"]?.Value<string>() ?? "-";
            var selectedId = result["selectedId"]?.Value<string>() ?? "-";
            var band = result["scheduleBand"]?.Value<string>() ?? "-";
            var receiptVerified = result["receiptVerified"]?.Value<bool>() ?? false;
            var receiptStorageKey = result["receiptStorageKey"]?.Value<string>() ?? "-";
            var sourceReceiptKey = receiptStorageKey == "-" ? "" : receiptStorageKey;
            var nativeJobStatus = QueueNativeIntent(pawn, selectedId, hour, sourceReceiptKey);

            Log.Message(
                $"[GlueRimworld] hour={hour} {pawn.LabelShort}: engine selected '{selectedId}' " +
                $"(verb={selectedVerb}, band={band}, scheduleMode={scheduleMode}, " +
                $"receiptVerified={receiptVerified}, receiptKey={receiptStorageKey}, " +
                $"nativeJob={nativeJobStatus})");

            // Visible, in-world confirmation that the engine's decision reached this pawn --
            // real RimWorld API (MoteMaker.ThrowText), not just a log line.
            if (pawn.Spawned && pawn.Map != null)
            {
                MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, "glue: " + selectedVerb, 4f);
            }
        }

        private void QueueMountHeartbeat(Map map, GlueBridgeClient bridge, int ticksGame)
        {
            if (_pendingMountHeartbeat != null || _pendingMountProjection != null) return;
            if (_lastMountHeartbeatTick == ticksGame) return;
            if (string.IsNullOrWhiteSpace(_mountSessionKey)) return;

            var observedAt = NativeReceiptEpochMs + (long)ticksGame * 400L;
            var envelope = BuildMountEnvelope(map, ticksGame, observedAt);
            var rendererArgs = BuildMountRendererArgs(map, ticksGame, observedAt, envelope);
            var args = new JObject
            {
                ["sessionKey"] = _mountSessionKey,
                ["worldRef"] = MountWorldRefPrefix + map.uniqueID,
                ["worldRevision"] = _mountWorldRevision,
                ["observedAt"] = observedAt,
                ["mountPolicyRef"] = MountPolicyRef,
                ["activeProjection"] = "rings",
                ["principal"] = rendererArgs["principal"]!.DeepClone(),
                ["mountedLenses"] = rendererArgs["mountedLenses"]!.DeepClone(),
                ["mountDecisions"] = rendererArgs["mountDecisions"]!.DeepClone(),
                ["actions"] = rendererArgs["actions"]!.DeepClone(),
                ["envelope"] = envelope
            };

            var cancellation = new CancellationTokenSource();
            _pendingMountHeartbeat = new PendingMountCall
            {
                SessionKey = _mountSessionKey,
                WorldRevision = _mountWorldRevision,
                AttemptTick = ticksGame,
                Args = args,
                RendererArgs = rendererArgs,
                Cancellation = cancellation,
                Request = bridge.ExecuteAsync(MountHeartbeatTemplate, args, cancellation.Token)
            };
            _lastMountHeartbeatTick = ticksGame;
            _lastMountState = "pending: native envelope heartbeat";
        }

        private void TryQueueRequestedMount(Map map, GlueBridgeClient bridge)
        {
            if (_mountRequestedTick < 0) return;
            if (_pendingMountHeartbeat != null || _pendingMountProjection != null) return;
            if (PendingCalls.Count > 0) return;

            var requestedTick = _mountRequestedTick;
            _mountRequestedTick = -1;
            QueueMountHeartbeat(map, bridge, requestedTick);
        }

        private JObject BuildMountEnvelope(Map map, int ticksGame, long observedAt)
        {
            var actors = new JArray();
            foreach (var pawn in map.mapPawns.FreeColonistsSpawned.Take(16))
            {
                actors.Add(new JObject
                {
                    ["actorId"] = "rimworld-pawn-" + pawn.thingIDNumber,
                    ["label"] = pawn.LabelShort,
                    ["mapId"] = map.uniqueID,
                    ["hunger"] = pawn.needs?.food?.CurLevel ?? 1.0,
                    ["rest"] = pawn.needs?.rest?.CurLevel ?? 1.0,
                    ["jobDef"] = pawn.CurJob?.def?.defName ?? ""
                });
            }

            var freshness = new JObject
            {
                ["valid"] = true,
                ["status"] = "fresh",
                ["source"] = "rimworld-native-game-component",
                ["freshness"] = "native-hourly",
                ["observedAt"] = observedAt,
                ["hookState"] = _lastHookState,
                ["hookAttemptCount"] = _hookAttemptCount,
                ["hookFiredCount"] = _hookFiredCount,
                ["pendingRequestCount"] = PendingRequestCount
            };

            return new JObject
            {
                ["$schema"] = "definition://session-envelope",
                ["currentSession"] = new JObject
                {
                    ["sessionId"] = _mountSessionKey,
                    ["revision"] = _mountWorldRevision,
                    ["worldRef"] = MountWorldRefPrefix + map.uniqueID
                },
                ["worldProjection"] = new JObject
                {
                    ["runtime"] = "rimworld",
                    ["mapId"] = map.uniqueID,
                    ["tick"] = ticksGame,
                    ["pawnCount"] = map.mapPawns.FreeColonistsSpawned.Count,
                    ["actors"] = actors.DeepClone()
                },
                ["worldProjectionFreshness"] = freshness.DeepClone(),
                ["controllerView"] = new JObject
                {
                    ["revision"] = _mountWorldRevision,
                    ["actors"] = actors
                },
                ["controllerViewFreshness"] = new JObject
                {
                    ["valid"] = true,
                    ["status"] = "fresh",
                    ["sessionRevision"] = _mountWorldRevision,
                    ["viewRevision"] = _mountWorldRevision,
                    ["sourceRevision"] = _mountWorldRevision,
                    ["freshness"] = "native-hourly",
                    ["diagnosticCode"] = JValue.CreateNull(),
                    ["nextAction"] = "none"
                },
                ["worldObservability"] = freshness,
                ["uiAccessibility"] = RimworldUIAccessibilityProjection.BuildSnapshot(
                    map, ticksGame, _mountSessionKey, _mountWorldRevision)
            };
        }

        private JObject BuildMountRendererArgs(Map map, int ticksGame, long observedAt, JObject envelope)
        {
            var actorRefs = new JArray(
                map.mapPawns.FreeColonistsSpawned.Take(16)
                    .Select(pawn => new JValue("rimworld-pawn-" + pawn.thingIDNumber)));
            var timeRecord = new JObject
            {
                ["at"] = observedAt,
                ["tick"] = ticksGame,
                ["worldRef"] = MountWorldRefPrefix + map.uniqueID,
                ["worldRevision"] = _mountWorldRevision
            };

            return new JObject
            {
                ["sessionKey"] = _mountSessionKey,
                ["activeProjection"] = "rings",
                ["selectedPath"] = "",
                ["worldRef"] = MountWorldRefPrefix + map.uniqueID,
                ["worldRevision"] = _mountWorldRevision,
                ["mountPolicyRef"] = MountPolicyRef,
                ["principal"] = new JObject
                {
                    ["runtime"] = "rimworld",
                    ["mapId"] = map.uniqueID,
                    ["authority"] = "native-rimworld"
                },
                ["selectedRefs"] = actorRefs,
                ["mountedLenses"] = new JArray(MountPolicyRef),
                ["mountDecisions"] = new JArray
                {
                    new JObject
                    {
                        ["lensRef"] = MountPolicyRef,
                        ["state"] = "mounted",
                        ["authority"] = "native-rimworld",
                        ["worldRevision"] = _mountWorldRevision
                    }
                },
                ["panels"] = new JArray
                {
                    new JObject
                    {
                        ["panelKey"] = "rimworld-colony",
                        ["title"] = "RimWorld Colony",
                        ["status"] = "native-live"
                    }
                },
                ["scene"] = new JObject
                {
                    ["runtime"] = "rimworld",
                    ["mapId"] = map.uniqueID,
                    ["tick"] = ticksGame
                },
                ["actions"] = new JArray("refresh", "inspect"),
                ["diagnostics"] = new JArray
                {
                    new JObject
                    {
                        ["code"] = "rimworld.native.mount",
                        ["severity"] = "info",
                        ["message"] = "Native RimWorld map facts are authoritative; Glue projects them."
                    }
                },
                ["accessibilitySnapshot"] = RimworldUIAccessibilityProjection.BuildSnapshot(
                    map, ticksGame, _mountSessionKey, _mountWorldRevision),
                ["rings"] = new JObject
                {
                    ["contractRef"] = "definition://rings",
                    ["records"] = envelope["controllerView"]?["actors"]?.DeepClone() ?? new JArray()
                },
                ["timeStream"] = new JObject
                {
                    ["contractRef"] = "definition://temporal-projection-record",
                    ["records"] = new JArray(timeRecord)
                },
                ["nextLens"] = JValue.CreateNull()
            };
        }

        private void CompletePendingMountCalls()
        {
            if (_pendingMountHeartbeat != null && _pendingMountHeartbeat.Request.IsCompleted)
            {
                var pending = _pendingMountHeartbeat;
                _pendingMountHeartbeat = null;
                pending.Cancellation.Dispose();
                var response = ReadCompletedResult(pending.Request);
                if (!response.Succeeded || response.Payload == null)
                {
                    _lastMountState = "not-fired: heartbeat failed";
                    Log.Warning($"[GlueRimworld] mount heartbeat failed session={pending.SessionKey} revision={pending.WorldRevision}: {response.Error ?? "request faulted"}.");
                }
                else
                {
                    var output = response.Payload["output"] as JObject ?? response.Payload;
                    var readback = output["readback"] as JObject;
                    var readbackKey = readback?["sessionKey"]?.Value<string>() ?? "-";
                    _lastMountState = "fired: heartbeat persisted/read back";
                    Log.Message($"[GlueRimworld] mount heartbeat session={pending.SessionKey} revision={pending.WorldRevision} persisted={output["persisted"] != null} readbackSession={readbackKey}.");

                    var bridge = GlueRimworldMod.Bridge;
                    if (bridge != null)
                    {
                        var cancellation = new CancellationTokenSource();
                        _pendingMountProjection = new PendingMountCall
                        {
                            SessionKey = pending.SessionKey,
                            WorldRevision = pending.WorldRevision,
                            AttemptTick = pending.AttemptTick,
                            Args = pending.RendererArgs,
                            RendererArgs = pending.RendererArgs,
                            Cancellation = cancellation,
                            Request = bridge.ExecuteAsync(CanonicalLensReceiptClient.TemplateKey, pending.RendererArgs, cancellation.Token)
                        };
                    }
                }
            }

            if (_pendingMountProjection == null || !_pendingMountProjection.Request.IsCompleted) return;

            var projection = _pendingMountProjection;
            _pendingMountProjection = null;
            projection.Cancellation.Dispose();
            var projectionResponse = ReadCompletedResult(projection.Request);
            if (!projectionResponse.Succeeded || projectionResponse.Payload == null)
            {
                Log.Warning($"[GlueRimworld] canonical mount projection failed session={projection.SessionKey} revision={projection.WorldRevision}: {projectionResponse.Error ?? "request faulted"}.");
                return;
            }

            var projectionOutput = projectionResponse.Payload["output"] as JObject ?? projectionResponse.Payload;
            Log.Message(
                $"[GlueRimworld] canonical mount projection session={projection.SessionKey} " +
                $"revision={projection.WorldRevision} schema={projectionOutput["$schema"]?.Value<string>() ?? "-"}.");
        }

        private static GlueBridgeResult ReadCompletedResult(Task<GlueBridgeResult> request)
        {
            try
            {
                return request.Result;
            }
            catch (Exception ex)
            {
                return GlueBridgeResult.Fail(ex.Message);
            }
        }

        private static void MarkNotFired(string reason)
        {
            _hookNotFiredCount++;
            _lastHookState = "not-fired: " + reason;
        }

        private static JArray FilterCandidatePack(JArray candidates, JObject worldFacts)
        {
            var eligible = new JArray();
            foreach (var token in candidates)
            {
                if (token is not JObject candidate) continue;
                var requirements = candidate["requires"] as JArray;
                var satisfiesRequirements = requirements == null || requirements.All(requirement =>
                {
                    var key = requirement.Value<string>();
                    return !string.IsNullOrEmpty(key) && (worldFacts[key!]?.Value<double>() ?? 0) > 0;
                });
                if (satisfiesRequirements)
                    eligible.Add(candidate.DeepClone());
            }
            return eligible;
        }

        private static Thing? FindEdibleMeal(Pawn pawn)
        {
            if (pawn.Map == null) return null;
            foreach (var thing in pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree))
            {
                if (!thing.Spawned || thing.def.ingestible == null) continue;
                if (!thing.def.ingestible.foodType.HasFlag(FoodTypeFlags.Meal)) continue;
                if (!FoodUtility.FoodIsSuitable(pawn, thing.def)) continue;
                if (!ReachabilityUtility.CanReach(pawn, thing, PathEndMode.ClosestTouch, Danger.Some, false, false, TraverseMode.ByPawn)) continue;
                return thing;
            }
            return null;
        }

        private static bool HasKitchen(Map map)
        {
            return map.listerBuildings.AllColonistBuildingsOfType<Building>().Any(building =>
                building.Spawned && building.def.building?.isMealSource == true);
        }

        private static Frame? FindConstructionTarget(Pawn pawn)
        {
            if (pawn.Map == null) return null;
            return pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Construction)
                .OfType<Frame>()
                .FirstOrDefault(frame => frame.Spawned);
        }

        private static double SocialAvailability(Map map, Pawn pawn)
        {
            var nearbyColonists = map.mapPawns.FreeColonistsSpawned.Count(other =>
                other != pawn && other.Spawned && pawn.Position.DistanceToSquared(other.Position) <= 144);
            return Math.Max(0.0, Math.Min(1.0, nearbyColonists / 2.0));
        }

        private static double SafetyAvailability(Map map, Pawn pawn)
        {
            var faction = pawn.Faction;
            if (faction == null) return 0.0;
            return GenHostility.AnyHostileActiveThreatTo(map, faction, false, false) ? 0.0 : 1.0;
        }

        private static void AddSkillFacts(Pawn pawn, RimworldSeedCatalog seeds, JObject worldFacts)
        {
            if (pawn.skills == null) return;

            foreach (var skill in seeds.Skills.Values)
            {
                var defName = skill["defName"]?.Value<string>();
                var key = skill["key"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(defName) || string.IsNullOrWhiteSpace(key)) continue;

                var skillDef = DefDatabase<SkillDef>.GetNamedSilentFail(defName);
                var record = skillDef == null ? null : pawn.skills.GetSkill(skillDef);
                if (record == null) continue;

                var normalizedLevel = Math.Max(0.0, Math.Min(1.0, record.Level / 20.0));
                worldFacts["skill." + key] = record.TotallyDisabled ? 0.0 : normalizedLevel;
                worldFacts["skill." + key + ".level"] = record.Level;
                worldFacts["skill." + key + ".disabled"] = record.TotallyDisabled ? 1 : 0;
            }
        }

        private static string QueueNativeIntent(Pawn pawn, string selectedId, int hour, string sourceReceiptKey)
        {
            if (!NativeJobsEnabled) return "disabled";
            var mapping = NativeMappingFor(selectedId);
            if (mapping == null) return "not-applicable";

            PendingNativeIntents[pawn.thingIDNumber] = new NativeIntent
            {
                Pawn = pawn,
                ActorId = "rimworld-pawn-" + pawn.thingIDNumber,
                SelectedId = selectedId,
                Hour = hour,
                AttemptTick = Find.TickManager.TicksGame,
                SourceReceiptKey = sourceReceiptKey
            };
            if (pawn.CurJob != null) return "queued:pawn-busy";
            return NativeIntentMayReplaceVanillaJob(pawn)
                ? "queued:replace-free-job"
                : "queued:job-giver";
        }

        private static JObject? NativeMappingFor(string selectedId)
        {
            return GlueRimworldMod.Seeds?.NativeJobMappings.TryGetValue(selectedId, out var selectedMapping) == true
                ? selectedMapping
                : null;
        }

        private static string TryBuildNativeJob(Pawn pawn, string selectedId, out Job? nativeJob)
        {
            nativeJob = null;
            if (!NativeJobsEnabled) return "disabled";
            var mapping = NativeMappingFor(selectedId);
            if (mapping == null) return "not-applicable";
            if (string.Equals(mapping["admission"]?.Value<string>(), "try-jobgiver", StringComparison.Ordinal))
                return TryBuildNativeJobGiver(pawn, selectedId, mapping, out nativeJob);
            if (!string.Equals(mapping["admission"]?.Value<string>(), "try-ordered-job", StringComparison.Ordinal))
            {
                if (!string.Equals(mapping["admission"]?.Value<string>(), "try-workgiver-job", StringComparison.Ordinal))
                    return "rejected:unsupported-admission";
                return TryBuildNativeWorkGiverJob(pawn, selectedId, mapping, out nativeJob);
            }
            if (pawn.Map == null || pawn.jobs == null) return "rejected:no-job-system";
            if (pawn.CurJob != null) return "rejected:pawn-busy";
            if (!string.Equals(mapping["targetKind"]?.Value<string>(), "edible-meal", StringComparison.Ordinal))
                return "rejected:unsupported-target";

            var meal = FindEdibleMeal(pawn);
            if (meal == null) return "rejected:no-native-meal-target";

            var jobDefName = mapping["jobDef"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(jobDefName)) return "rejected:missing-job-def";
            var jobDef = DefDatabase<JobDef>.GetNamedSilentFail(jobDefName);
            if (jobDef == null) return "rejected:unknown-job-def";

            nativeJob = JobMaker.MakeJob(jobDef, meal);
            return "admitted:" + selectedId;
        }

        private static string TryBuildNativeJobGiver(
            Pawn pawn,
            string selectedId,
            JObject mapping,
            out Job? nativeJob)
        {
            nativeJob = null;
            if (pawn.Map == null || pawn.jobs == null) return "rejected:no-job-system";
            if (pawn.CurJob != null) return "rejected:pawn-busy";

            var typeName = mapping["jobGiverType"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(typeName)) return "rejected:missing-jobgiver-type";

            var jobGiverType = typeof(Pawn).Assembly.GetType(typeName, throwOnError: false);
            if (jobGiverType == null) return "rejected:unknown-jobgiver-type";

            object? jobGiver;
            try
            {
                jobGiver = Activator.CreateInstance(jobGiverType);
            }
            catch
            {
                return "rejected:jobgiver-construction-failed";
            }

            if (jobGiver == null) return "rejected:jobgiver-construction-failed";

            var tryGiveJob = jobGiverType.GetMethod(
                "TryGiveJob",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(Pawn) },
                modifiers: null);
            if (tryGiveJob == null) return "rejected:jobgiver-method-missing";

            try
            {
                nativeJob = tryGiveJob.Invoke(jobGiver, new object[] { pawn }) as Job;
            }
            catch
            {
                nativeJob = null;
                return "rejected:jobgiver-threw";
            }

            return nativeJob == null
                ? "rejected:jobgiver-no-legal-job"
                : "admitted:" + selectedId;
        }

        private static string TryBuildNativeWorkGiverJob(
            Pawn pawn,
            string selectedId,
            JObject mapping,
            out Job? nativeJob)
        {
            nativeJob = null;
            if (pawn.Map == null || pawn.jobs == null) return "rejected:no-job-system";
            if (pawn.CurJob != null) return "rejected:pawn-busy";
            if (!string.Equals(mapping["targetKind"]?.Value<string>(), "construction-frame", StringComparison.Ordinal))
                return "rejected:unsupported-target";

            var workGiverDefName = mapping["workGiverDef"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(workGiverDefName)) return "rejected:missing-workgiver-def";
            var workGiverDef = DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiverDefName);
            if (workGiverDef == null) return "rejected:unknown-workgiver-def";
            if (workGiverDef.Worker is not WorkGiver_Scanner scanner)
                return "rejected:workgiver-not-scanner";

            foreach (var target in pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Construction)
                .OfType<Frame>()
                .Where(frame => frame.Spawned))
            {
                if (!scanner.HasJobOnThing(pawn, target, false)) continue;
                var job = scanner.JobOnThing(pawn, target, false);
                if (job == null) continue;
                nativeJob = job;
                return "admitted:" + selectedId;
            }

            return "rejected:workgiver-no-legal-target";
        }

        private static FieldInfo? FindInstanceField(Type type, string name)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var field = current.GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }

        internal static Job? TryIssueNativeJob(Pawn pawn)
        {
            if (!PendingNativeIntents.TryGetValue(pawn.thingIDNumber, out var intent)) return null;
            if (pawn.CurJob != null) return null;

            var status = TryBuildNativeJob(pawn, intent.SelectedId, out var nativeJob);
            if (status == "rejected:pawn-busy") return null;

            PendingNativeIntents.Remove(pawn.thingIDNumber);
            QueueNativeOutcomeReceipt(intent, status);
            Log.Message(
                $"[GlueRimworld] native JobGiver actor={intent.ActorId} " +
                $"selected='{intent.SelectedId}' status={status}.");
            return nativeJob;
        }

        internal static Pawn? PawnForJobTracker(Pawn_JobTracker tracker)
        {
            _jobTrackerPawnField ??= FindInstanceField(tracker.GetType(), "pawn");
            return _jobTrackerPawnField?.GetValue(tracker) as Pawn;
        }

        internal static ThinkNode NativeSourceNode { get; } = new GlueNativeJobGiver();

        private static void QueueNativeOutcomeReceipt(NativeIntent intent, string nativeStatus)
        {
            var bridge = GlueRimworldMod.Bridge;
            if (!NativeJobsEnabled || bridge == null) return;

            var mapping = GlueRimworldMod.Seeds?.NativeJobMappings.TryGetValue(intent.SelectedId, out var selectedMapping) == true
                ? selectedMapping
                : null;
            var actionKind = mapping?["nativeReceiptKind"]?.Value<string>() ?? "rimworld-native-job-admission";
            var idempotencyKey = intent.ActorId + "-native-job-" + intent.AttemptTick;
            if (PendingNativeReceipts.ContainsKey(idempotencyKey)) return;

            var sourceRef = intent.SourceReceiptKey;
            var artifactRefs = new JArray();
            if (!string.IsNullOrWhiteSpace(sourceRef)) artifactRefs.Add(sourceRef);

            var timestamp = NativeReceiptEpochMs + (long)intent.AttemptTick * 400L;
            var receipt = new JObject
            {
                ["receiptId"] = "receipt-" + idempotencyKey,
                ["idempotencyKey"] = idempotencyKey,
                ["actorId"] = intent.ActorId,
                ["actionKind"] = actionKind,
                ["causationId"] = string.IsNullOrWhiteSpace(sourceRef) ? idempotencyKey : sourceRef,
                ["correlationId"] = intent.ActorId,
                ["status"] = nativeStatus.StartsWith("admitted:", StringComparison.Ordinal) ? "completed" : "rejected",
                ["startedAt"] = timestamp,
                ["completedAt"] = timestamp,
                ["persistenceScope"] = "local",
                ["evidenceClass"] = "executed-work",
                ["subjectRefs"] = new JArray(intent.ActorId),
                ["outcome"] = new JObject
                {
                    ["summary"] = nativeStatus,
                    ["metrics"] = new JObject
                    {
                        ["hour"] = intent.Hour,
                        ["attemptTick"] = intent.AttemptTick,
                        ["selectedId"] = intent.SelectedId,
                        ["nativeStatus"] = nativeStatus,
                        ["sourceReceiptKey"] = sourceRef
                    },
                    ["artifactRefs"] = artifactRefs
                }
            };

            var cancellation = new CancellationTokenSource();
            PendingNativeReceipts[idempotencyKey] = new PendingNativeReceipt
            {
                IdempotencyKey = idempotencyKey,
                Cancellation = cancellation,
                Request = bridge.ExecuteAsync(
                    NativeReceiptTemplate,
                    new JObject { ["receipt"] = receipt },
                    cancellation.Token)
            };
        }

        private static void CompletePendingNativeReceipts()
        {
            foreach (var pair in PendingNativeReceipts.ToList())
            {
                var pending = pair.Value;
                if (!pending.Request.IsCompleted) continue;

                PendingNativeReceipts.Remove(pair.Key);
                pending.Cancellation.Dispose();
                GlueBridgeResult response;
                try
                {
                    response = pending.Request.Result;
                }
                catch (Exception ex)
                {
                    response = GlueBridgeResult.Fail(ex.Message);
                }

                if (!response.Succeeded || response.Payload == null)
                {
                    Log.Warning(
                        $"[GlueRimworld] native receipt persistence failed for {pending.IdempotencyKey}: " +
                        (response.Error ?? "request faulted"));
                    continue;
                }

                var verified = response.Payload["verified"]?.Value<bool>() ?? false;
                var storageKey = response.Payload["storageKey"]?.Value<string>() ?? "-";
                Log.Message(
                    $"[GlueRimworld] native receipt idempotencyKey={pending.IdempotencyKey} " +
                    $"verified={verified} storageKey={storageKey}.");
                if (verified) RequestLifecycleSave();
            }
        }

        private static void RequestLifecycleSave()
        {
            var component = Current.Game?.GetComponent<GlueBehaviorTickComponent>();
            if (component == null || !LifecycleProbeEnabled || component._lifecycleSaveRequested) return;
            if (!GameDataSaveLoader.CurrentGameStateIsValuable) return;

            component._lifecycleSaveRequested = true;
            try
            {
                GameDataSaveLoader.SaveGame(LifecycleSaveName);
                Log.Message(
                    $"[GlueRimworld] lifecycle save requested name={LifecycleSaveName} " +
                    $"path={GenFilePaths.FilePathForSavedGame(LifecycleSaveName)}.");
            }
            catch (Exception ex)
            {
                component._lifecycleSaveRequested = false;
                Log.Warning($"[GlueRimworld] lifecycle save failed: {ex.Message}");
            }
        }

        private static void TryLoadLifecycleProbe()
        {
            if (!LifecycleLoadEnabled || _lifecycleLoadIssued) return;

            var path = GenFilePaths.FilePathForSavedGame(LifecycleSaveName);
            if (!File.Exists(path)) return;

            _lifecycleLoadIssued = true;
            try
            {
                Log.Message($"[GlueRimworld] lifecycle load requested path={path}.");
                GameDataSaveLoader.CheckVersionAndLoadGame(LifecycleSaveName);
            }
            catch (Exception ex)
            {
                _lifecycleLoadIssued = false;
                Log.Warning($"[GlueRimworld] lifecycle load failed: {ex.Message}");
            }
        }

        private static void ResetPendingCalls(string reason)
        {
            if (PendingCalls.Count > 0)
                Log.Message($"[GlueRimworld] discarded {PendingCalls.Count} in-flight request(s): {reason}.");
            foreach (var pending in PendingCalls.Values)
            {
                pending.Cancellation.Cancel();
                pending.Cancellation.Dispose();
            }
            PendingCalls.Clear();
            foreach (var pending in PendingNativeReceipts.Values)
            {
                pending.Cancellation.Cancel();
                pending.Cancellation.Dispose();
            }
            PendingNativeReceipts.Clear();
            PendingNativeIntents.Clear();
            MarkNotFired(reason);
        }

        private void ResetMountRequests(string reason)
        {
            if (_pendingMountHeartbeat != null || _pendingMountProjection != null)
                Log.Message($"[GlueRimworld] discarded pending mount request(s): {reason}.");

            if (_pendingMountHeartbeat != null)
                _pendingMountHeartbeat.Cancellation.Cancel();
            if (_pendingMountProjection != null)
                _pendingMountProjection.Cancellation.Cancel();
            _pendingMountHeartbeat?.Cancellation.Dispose();
            _pendingMountProjection?.Cancellation.Dispose();
            _pendingMountHeartbeat = null;
            _pendingMountProjection = null;
            _mountRequestedTick = -1;
            _lastMountState = "not-fired: " + reason;
        }

        private void CheckBridgeLiveness(GlueBridgeClient? bridge)
        {
            if (_bridgeLivenessReported || bridge == null) return;

            _bridgeLivenessRequest ??= bridge.PingAsync();
            if (!_bridgeLivenessRequest.IsCompleted) return;

            bool reachable;
            try
            {
                reachable = _bridgeLivenessRequest.Result;
            }
            catch
            {
                reachable = false;
            }
            ReportBridgeLiveness(reachable);
        }

        private void ReportBridgeLiveness(bool reachable)
        {
            _bridgeLivenessReported = true;
            var bridgeUrl = GlueRimworldMod.Bridge?.BaseUrl ?? "http://127.0.0.1:8765";
            if (reachable)
            {
                Log.Message($"[GlueRimworld] glue-runtime-host is reachable at {bridgeUrl}.");
                return;
            }

            Log.Warning(
                $"[GlueRimworld] glue-runtime-host not reachable at {bridgeUrl} -- " +
                "behavior-candidate calls will fail closed until it is running. From the glue " +
                "monorepo: dotnet run --project runtimes/csharp/glue-runtime-host. See NEXT_STEPS.md.");
        }

        private void ResetRuntimeState(string reason)
        {
            ResetPendingCalls(reason);
            ResetMountRequests(reason);
            _hookAttemptCount = 0;
            _hookFiredCount = 0;
            _hookNotFiredCount = 0;
            _lastAttemptTick = -1;
            _lastMapId = int.MinValue;
            _mountSessionKey = "";
            _mountWorldRevision++;
            _lastMountHeartbeatTick = -1;
            _mountRequestedTick = -1;
            _bridgeLivenessRequest = null;
            _bridgeLivenessReported = false;
        }

        private sealed class PendingPawnCall
        {
            public Pawn Pawn { get; set; } = null!;
            public Task<GlueBridgeResult> Request { get; set; } = null!;
            public CancellationTokenSource Cancellation { get; set; } = null!;
            public int AttemptTick { get; set; }
            public string ActorId { get; set; } = "";
            public int Hour { get; set; }
            public string ScheduleMode { get; set; } = "";
        }

        private sealed class NativeIntent
        {
            public Pawn Pawn { get; set; } = null!;
            public string ActorId { get; set; } = "";
            public string SelectedId { get; set; } = "";
            public int Hour { get; set; }
            public int AttemptTick { get; set; }
            public string SourceReceiptKey { get; set; } = "";
        }

        private sealed class PendingNativeReceipt
        {
            public string IdempotencyKey { get; set; } = "";
            public Task<GlueBridgeResult> Request { get; set; } = null!;
            public CancellationTokenSource Cancellation { get; set; } = null!;
        }

        private sealed class PendingMountCall
        {
            public string SessionKey { get; set; } = "";
            public int WorldRevision { get; set; }
            public int AttemptTick { get; set; }
            public JObject Args { get; set; } = new JObject();
            public JObject RendererArgs { get; set; } = new JObject();
            public Task<GlueBridgeResult> Request { get; set; } = null!;
            public CancellationTokenSource Cancellation { get; set; } = null!;
        }

        /// <summary>Source marker for a job supplied at RimWorld's DetermineNextJob boundary.</summary>
        private sealed class GlueNativeJobGiver : ThinkNode_JobGiver
        {
            protected override Job TryGiveJob(Pawn pawn) => TryIssueNativeJob(pawn)!;
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

    /// <summary>
    /// Let RimWorld's own job tracker ask for the queued Glue intent only after its normal
    /// think-tree result is empty. The tracker then owns the returned ThinkResult, driver,
    /// reservation, and job lifecycle; Glue never interrupts an existing job.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), "DetermineNextJob")]
    internal static class Pawn_JobTracker_DetermineNextJob_UseGlueIntent
    {
        private static void Postfix(Pawn_JobTracker __instance, ref ThinkResult __result)
        {
            if (!GlueBehaviorTickComponent.HasPendingNativeIntents) return;
            var pawn = GlueBehaviorTickComponent.PawnForJobTracker(__instance);
            if (pawn == null || pawn.CurJob != null) return;
            if (__result.Job != null && !GlueBehaviorTickComponent.NativeIntentMayReplaceVanillaJob(pawn)) return;

            var nativeJob = GlueBehaviorTickComponent.TryIssueNativeJob(pawn);
            if (nativeJob == null) return;

            __result = new ThinkResult(
                nativeJob,
                GlueBehaviorTickComponent.NativeSourceNode,
                JobTag.Misc,
                false);
        }
    }
}
