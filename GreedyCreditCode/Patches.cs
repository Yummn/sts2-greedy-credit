using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Nodes.Events.Custom;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace GreedyCredit;

/// <summary>
/// Makes every stocked shop entry count as affordable. This only changes the shop decision gate;
/// the actual charge/debt is handled by MerchantEntryPurchasePatch.
/// </summary>
[HarmonyPatch(typeof(MerchantEntry), nameof(MerchantEntry.EnoughGold), MethodType.Getter)]
internal static class MerchantEntryEnoughGoldPatch
{
    [HarmonyPostfix]
    private static void Postfix(MerchantEntry __instance, ref bool __result)
    {
        if (__instance.IsStocked)
            __result = true;
    }
}

/// <summary>
/// Forces normal merchant purchases down the "ignore vanilla cost" path, then charges the full price manually
/// so gold may go negative. Already-free purchases from other mods (ignoreCost=true) are left untouched.
/// </summary>
[HarmonyPatch(typeof(MerchantEntry), nameof(MerchantEntry.OnTryPurchaseWrapper))]
internal static class MerchantEntryPurchasePatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Low)]
    private static void Prefix(
        MerchantEntry __instance,
        MerchantInventory inventory,
        ref bool ignoreCost,
        out PurchaseState? __state)
    {
        __state = null;

        try
        {
            if (ignoreCost)
                return;
            if (!__instance.IsStocked || __instance.Cost <= 0)
                return;
            if (inventory?.Player is not Player player)
                return;

            __state = new PurchaseState
            {
                Entry = __instance,
                Player = player,
                Cost = __instance.Cost,
                GoldBefore = player.Gold,
                WasStocked = __instance.IsStocked,
                SignatureBefore = DebtManager.EntrySignature(__instance)
            };

            ignoreCost = true;
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"[GreedyCredit] purchase prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    private static void Postfix(PurchaseState? __state, ref Task<bool> __result)
    {
        if (__state == null)
            return;

        __result = DebtManager.AfterSuccessfulPurchase(__result ?? Task.FromResult(false), __state);
    }
}

/// <summary>
/// Card removal uses a separate three-argument wrapper. If we only patch the normal shop wrapper,
/// insufficient-gold removal can still run through vanilla payment logic and clamp gold back to 0.
/// </summary>
[HarmonyPatch(typeof(MerchantCardRemovalEntry), nameof(MerchantCardRemovalEntry.OnTryPurchaseWrapper), typeof(MerchantInventory), typeof(bool), typeof(bool))]
internal static class MerchantCardRemovalPurchasePatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Low)]
    private static void Prefix(
        MerchantCardRemovalEntry __instance,
        MerchantInventory inventory,
        ref bool ignoreCost,
        out PurchaseState? __state)
    {
        __state = null;

        try
        {
            if (ignoreCost)
                return;
            if (!__instance.IsStocked || __instance.Cost <= 0)
                return;
            if (inventory?.Player is not Player player)
                return;

            __state = new PurchaseState
            {
                Entry = __instance,
                Player = player,
                Cost = __instance.Cost,
                GoldBefore = player.Gold,
                WasStocked = __instance.IsStocked,
                SignatureBefore = DebtManager.EntrySignature(__instance)
            };

            ignoreCost = true;
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"[GreedyCredit] card-removal prefix failed: {e.Message}");
        }
    }

    [HarmonyPostfix]
    private static void Postfix(PurchaseState? __state, ref Task<bool> __result)
    {
        if (__state == null)
            return;

        __result = DebtManager.AfterSuccessfulCardRemoval(__result ?? Task.FromResult(false), __state);
    }
}

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.Init))]
internal static class GreedyDebtCurseModelDbPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        try
        {
            var card = ModelDb.GetByIdOrNull<CardModel>(new ModelId("CARD", DebtManager.GreedEntryId));
            if (card == null)
            {
                try { ModelDb.Inject(typeof(Greed)); } catch { }
                card = ModelDb.GetByIdOrNull<CardModel>(new ModelId("CARD", DebtManager.GreedEntryId));
            }
            MainFile.Logger.Info(card != null
                ? $"[GreedyCredit] built-in Greed curse available in ModelDb: {card.Id}"
                : "[GreedyCredit] built-in Greed curse is missing from ModelDb after init.");
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"[GreedyCredit] custom curse ModelDb check failed: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(NMerchantRoom), "AfterRoomIsLoaded")]
internal static class MerchantRoomDebtRepaymentPatch
{
    private static readonly AccessTools.FieldRef<NMerchantRoom, List<Player>> PlayersRef =
        AccessTools.FieldRefAccess<NMerchantRoom, List<Player>>("_players");

    [HarmonyPostfix]
    private static void Postfix(NMerchantRoom __instance)
    {
        try
        {
            var players = PlayersRef(__instance);
            if (players is { Count: > 0 })
            {
                foreach (var player in players)
                    if (player != null)
                        DebtManager.RunSafely(DebtManager.ReconcileOnShopEntry(player, "merchant-room"));
                return;
            }
        }
        catch { }

        try
        {
            var me = LocalContext.GetMe(RunManager.Instance?.DebugOnlyGetState());
            if (me != null)
                DebtManager.RunSafely(DebtManager.ReconcileOnShopEntry(me, "merchant-room-local"));
        }
        catch { }
    }
}

[HarmonyPatch(typeof(NFakeMerchant), "AfterRoomIsLoaded")]
internal static class FakeMerchantDebtRepaymentPatch
{
    private static readonly AccessTools.FieldRef<NFakeMerchant, List<Player>> PlayersRef =
        AccessTools.FieldRefAccess<NFakeMerchant, List<Player>>("_players");

    [HarmonyPostfix]
    private static void Postfix(NFakeMerchant __instance)
    {
        try
        {
            var players = PlayersRef(__instance);
            if (players is { Count: > 0 })
            {
                foreach (var player in players)
                    if (player != null)
                        DebtManager.RunSafely(DebtManager.ReconcileOnShopEntry(player, "fake-merchant"));
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"[GreedyCredit] fake merchant reconcile failed: {e.Message}");
        }
    }
}


