using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SuitKing;

internal sealed unsafe class OutfitAutoCompleter(Plugin plugin)
{
    private readonly HashSet<CompletionItemKey> allowed = [];
    private readonly HashSet<CompletionItemKey> completedKeys = [];
    private ExistingOutfitCompletionPlan? submitted;
    private ulong characterId;
    private bool hideGearsets;
    private int completed;
    private int total;
    private int skipped;
    private long deadline;
    internal bool IsRunning { get; private set; }
    internal string Status { get; private set; } = "";

    internal void ClearStatus() { if (!IsRunning) Status = ""; }

    internal void Toggle()
    {
        if (IsRunning) { Stop("已停止；已提交的操作仍可能完成。"); return; }
        try
        {
            plugin.Creation.ClearStatus(); plugin.Quality.ClearStatus();
            if (plugin.Creation.IsRunning || plugin.Quality.IsRunning || plugin.CabinetStore.IsRunning)
            { Status = "请先停止其他收纳操作。"; return; }
            if (!plugin.IsDresserOpen || plugin.IsCabinetOpen || Plugin.Player.ContentId == 0)
            { Status = "请先打开投影台。"; return; }
            if (HasDialog() || plugin.QualityPreview.IsAllocated)
            { Status = "请先关闭已有确认或预览窗口。"; return; }
            plugin.Inventory.Refresh();
            if (!plugin.Inventory.IsComplete || !plugin.Inventory.AreOutfitsLoaded)
            { Status = "等待物品栏和投影台套装数据加载。"; return; }
            allowed.Clear(); completedKeys.Clear(); submitted = null;
            completed = skipped = 0;
            hideGearsets = plugin.Config.HideGearsetItems;
            foreach (var item in OutfitDisplayFilter.Apply(plugin.Inventory.Equipment, hideGearsets, ""))
            {
                if (item.Assessment?.State != OutfitStorageState.CanComplete) continue;
                if (item.Condition < 30000) { skipped++; continue; }
                if (KeyOf(item) is { } key) allowed.Add(key);
            }
            total = allowed.Count;
            if (total == 0)
            { Status = skipped > 0 ? $"有 {skipped} 件装备需要修理，暂时没有可提交装备。" : "没有可补齐的装备。"; return; }
            characterId = Plugin.Player.ContentId;
            deadline = Environment.TickCount64 + 15_000;
            IsRunning = true;
            Status = $"准备静默补齐 {total} 件。";
        }
        catch (Exception error)
        {
            Plugin.Log.Error(error, "SuitKing could not start silent completion");
            if (IsRunning) Stop("无法开始补齐，请查看 /xllog。");
            else Status = "无法开始补齐，请查看 /xllog。";
        }
    }

    internal void Stop(string message)
    {
        IsRunning = false;
        allowed.Clear(); completedKeys.Clear(); submitted = null;
        Status = $"{message} 已补齐 {completed} / {total} 件。";
        plugin.RequestRefresh();
    }

    internal void Tick()
    {
        if (!IsRunning) return;
        try
        {
            if (!plugin.IsDresserOpen || plugin.IsCabinetOpen || Plugin.Player.ContentId != characterId)
            { Stop("投影台已关闭或角色已切换。"); return; }
            if (plugin.Config.HideGearsetItems != hideGearsets)
            { Stop("过滤条件已改变。"); return; }
            if (Environment.TickCount64 > deadline)
            { Stop("等待游戏响应超时，未重试提交。"); return; }
            if (HasDialog() || plugin.QualityPreview.IsAllocated)
            { Stop("出现其他确认或套装操作窗口，已停止。"); return; }
            var manager = MirageManager.Instance();
            if (manager == null || !manager->PrismBoxRequested || !manager->PrismBoxLoaded) return;
            // One request in flight. Check server state each frame instead of sleeping
            // through several dialog animations. Never resubmit an unacknowledged batch.
            if (submitted != null)
            {
                if (manager->PrismBoxItemIds[submitted.Stored.BoxIndex] != submitted.RawId)
                { Stop("目标套装记录已改变。"); return; }
                if (!ExistingOutfitCompletionPlan.IsAcknowledged(submitted.Sources,
                    key => manager->IsSetSlotUnlocked((uint)submitted.Stored.BoxIndex,
                        Array.IndexOf(submitted.Definition.Pieces.ToArray(), key.ItemId)),
                    key => ReadKey((InventoryType)key.Container, key.Slot) == key,
                    submitted.Sources.All(SourceReadable))) return;
                foreach (var key in submitted.Sources) completedKeys.Add(key);
                completed += submitted.Sources.Count;
                submitted = null;
                deadline = Environment.TickCount64 + 15_000;
                plugin.RequestRefresh();
            }
            SubmitNext(manager);
        }
        catch (Exception error)
        {
            Plugin.Log.Error(error, "SuitKing silent completion stopped");
            Stop(error is InvalidOperationException ? error.Message : "自动补齐遇到错误，请查看 /xllog。");
        }
    }

