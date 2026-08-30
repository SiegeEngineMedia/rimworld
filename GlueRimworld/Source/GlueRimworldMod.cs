using HarmonyLib;
using Verse;

namespace GlueRimworld
{
    /// <summary>
    /// Mod entrypoint. RimWorld instantiates one instance of every Mod subclass found in a
    /// loaded mod's assemblies at startup (Verse.LoadedModManager) -- this constructor is the
    /// real, standard place to do one-time setup (Harmony patching, bridge/catalog wiring).
    /// </summary>
    public class GlueRimworldMod : Mod
    {
        public static GlueBridgeClient? Bridge { get; private set; }
        public static RimworldSeedCatalog? Seeds { get; private set; }

        public GlueRimworldMod(ModContentPack content) : base(content)
        {
            var harmony = new Harmony("glue.gluerimworld");
            harmony.PatchAll();

            Bridge = new GlueBridgeClient();
            Seeds = RimworldSeedCatalog.LoadFromModContent(content);

            Log.Message("[GlueRimworld] Loaded. " + Seeds.Summary());

            if (!Bridge.Ping())
            {
                Log.Warning(
                    "[GlueRimworld] glue-runtime-host not reachable at http://127.0.0.1:8765 -- " +
                    "behavior-candidate calls will no-op (fail loud, once per pawn per tick window, " +
                    "see PawnBehaviorBridge) until it is running. From the glue monorepo: " +
                    "dotnet run --project runtimes/csharp/glue-runtime-host. See NEXT_STEPS.md.");
            }
        }
    }
}
