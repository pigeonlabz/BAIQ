using Backpack.Config;
using Backpack.Patches;
using Backpack.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if IL2CPP
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Items;
using Il2CppTMPro;
using SlotList = Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot>;
#elif MONO
using ScheduleOne.DevUtilities;
using ScheduleOne.ItemFramework;
using ScheduleOne.Money;
using ScheduleOne.UI;
using ScheduleOne.UI.Items;
using TMPro;
using SlotList = System.Collections.Generic.List<ScheduleOne.ItemFramework.ItemSlot>;
#endif

namespace Backpack;

public static class StorageHotkeys
{
    public static void Update()
    {
        if (IsTyping())
            return;

        var takeAllKey = Configuration.Instance.TakeAllKey;
        if (takeAllKey != KeyCode.None && Input.GetKeyDown(takeAllKey))
            TryTakeAll();

        var sortKey = Configuration.Instance.SortActiveContainerKey;
        if (sortKey != KeyCode.None && Input.GetKeyDown(sortKey))
            TrySortActive();
    }

    public static bool IsTyping()
    {
        if (BackpackSearchBar.AnyInputFocused)
            return true;

        var selected = EventSystem.current?.currentSelectedGameObject;
        if (selected == null || !selected.activeInHierarchy)
            return false;

        return selected.GetComponent<InputField>()?.isFocused == true
               || selected.GetComponent<TMP_InputField>()?.isFocused == true;
    }

    private static void TrySortActive()
    {
        if (PlayerBackpack.Instance?.IsOpen == true)
        {
            PlayerBackpack.Instance.SortContents();
            Logger.Diagnostic("[Hotkey] Sorted standalone Backpack.");
            return;
        }

        if (PhoneBackpackUI.IsOpen)
        {
            PlayerBackpack.Instance?.SortContents();
            Logger.Diagnostic("[Hotkey] Sorted Phone Backpack.");
            return;
        }

        if (ContextualBackpackPanel.ContainsScreenPoint(Input.mousePosition))
        {
            PlayerBackpack.Instance?.SortContents();
            Logger.Diagnostic("[Hotkey] Sorted contextual Backpack under pointer.");
            return;
        }

        if (StorageSortButton.TrySortActive())
        {
            Logger.Diagnostic("[Hotkey] Sorted active native storage.");
            return;
        }

        if (ContextualBackpackPanel.IsOpen && PlayerBackpack.Instance != null)
        {
            PlayerBackpack.Instance.SortContents();
            Logger.Diagnostic("[Hotkey] Sorted active contextual Backpack.");
        }
    }

    private static void TryTakeAll()
    {
        if (!StorageSortButton.TryGetOpenStorageOwner(out var owner, out var menu))
            return;

        var manager = Singleton<ItemUIManager>.Instance;
        var slots = owner.ItemSlots;
        if (manager == null || manager.IsCurrentlyDragging || slots == null)
            return;

        var movedStacks = 0;
#if !SCHEDULE_I_BETA_047
        var skippedCash = 0;
#endif
        for (var i = 0; i < slots.Count; i++)
        {
#if IL2CPP
            var source = slots[new Index(i)]?.TryCast<ItemSlot>();
#elif MONO
            var source = slots[i];
#endif
            var sourceUi = FindSlotUi(menu, source);
            if (source?.ItemInstance == null || sourceUi == null || !manager.CanDragFromSlot(sourceUi)
                || source.IsRemovalLocked || UserSlotLockRegistry.IsUserLocked(owner, source))
                continue;

#if !SCHEDULE_I_BETA_047
            if (AsCash(source.ItemInstance) != null)
            {
                skippedCash++;
                continue;
            }
#endif

            try
            {
                if (TryQuickMoveEntireStack(manager, source))
                    movedStacks++;
            }
            catch (Exception exception)
            {
                Logger.Warning($"Take All skipped a stack safely: {exception.Message}");
            }
            finally
            {
                OvercapTransferContext.End();
            }
        }

        if (movedStacks > 0)
        {
            StorageSortButton.RefreshVisibility();
            ContextualBackpackPanel.Refresh();
            ResponsiveBackpackUI.Refresh();
        }

#if SCHEDULE_I_BETA_047
        Logger.Diagnostic($"Take All moved {movedStacks} stack(s) from the active storage owner.");
#else
        if (skippedCash > 0)
            Logger.Diagnostic($"Take All moved {movedStacks} stack(s) and left {skippedCash} cash stack(s) for the native cash transfer control.");
#endif
    }