    private void SubmitNext(MirageManager* manager)
    {
        plugin.Inventory.Refresh();
        if (!plugin.Inventory.IsComplete || !plugin.Inventory.AreOutfitsLoaded) return;
        var stored = DresserOutfitReader.Read();
        if (stored == null) return;
        var candidates = plugin.Inventory.Equipment
            .Where(item => item.Assessment?.State == OutfitStorageState.CanComplete)
            .Select(KeyOf).Where(key => key.HasValue && allowed.Contains(key.Value) && !completedKeys.Contains(key.Value))
            .Select(key => key!.Value).ToArray();
        var plan = ExistingOutfitCompletionPlan.First(candidates, stored, plugin.Inventory.FindOutfit);
        if (plan == null)
        {
            skipped += total - completed;
            Stop(skipped > 0 ? $"本次处理结束，跳过 {skipped} 件已变化、重复或需修理装备。" : "自动补齐完成。");
            return;
        }
        if (manager->PrismBoxItemIds[plan.Stored.BoxIndex] != plan.RawId
            || plan.Sources.Any(key => !IsCurrentEligible(key)
                || manager->IsSetSlotUnlocked((uint)plan.Stored.BoxIndex, Array.IndexOf(plan.Definition.Pieces.ToArray(), key.ItemId))))
        { Stop("目标装备、品质或套装缺件已改变，未提交。"); return; }
        var inventory = InventoryManager.Instance();
        var prisms = inventory == null ? 0u : (uint)inventory->GetInventoryItemCount(QualityNormalizationPolicy.GlamourPrismId, false, false, false);
        var reason = OutfitCompletionPolicy.ValidateHandIns(allowed, plan.Sources, plan.Sources[0], prisms);
        if (reason != null) { Stop(reason); return; }
        var packed = OutfitSubmissionLayout.Pack(plan.Definition.Pieces, plan.Sources, plan.Stored.HighQuality);
        InventoryType* containers = stackalloc InventoryType[OutfitSubmissionLayout.Capacity];
        ushort* slots = stackalloc ushort[OutfitSubmissionLayout.Capacity];
        for (var i = 0; i < OutfitSubmissionLayout.Capacity; i++) { containers[i] = InventoryType.Invalid; slots[i] = 0; }
        for (var i = 0; i < packed.Length; i++) { containers[i] = (InventoryType)packed[i].Container; slots[i] = packed[i].Slot; }
        submitted = plan; // Latch before calling the inventory-changing native method.
        deadline = Environment.TickCount64 + 15_000;
        Status = $"正在静默补齐 {completed} / {total} 件：{plan.Definition.Name}（{packed.Length} 件）";
        Plugin.Log.Information("Completing existing outfit {Id} at dresser slot {Index} with {Count} sources.",
            plan.RawId, plan.Stored.BoxIndex, packed.Length);
        if (!manager->StoreExistingOutfit((uint)plan.Stored.BoxIndex, containers, slots))
            Stop("游戏未接受补齐请求，未重复提交。");
    }

    private bool IsCurrentEligible(CompletionItemKey key)
    {
        if (!allowed.Contains(key) || completedKeys.Contains(key) || ReadKey((InventoryType)key.Container, key.Slot) != key) return false;
        var manager = InventoryManager.Instance();
        var item = manager->GetInventoryContainer((InventoryType)key.Container)->GetInventorySlot(key.Slot);
        var gearsets = RaptureGearsetModule.Instance();
        return item->Condition >= 30000 && (!hideGearsets || (gearsets != null && !gearsets->IsItemRegisteredToGearset(item)))
            && plugin.Inventory.Equipment.Any(entry => KeyOf(entry) == key
                && entry.Assessment?.State == OutfitStorageState.CanComplete
                && (!hideGearsets || entry.RegisteredToGearset == false));
    }

    private static CompletionItemKey? KeyOf(OutfitEquipment item)
        => OutfitInventoryReader.TryResolveContainer(item.Location, out var container) && item.Slot is > 0 and <= ushort.MaxValue
            ? new(item.ItemId, (uint)container, (ushort)(item.Slot - 1), item.HighQuality) : null;

    private static bool SourceReadable(CompletionItemKey key)
    {
        var manager = InventoryManager.Instance();
        if (manager == null) return false;
        var container = manager->GetInventoryContainer((InventoryType)key.Container);
        return container != null && container->IsLoaded && key.Slot < container->Size;
    }

    private static CompletionItemKey? ReadKey(InventoryType container, ushort slot)
    {
        var manager = InventoryManager.Instance();
        if (manager == null) return null;
        var inventory = manager->GetInventoryContainer(container);
        if (inventory == null || !inventory->IsLoaded || slot >= inventory->Size) return null;
        var item = inventory->GetInventorySlot(slot);
        if (item == null || item->IsSymbolic || item->ItemId == 0 || item->Quantity != 1) return null;
        return new(item->ItemId, (uint)container, slot, (item->Flags & InventoryItem.ItemFlags.HighQuality) != 0);
    }

    private static bool HasDialog() => new[] { "MiragePrismPrismSetConvert", "MiragePrismPrismSetConvertC", "SelectYesno",
        NativeOutfitRestorer.SelectionName, NativeOutfitRestorer.ConfirmationName }.Any(name => GetAddon(name) != null);

    private static AtkUnitBase* GetAddon(string name)
    {
        var handle = Plugin.GameGui.GetAddonByName(name);
        return !handle.IsNull && handle.IsReady && handle.IsVisible ? (AtkUnitBase*)handle.Address : null;
    }

    internal static bool ClickButton(AtkUnitBase* addon, AtkComponentButton* button)
    {
        if (button == null || !button->IsEnabled || button->OwnerNode == null || !button->AtkResNode->IsVisible()) return false;
        // Use the real registered event, not a guessed FireCallback argument or a signature hook.
        var evt = button->OwnerNode->AtkResNode.AtkEventManager.Event;
        for (var i = 0; evt != null && i < 64; i++, evt = evt->NextEvent)
        {
            if (evt->State.EventType != AtkEventType.ButtonClick || evt->Listener != (AtkEventListener*)addon) continue;
            if ((evt->State.StateFlags & AtkEventStateFlags.IsGlobalEvent) != 0)
                addon->ReceiveGlobalEvent(evt->State.EventType, (int)evt->Param, evt);
            else addon->ReceiveEvent(evt->State.EventType, (int)evt->Param, evt);
            return true;
        }
        return false;
    }

}
