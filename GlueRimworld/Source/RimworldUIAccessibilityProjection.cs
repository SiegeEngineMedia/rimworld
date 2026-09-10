using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Verse;

namespace GlueRimworld
{
    /// <summary>
    /// Bounded observation of the native RimWorld UI state. This is deliberately an
    /// observation seam, not a second UI implementation: WindowStack and Selector remain
    /// authoritative, locators are snapshot-addressed, and no coordinate or OCR fallback is
    /// offered. Logical control lowering stays a separate, explicitly deferred capability.
    /// </summary>
    internal static class RimworldUIAccessibilityProjection
    {
        private const int MaxWindows = 64;

        public static JObject BuildSnapshot(Map map, int ticksGame, string sessionKey, int worldRevision)
        {
            var snapshotId = "rimworld-ui-" + map.uniqueID + "-" + worldRevision + "-" + ticksGame;
            var windows = SurveyWindows(snapshotId, ticksGame);
            var selection = SurveySelection(snapshotId, ticksGame);

            return new JObject
            {
                ["$schema"] = "definition://rimworld-ui-accessibility-snapshot",
                ["status"] = "partial-native-observation",
                ["dispatchable"] = false,
                ["sessionKey"] = sessionKey,
                ["snapshotId"] = snapshotId,
                ["nativeRevision"] = ticksGame,
                ["worldRevision"] = worldRevision,
                ["runtime"] = "rimworld",
                ["mapId"] = map.uniqueID,
                ["root"] = new JObject
                {
                    ["id"] = "rimworld-ui-session-" + sessionKey,
                    ["role"] = "application",
                    ["accessibleName"] = "RimWorld",
                    ["state"] = "native-surface-observed",
                    ["actions"] = new JArray("refresh", "inspect"),
                    ["locator"] = new JObject
                    {
                        ["kind"] = "session",
                        ["sessionKey"] = sessionKey,
                        ["snapshotId"] = snapshotId
                    },
                    ["nativeRevision"] = ticksGame,
                    ["children"] = new JArray
                    {
                        new JObject
                        {
                            ["id"] = "rimworld-ui-windows-" + snapshotId,
                            ["role"] = "region",
                            ["accessibleName"] = "Open native windows",
                            ["state"] = "observed",
                            ["actions"] = new JArray(),
                            ["locator"] = new JObject
                            {
                                ["kind"] = "window-stack",
                                ["snapshotId"] = snapshotId
                            },
                            ["nativeRevision"] = ticksGame,
                            ["children"] = windows
                        },
                        new JObject
                        {
                            ["id"] = "rimworld-ui-selection-" + snapshotId,
                            ["role"] = "region",
                            ["accessibleName"] = "Native selection",
                            ["state"] = "observed",
                            ["actions"] = new JArray(),
                            ["locator"] = new JObject
                            {
                                ["kind"] = "selection",
                                ["snapshotId"] = snapshotId
                            },
                            ["nativeRevision"] = ticksGame,
                            ["children"] = selection
                        }
                    }
                },
                ["diagnostics"] = new JArray
                {
                    new JObject
                    {
                        ["code"] = "rimworld.native.ui.survey.partial",
                        ["severity"] = "info",
                        ["message"] = "Native WindowStack and Selector state are observed; semantic control enumeration and dispatch are not live yet."
                    }
                }
            };
        }

        private static JArray SurveyWindows(string snapshotId, int nativeRevision)
        {
            var nodes = new JArray();
            var stack = Find.WindowStack;
            if (stack == null)
                return nodes;

            var windowsValue = ReadMember(stack, "Windows");
            if (!(windowsValue is IEnumerable windows))
                return nodes;

            var ordinal = 0;
            foreach (var window in windows)
            {
                if (window == null || ordinal >= MaxWindows)
                    break;

                var typeName = window.GetType().FullName ?? window.GetType().Name;
                var windowKey = "rimworld-window-" + ordinal;
                nodes.Add(new JObject
                {
                    ["id"] = windowKey + "-" + snapshotId,
                    ["role"] = "region",
                    ["accessibleName"] = "Native RimWorld window " + window.GetType().Name,
                    ["state"] = "open",
                    ["actions"] = new JArray(),
                    ["locator"] = new JObject
                    {
                        ["kind"] = "native-window",
                        ["windowType"] = typeName,
                        ["ordinal"] = ordinal,
                        ["snapshotId"] = snapshotId
                    },
                    ["nativeRevision"] = nativeRevision
                });
                ordinal++;
            }

            return nodes;
        }

        private static JArray SurveySelection(string snapshotId, int nativeRevision)
        {
            var nodes = new JArray();
            var selectedThing = Find.Selector?.SingleSelectedThing;
            if (selectedThing == null)
                return nodes;

            nodes.Add(new JObject
            {
                ["id"] = "rimworld-selection-" + selectedThing.thingIDNumber + "-" + snapshotId,
                ["role"] = "treeitem",
                ["accessibleName"] = "Selected " + selectedThing.LabelShort,
                ["state"] = "selected",
                ["actions"] = new JArray("inspect"),
                ["locator"] = new JObject
                {
                    ["kind"] = "native-thing",
                    ["thingId"] = selectedThing.thingIDNumber,
                    ["thingType"] = selectedThing.GetType().FullName ?? selectedThing.GetType().Name,
                    ["snapshotId"] = snapshotId
                },
                ["nativeRevision"] = nativeRevision
            });
            return nodes;
        }

        private static object? ReadMember(object target, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = target.GetType();
            var property = type.GetProperty(name, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
                return property.GetValue(target, null);

            foreach (var candidate in type.GetProperties(flags))
            {
                if (candidate.GetIndexParameters().Length == 0 &&
                    string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
                    return candidate.GetValue(target, null);
            }

            var field = type.GetField(name, flags);
            if (field != null)
                return field.GetValue(target);

            foreach (var candidate in type.GetFields(flags))
            {
                if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
                    return candidate.GetValue(target);
            }

            return null;
        }
    }
}