    private static bool TryQuickMoveEntireStack(ItemUIManager manager, ItemSlot source)
    {
        var originalQuantity = source.Quantity;
#if SCHEDULE_I_BETA_047
        var sourceCash = AsCash(source.ItemInstance);
        var originalCashBalance = sourceCash?.Balance ?? 0f;
#endif
        var destinations = manager.GetQuickMoveSlots(source);
        destinations = ContextualBackpackPanel.ApplyPlayerDestinationPriority(destinations);
        destinations = InventoryInteractionPatch.FilterValidQuickMoveDestinations(source, destinations);
        if (destinations == null || destinations.Count == 0)
            return false;

#if SCHEDULE_I_BETA_047
        if (sourceCash != null)
        {
            MoveCashIntoDestinations(source, sourceCash, destinations, true);
            sourceCash = AsCash(source.ItemInstance);
            if (sourceCash != null)
                MoveCashIntoDestinations(source, sourceCash, destinations, false);
            return originalCashBalance > (AsCash(source.ItemInstance)?.Balance ?? 0f);
        }
#endif

        MoveIntoDestinations(source, destinations, true);
        MoveIntoDestinations(source, destinations, false);
        return source.ItemInstance == null || source.Quantity < originalQuantity;
    }

#if SCHEDULE_I_BETA_047
    private static void MoveCashIntoDestinations(
        ItemSlot source,
        CashInstance sourceCash,
        SlotList destinations,
        bool existingStacksOnly)
    {
        for (var i = 0; i < destinations.Count && sourceCash != null && sourceCash.Balance > 0f; i++)
        {
#if IL2CPP
            var destination = destinations[new Index(i)]?.TryCast<ItemSlot>();
#elif MONO
            var destination = destinations[i];
#endif
            var destinationCash = AsCash(destination?.ItemInstance);
            if (destination == null || (existingStacksOnly && destinationCash == null)
                || (!existingStacksOnly && destination.ItemInstance != null)
                || !InventoryInteractionPatch.IsValidCashDestination(source, destination, sourceCash, false)
                || UserSlotLockRegistry.IsUserLocked(destination))
                continue;

#if IL2CPP
            var dedicatedCashSlot = destination.TryCast<CashSlot>();
#elif MONO
            var dedicatedCashSlot = destination as CashSlot;
#endif
            var balanceLimit = dedicatedCashSlot != null
                ? CashInstance.MAX_BALANCE
                : CashSlot.MAX_CASH_PER_SLOT;
            var amount = Math.Min(sourceCash.Balance, balanceLimit - (destinationCash?.Balance ?? 0f));
            if (amount <= 0f)
                continue;

            if (destinationCash == null)
            {
                var copy = sourceCash.GetCopy(1);
                destinationCash = AsCash(copy);
                if (destinationCash == null)
                    continue;

                destinationCash.SetBalance(amount, true);
                destination.SetStoredItem(copy);
                destinationCash = AsCash(destination.ItemInstance);
                if (destinationCash == null)
                    continue;
                amount = Math.Min(amount, destinationCash.Balance);
                if (amount <= 0f)
                    continue;
            }
            else
            {
                destinationCash.ChangeBalance(amount);
                destination.ReplicateStoredInstance();
            }

            sourceCash.ChangeBalance(-amount);
            source.ReplicateStoredInstance();
            sourceCash = AsCash(source.ItemInstance);
        }
    }

#endif

    private static void MoveIntoDestinations(ItemSlot source, SlotList destinations, bool existingStacksOnly)
    {
        for (var i = 0; i < destinations.Count && source?.ItemInstance != null && source.Quantity > 0; i++)
        {
#if IL2CPP
            var destination = destinations[new Index(i)]?.TryCast<ItemSlot>();
#elif MONO
            var destination = destinations[i];
#endif
            if (destination == null || destination.IsAddLocked || UserSlotLockRegistry.IsUserLocked(destination))
                continue;

            if (existingStacksOnly)
            {
                if (destination.ItemInstance == null || !destination.ItemInstance.CanStackWith(source.ItemInstance, false))
                    continue;
            }
            else if (destination.ItemInstance != null)
            {
                continue;
            }

            var moveAmount = Math.Min(destination.GetCapacityForItem(source.ItemInstance, true), source.Quantity);
            if (moveAmount <= 0)
                continue;

            var copy = source.ItemInstance.GetCopy(moveAmount);
            destination.AddItem(copy);
            source.ChangeQuantity(-moveAmount);
        }
    }

    private static CashInstance AsCash(ItemInstance item)
    {
#if IL2CPP
        return item?.TryCast<CashInstance>();
#elif MONO
        return item as CashInstance;
#endif
    }


    private static ItemSlotUI FindSlotUi(StorageMenu menu, ItemSlot slot)
    {
        if (menu?.SlotsUIs == null || slot == null)
            return null;

        for (var i = 0; i < menu.SlotsUIs.Length; i++)
        {
            var candidate = menu.SlotsUIs[i];
            if (candidate != null && PlayerBackpack.SlotsMatch(candidate.assignedSlot, slot))
                return candidate;
        }

        return null;
    }
}
