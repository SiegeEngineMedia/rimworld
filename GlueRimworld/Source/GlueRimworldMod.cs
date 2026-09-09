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

            // Liveness is checked asynchronously from GlueBehaviorTickComponent after the game
            // has initialized. Mod constructors run on RimWorld's startup path; a dead host must
            // never stall startup for the bridge timeout.
        }
    }
}
