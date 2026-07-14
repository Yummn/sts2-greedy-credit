using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace GreedyCredit;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "GreedyCredit";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } = new(ModId, LogType.Generic);

    public static void Initialize()
    {
        var harmony = new Harmony(ModId);
        harmony.PatchAll();
        Logger.Info("[GreedyCredit] loaded: shop credit enabled; any debt => built-in Greed curses by 50-gold bands; BaseLib not required.");
    }
}
