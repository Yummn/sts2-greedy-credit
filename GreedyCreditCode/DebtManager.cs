using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Runs;

namespace GreedyCredit;

internal sealed class PurchaseState
{
    public required MerchantEntry Entry { get; init; }
    public required Player Player { get; init; }
    public required int Cost { get; init; }
    public required int GoldBefore { get; init; }
    public required bool WasStocked { get; init; }
    public required string SignatureBefore { get; init; }
}

internal static class DebtManager
{
    public const int DebtPerGreed = 50;
    public const string GreedEntryId = "GREED";

    // Runtime ledger of Greed curses created by this mod. It keeps this mod from removing unrelated Greed curses.
    // If a save is reloaded mid-run, we infer conservatively from current debt and existing Greed cards.
    private static readonly Dictionary<ulong, int> DebtGreedsByPlayer = new();

    public static void RunSafely(Task task)
    {
        _ = RunSafelyInternal(task);
    }

    private static async Task RunSafelyInternal(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"[GreedyCredit] async operation failed: {e}");
        }
    }

    public static int DesiredGreedCount(Player player)
    {
        // 只要欠钱就有惩罚，并按 50 金币一档向上取整：
        // -1~-50: 1 张，-51~-100: 2 张，-101~-150: 3 张。
        return player.Gold < 0 ? ((-player.Gold + DebtPerGreed - 1) / DebtPerGreed) : 0;
    }

    public static async Task<bool> AfterSuccessfulPurchase(Task<bool> originalTask, PurchaseState state)
    {
        bool success = originalTask != null && await originalTask;
        if (!success)
            return false;

        // For normal entries, a successful buy either depletes the slot or changes its model (Courier/restock).
        // For card-removal, cancel leaves the entry stocked/signature unchanged; confirm depletes it.
        bool purchased = state.WasStocked && (!state.Entry.IsStocked || EntrySignature(state.Entry) != state.SignatureBefore);
        if (!purchased)
            return success;

        await ChargeAndReconcile(state, "purchase");

        return success;
    }

    public static async Task<bool> AfterSuccessfulCardRemoval(Task<bool> originalTask, PurchaseState state)
    {
        bool success = originalTask != null && await originalTask;
        if (!success)
            return false;

        await ChargeAndReconcile(state, "card-removal");

        return success;
    }

    private static async Task ChargeAndReconcile(PurchaseState state, string reason)
    {
        int before = state.Player.Gold;
        DebitGoldAllowingNegative(state.Player, state.Cost);
        MainFile.Logger.Info($"[GreedyCredit] charged {state.Cost} gold ({reason}): {before} -> {state.Player.Gold}");

        await ReconcileGreedCurses(state.Player, allowRemoval: false, reason: reason);
    }

    public static async Task ReconcileOnShopEntry(Player player, string source)
    {
        await Task.Yield();
        await ReconcileGreedCurses(player, allowRemoval: true, reason: source);
    }

    private static void DebitGoldAllowingNegative(Player player, int cost)
    {
        if (cost <= 0)
            return;

        // PlayerCmd.LoseGold is intentionally not used because vanilla purchase paths assume gold never goes below 0.
        // Directly setting Player.Gold still raises the normal GoldChanged event used by the top bar.
        player.Gold -= cost;
    }

    private static async Task ReconcileGreedCurses(Player player, bool allowRemoval, string reason)
    {
        int desired = DesiredGreedCount(player);
        int tracked = GetTrackedGreedCount(player, desired);

        if (desired > tracked)
        {
            int toAdd = desired - tracked;
            int added = 0;
            for (int i = 0; i < toAdd; i++)
            {
                if (await AddGreedCurse(player))
                    added++;
            }

            int newTracked = Math.Min(desired, tracked + added);
            DebtGreedsByPlayer[player.NetId] = newTracked;
            MainFile.Logger.Info($"[GreedyCredit] add debt curses requested={toAdd}, added={added}, desired={desired}, tracked={newTracked}, deckGreeds={CountGreedInDeck(player)}, gold={player.Gold}, reason={reason}");
            return;
        }

        if (allowRemoval && desired < tracked)
        {
            int toRemove = tracked - desired;
            int removed = await RemoveGreedCurses(player, toRemove);
            DebtGreedsByPlayer[player.NetId] = Math.Max(desired, tracked - removed);
            MainFile.Logger.Info($"[GreedyCredit] removed {removed}/{toRemove} Greed curse(s), tracked={DebtGreedsByPlayer[player.NetId]}, reason={reason}");
            return;
        }

        DebtGreedsByPlayer[player.NetId] = tracked;
    }

    private static int GetTrackedGreedCount(Player player, int desired)
    {
        if (DebtGreedsByPlayer.TryGetValue(player.NetId, out int tracked))
            return Math.Min(tracked, CountGreedInDeck(player));

        // Conservative reload recovery: if the player is still in debt, treat up to desired existing Greeds as ours
        // so we do not duplicate curses after loading. If the debt is already paid, do not claim unrelated Greeds.
        int inferred = desired > 0 ? Math.Min(desired, CountGreedInDeck(player)) : 0;
        DebtGreedsByPlayer[player.NetId] = inferred;
        return inferred;
    }

    private static int CountGreedInDeck(Player player)
    {
        return player.Deck?.Cards?.Count(IsGreedCard) ?? 0;
    }

    private static async Task<bool> AddGreedCurse(Player player)
    {
        await Task.Yield();

        int before = CountGreedInDeck(player);
        var template = FindGreedTemplate();
        if (template == null)
        {
            MainFile.Logger.Error("[GreedyCredit] Greed debt curse template not found in ModelDb; cannot add debt curse.");
            return false;
        }

        var runState = GetRunState(player);
        if (runState == null)
        {
            MainFile.Logger.Error("[GreedyCredit] RunState not found; cannot add Greed curse.");
            return false;
        }

        CardModel card = runState.CreateCard(template.CanonicalInstance ?? template, player);

        // Direct insertion is used intentionally. CardPileCmd.AddCursesToDeck rejects some already-created
        // mutable CardModel instances ("used in incorrect place") even though adding the run-owned card to the
        // deck is valid and saveable.
        if (CountGreedInDeck(player) <= before)
        {
            try
            {
                if (player.Deck?.Cards?.Contains(card) != true)
                    player.Deck?.AddInternal(card, -1, false);
                player.Deck?.InvokeCardAddFinished();
                player.Deck?.InvokeContentsChanged();
            }
            catch (Exception e)
            {
                MainFile.Logger.Error($"[GreedyCredit] direct Greed curse insertion failed: {e}");
                return false;
            }
        }

        int after = CountGreedInDeck(player);
        bool added = after > before;
        MainFile.Logger.Info($"[GreedyCredit] debt curse add result added={added}, deckGreeds {before}->{after}, template={SafeCardId(template)}, card={SafeCardId(card)}");
        return added;
    }

    private static async Task<int> RemoveGreedCurses(Player player, int count)
    {
        if (count <= 0)
            return 0;

        var runState = GetRunState(player);
        var cards = player.Deck?.Cards?.Where(IsGreedCard).Take(count).ToList() ?? [];
        if (cards.Count == 0)
            return 0;

        try
        {
            await CardPileCmd.RemoveFromDeck((IReadOnlyList<CardModel>)cards, false);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"[GreedyCredit] CardPileCmd.RemoveFromDeck failed, using direct removal: {e.Message}");
            foreach (var card in cards)
            {
                try { card.RemoveFromState(); } catch { }
                try
                {
                    if (runState?.ContainsCard(card) == true)
                        runState.RemoveCard(card);
                }
                catch { }
            }
        }

        return cards.Count;
    }

    private static RunState? GetRunState(Player player)
    {
        return player.RunState as RunState ?? RunManager.Instance?.DebugOnlyGetState();
    }

    private static CardModel? FindGreedTemplate()
    {
        try
        {
            var byId = ModelDb.GetByIdOrNull<CardModel>(new ModelId("CARD", GreedEntryId));
            if (byId != null)
                return byId;
        }
        catch { }

        try
        {
            var byType = ModelDb.AllCards.FirstOrDefault(card => card is Greed);
            if (byType != null)
                return byType;
        }
        catch { }

        try
        {
            var vanillaGreed = ModelDb.GetByIdOrNull<CardModel>(new ModelId("CARD", StringHelper.Slugify("Greed")));
            if (vanillaGreed != null)
                return vanillaGreed;
        }
        catch { }

        TryInjectGreed();

        try
        {
            var byIdAfterInject = ModelDb.GetByIdOrNull<CardModel>(new ModelId("CARD", GreedEntryId));
            if (byIdAfterInject != null)
                return byIdAfterInject;
        }
        catch { }

        try
        {
            return ModelDb.AllCards.FirstOrDefault(IsGreedCard);
        }
        catch
        {
            return null;
        }
    }

    public static bool IsGreedCard(CardModel card)
    {
        try
        {
            string entry = card.Id.Entry;
            return card is Greed
                   || string.Equals(entry, GreedEntryId, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(entry, "Greed", StringComparison.OrdinalIgnoreCase)
                   // Compatibility with older v0.1.x test builds that used a custom BaseLib-backed marker.
                   || string.Equals(entry, "GREEDYCREDIT-GREEDY_DEBT_CURSE", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(entry, "GREEDY_DEBT_CURSE", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(entry, "GREEDYDEBTCURSE", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void TryInjectGreed()
    {
        try
        {
            // Greed is a built-in StS2 curse model, but some builds do not keep it in ModelDb until requested.
            // Injecting the game's own model avoids any BaseLib/custom-card dependency.
            ModelDb.Inject(typeof(Greed));
        }
        catch
        {
            // Already injected, not initialized, or unavailable in this game build.
        }
    }

    private static string SafeCardId(CardModel card)
    {
        try { return card.Id.ToString(); }
        catch { return card.GetType().FullName ?? card.GetType().Name; }
    }

    public static string EntrySignature(MerchantEntry entry)
    {
        try
        {
            string model = TryGetModelId(entry) ?? "no-model";
            return $"{entry.GetType().FullName}|stocked={entry.IsStocked}|cost={entry.Cost}|model={model}";
        }
        catch
        {
            return entry.GetType().FullName ?? "unknown-entry";
        }
    }

    private static string? TryGetModelId(object source)
    {
        object? directModel = GetPropertyValue(source, "Model");
        if (directModel is AbstractModel direct)
            return direct.Id.ToString();

        object? creationResult = GetPropertyValue(source, "CreationResult");
        object? card = creationResult != null ? GetPropertyValue(creationResult, "Card") : null;
        if (card is AbstractModel cardModel)
            return cardModel.Id.ToString();

        object? potion = creationResult != null ? GetPropertyValue(creationResult, "Potion") : null;
        if (potion is AbstractModel potionModel)
            return potionModel.Id.ToString();

        return null;
    }

    private static object? GetPropertyValue(object source, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        for (Type? type = source.GetType(); type != null; type = type.BaseType)
        {
            var prop = type.GetProperty(name, flags);
            if (prop != null)
                return prop.GetValue(source);
        }
        return null;
    }
}

