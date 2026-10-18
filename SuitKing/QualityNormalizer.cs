using Phase = SuitKing.QualityNormalizationPhase;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SuitKing;

internal sealed unsafe class QualityNormalizer(Plugin plugin)
{
    private Phase phase;
    private QualityNormalizationPlan? plan;
    private ulong characterId;
    private ulong dresserId;
    private uint dresserDataId;
    private uint territoryId;
    private long deadline;
    private long nextAction;
    private readonly List<CompletionItemKey> restored = [];
    private readonly HashSet<CompletionItemKey> baseline = [];
    private readonly List<CompletionItemKey> handIns = [];
    private int lowerIndex;
    private bool restoreAccepted;
    private readonly NativeOutfitRestorer nativeRestore = new();
    internal bool IsRunning => phase != Phase.Idle;
    internal string Status { get; private set; } = "";

    internal void ClearStatus() { if (!IsRunning) Status = ""; }

    internal void OpenPreview()
    {
        if (IsRunning) { Stop("已停止。"); return; }
        if (plugin.Creation.IsRunning) { Status = "请先停止建立套装。"; return; }
        plugin.Creation.ClearStatus();
        if (plugin.QualityPreview.IsAllocated) return;
        try
        {
            RequireContext();
            plugin.Inventory.Refresh();
            if (!plugin.Inventory.IsComplete || !plugin.Inventory.AreOutfitsLoaded)
                throw new InvalidOperationException("等待完整装备与套装数据。");
            var stored = DresserOutfitReader.Read();
            var candidate = stored == null ? null : QualityNormalizationPlan.First(plugin.Inventory.Equipment, stored, plugin.Inventory.FindOutfit);
            if (candidate == null) throw new InvalidOperationException("没有可自动处理的 NQ 装备与 HQ 套装冲突。");
            Preflight(candidate);
            CaptureDresser();
            characterId = Plugin.Player.ContentId;
            plan = candidate;
            plugin.QualityPreview.Present(candidate);
            Status = "请在预览中确认本套装备永久降为 NQ。";
        }
        catch (Exception error) { Status = error.Message; Plugin.Log.Warning(error, "Quality preview unavailable"); }
    }

    internal void Confirm(QualityNormalizationPlan expected)
    {
        if (IsRunning || !ReferenceEquals(plan, expected)) return;
        try
        {
            RequireContext();
            if (Plugin.Player.ContentId != characterId) throw new InvalidOperationException("角色已改变，请重新预览。");
            plugin.Inventory.Refresh();
            if (!plugin.Inventory.IsComplete || !plugin.Inventory.AreOutfitsLoaded) throw new InvalidOperationException("数据刷新中，请重新预览。");
            Preflight(expected);
            RequireSameDresser();
            restoreAccepted = false;
            SetPhase(Phase.Restore);
            Status = "等待预览窗口关闭后取出 HQ 部件。";
        }
        catch (Exception error) { Stop(error.Message); Plugin.Log.Warning(error, "Quality normalization did not start"); }
    }

    internal void Stop(string reason)
    {
        var changed = restoreAccepted;
        if (changed) Plugin.Log.Warning("Quality normalization stopped: {Reason}; restored or lowered gear remains in inventory.", reason);
        phase = Phase.Idle;
        Status = reason + (changed ? " 取出请求已确认；若装备已取出或降品质，请检查物品栏后手动存回。" : "");
        plan = null;
        restoreAccepted = false;
        plugin.RequestRefresh();
    }

    internal bool AcceptsContextTransition(bool dresserOpen, bool cabinetOpen)
        => QualityNormalizationPolicy.AcceptsContextTransition(phase, dresserOpen, cabinetOpen);

    private bool OutsideDresser => QualityNormalizationPolicy.AllowsClosedDresser(phase);

    private static bool Busy => Plugin.Conditions[ConditionFlag.OccupiedInQuestEvent]
        || Plugin.Conditions[ConditionFlag.Occupied] || Plugin.Conditions[ConditionFlag.OccupiedInEvent]
        || Plugin.Conditions[ConditionFlag.OccupiedInCutSceneEvent] || Plugin.Conditions[ConditionFlag.WatchingCutscene]
        || Plugin.Conditions[ConditionFlag.BetweenAreas] || Plugin.Conditions[ConditionFlag.BetweenAreas51]
        || Plugin.Conditions[ConditionFlag.InCombat] || Plugin.Conditions[ConditionFlag.Casting];

    private static bool CanLower => QualityNormalizationPolicy.CanLowerOutside(
        Visible(Plugin.DresserAddonName) || Visible("MiragePrismPrismBoxCrystallize"),
        Visible("Cabinet") || Visible("CabinetWithdraw"), Busy);

    private void CaptureDresser()
    {
        var player = Plugin.Objects.LocalPlayer;
        if (player == null) throw new InvalidOperationException("角色数据未就绪。");
        bool IsDresser(IGameObject obj) => obj.ObjectKind == ObjectKind.EventObj
            && obj.Name.TextValue == "投影台" && Vector3.Distance(player.Position, obj.Position) <= 6;
        var target = Plugin.Targets.Target;
        var candidates = Plugin.Objects.Where(IsDresser).ToArray();
        var dresser = target != null && IsDresser(target) ? target : candidates.Length == 1 ? candidates[0] : null;
        if (dresser == null) throw new InvalidOperationException("无法唯一识别附近的投影台，请先选中当前投影台。");
        dresserId = dresser.GameObjectId;
        dresserDataId = dresser.BaseId;
        territoryId = Plugin.ClientState.TerritoryType;
        RequireSameDresser();
    }

    private IGameObject RequireSameDresser()
    {
        var player = Plugin.Objects.LocalPlayer;
        var dresser = Plugin.Objects.FirstOrDefault(obj => obj.GameObjectId == dresserId);
        if (player == null || dresser == null || !QualityNormalizationPolicy.SameDresser(dresserId, dresserDataId,
            territoryId, dresser.GameObjectId, dresser.BaseId, Plugin.ClientState.TerritoryType,
            Vector3.Distance(player.Position, dresser.Position)) || dresser.ObjectKind != ObjectKind.EventObj)
            throw new InvalidOperationException("原投影台已不可用或距离过远，已停止。");
        return dresser;
    }

    private void RequireContext()
    {
        if (!plugin.IsDresserOpen || plugin.IsCabinetOpen || Plugin.Player.ContentId == 0)
            throw new InvalidOperationException("请保持投影台主窗口打开。");
        if (plugin.Completion.IsRunning || plugin.CabinetStore.IsRunning || plugin.Creation.IsRunning)
            throw new InvalidOperationException("请先停止其他收纳操作。");
        foreach (var name in new[] { "MiragePrismPrismSetConvert", "MiragePrismPrismSetConvertC", NativeOutfitRestorer.SelectionName, NativeOutfitRestorer.ConfirmationName, "SelectYesno" })
            if (Visible(name)) throw new InvalidOperationException("请先关闭已有的幻影化或确认窗口。");
    }

    private void Preflight(QualityNormalizationPlan candidate)
    {
        var manager = MirageManager.Instance();
        var inventory = InventoryManager.Instance();
        if (manager == null || !manager->PrismBoxRequested || !manager->PrismBoxLoaded || inventory == null || candidate.RestoreMask == 0
            || candidate.Stored.BoxIndex is < 0 or >= 800 || !candidate.Stored.HighQuality
            || candidate.PrismCount > OutfitSubmissionLayout.Capacity)
            throw new InvalidOperationException("套装数据未就绪，请重新预览。");
        if (manager->PrismBoxItemIds[candidate.Stored.BoxIndex] != candidate.RawStoredId)
            throw new InvalidOperationException("目标套装记录已改变，请重新预览。");
        for (var slot = 0; slot < candidate.Definition.Pieces.Count; slot++)
        {
            var id = candidate.Definition.Pieces[slot];
            if (id == 0) continue;
            var saved = manager->IsSetSlotUnlocked((uint)candidate.Stored.BoxIndex, slot);
            if (saved != candidate.Stored.Pieces.Contains(id)) throw new InvalidOperationException("套装部件已变化，请重新预览。");
            if (!saved) continue;
            if (Registered(id, true) || Registered(id, false)) throw new InvalidOperationException("受影响部件已登记到职业配装，未取出。");
            if (inventory->GetInventoryItemCount(id, true, true, true) != 0)
                throw new InvalidOperationException("随身已有同名 HQ 副本，请先整理，避免混淆取出的装备。");
        }
        if (manager->PrismBoxItemIds.Contains(candidate.Stored.Id)) throw new InvalidOperationException("另有 NQ 套装记录，请先手动整理。");
        if (inventory->GetEmptySlotsInBag() < candidate.Stored.Pieces.Count)
            throw new InvalidOperationException("物品栏空位不足，未取出。");
        if (inventory->GetInventoryItemCount(QualityNormalizationPolicy.GlamourPrismId, false, false, false) < candidate.PrismCount)
            throw new InvalidOperationException($"需要 {candidate.PrismCount} 个幻象棱晶，当前不足。");
        foreach (var item in candidate.Additional)
        {
            var key = KeyOf(item);
            if (key == null || key.Value.HighQuality || !Valid(key.Value)) throw new InvalidOperationException("待补齐的 NQ 装备已改变或被职业配装保护。");
        }
    }

    internal void Tick()
    {
        if (!IsRunning) return;
        try
        {
            var now = Environment.TickCount64;
            if (now > deadline) { Stop("等待游戏响应超时，未重复操作。"); return; }
            if (now < nextAction) return;
            nextAction = now + 350;
            if ((!plugin.IsDresserOpen && !OutsideDresser) || plugin.IsCabinetOpen
                || Plugin.Player.ContentId != characterId || Plugin.ClientState.TerritoryType != territoryId || plan == null)
            { Stop("收纳界面或角色所在区域已改变。"); return; }
            RequireSameDresser();
            if (Plugin.Conditions[ConditionFlag.InCombat] || Plugin.Conditions[ConditionFlag.BetweenAreas]
                || Plugin.Conditions[ConditionFlag.BetweenAreas51] || Plugin.Conditions[ConditionFlag.WatchingCutscene])
            { Stop("角色正在战斗、切换区域或观看过场，已停止。"); return; }
            var manager = MirageManager.Instance();
            if (!OutsideDresser && (manager == null || !manager->PrismBoxLoaded)) return;
            if (phase != Phase.LowerConfirm && phase != Phase.LowerWait && Visible("SelectYesno")) { Stop("出现其他确认窗口，已停止。"); return; }
            switch (phase)
            {
                case Phase.Restore:
                    if (plugin.QualityPreview.IsAllocated) return;
                    RequireContext();
                    plugin.Inventory.Refresh();
                    if (!plugin.Inventory.IsComplete || !plugin.Inventory.AreOutfitsLoaded) return;
                    Preflight(plan);
                    baseline.Clear();
                    foreach (var item in plugin.Inventory.Equipment) if (KeyOf(item) is { } key) baseline.Add(key);
                    restored.Clear(); handIns.Clear(); lowerIndex = 0;
                    foreach (var item in plan.Additional) handIns.Add(KeyOf(item)!.Value);
                    if (nativeRestore.Open(plan))
                    {
                        SetPhase(Phase.RestoreSelect);
                        Status = "正在打开原生取出窗口。";
                    }
                    break;
                case Phase.RestoreSelect:
                    Preflight(plan);
                    if (nativeRestore.SelectAll(plan)) SetPhase(Phase.RestoreSubmit);
                    break;
                case Phase.RestoreSubmit:
                    Preflight(plan);
                    if (nativeRestore.SubmitSelection(plan)) SetPhase(Phase.RestoreConfirm);
                    break;
                case Phase.RestoreConfirm:
                    Preflight(plan);
                    var restoreButton = nativeRestore.FinalButton(plan, out var restoreDialog);
                    if (restoreButton == null) return;
                    SetPhase(Phase.RestoreWait); // Latch before the only inventory-changing click.
                    restoreAccepted = true;
                    if (!OutfitAutoCompleter.ClickButton(restoreDialog, restoreButton))
                    {
                        restoreAccepted = false;
                        Stop("未找到原生取出确认按钮的点击事件。");
                    }
                    else Status = "已确认原生取出，等待游戏更新装备。";
                    break;
                case Phase.RestoreWait:
                    plugin.Inventory.Refresh();
                    if (!plugin.Inventory.IsComplete || !plugin.Inventory.AreOutfitsLoaded) return;
                    if (manager->PrismBoxItemIds.Contains(plan.RawStoredId)) return;
                    restored.Clear();
                    foreach (var id in plan.Stored.Pieces)
                    {
                        var matches = plugin.Inventory.Equipment.Where(item => item.ItemId == id && item.HighQuality)
                            .Select(KeyOf).Where(key => key.HasValue && !baseline.Contains(key.Value)).Select(key => key!.Value).ToArray();
                        if (matches.Length == 0) return;
                        if (matches.Length != 1) { Stop("无法唯一识别取出的 HQ 部件。"); return; }
                        restored.Add(matches[0]);
                    }
                    SetPhase(Phase.CloseSelection); break;
                case Phase.CloseSelection:
                    RequireSameDresser();
                    SetPhase(Phase.WaitSelectionClosed);
                    CloseNative("MiragePrismPrismBoxCrystallize");
                    Status = "正在关闭投影台，等待解除降低品质限制。";
                    break;
                case Phase.WaitSelectionClosed:
                    if (Visible("MiragePrismPrismBoxCrystallize")) return;
                    SetPhase(Phase.CloseDresser);
                    break;
                case Phase.CloseDresser:
                    RequireSameDresser();
                    SetPhase(Phase.WaitDresserClosed);
                    CloseNative(Plugin.DresserAddonName);
                    break;
                case Phase.WaitDresserClosed:
                    if (!CanLower) return;
                    SetPhase(Phase.Lower);
                    break;
                case Phase.Lower:
                    if (!CanLower) return;
                    if (lowerIndex >= restored.Count) { SetPhase(Phase.ReopenDresser); break; }
                    var target = restored[lowerIndex];
                    if (!ValidRestored(target) || Registered(target.ItemId, false)) { Stop("取出装备已移动或受到职业配装保护。"); return; }
                    var agent = AgentInventoryContext.Instance();
                    if (agent == null || agent->BlockingAddonId != 0) return;
                    SetPhase(Phase.LowerConfirm);
                    agent->LowerItemQuality(Read(target), (InventoryType)target.Container, target.Slot, 0);
                    Status = $"正在将部件降为 NQ：{lowerIndex + 1} / {restored.Count}";
                    break;
                case Phase.LowerConfirm: ConfirmLower(); break;
                case Phase.LowerWait:
                    var original = restored[lowerIndex];
                    var nq = original with { HighQuality = false };
                    if (Matches(nq))
                    {
                        if (Visible("SelectYesno")) return;
                        var inventory = InventoryManager.Instance();
                        if (inventory == null || inventory->GetInventoryItemCount(original.ItemId, true, true, true) != 0)
                        { Stop("降低品质后的 HQ/NQ 来源不一致。"); return; }
                        handIns.Add(nq); lowerIndex++; SetPhase(Phase.Lower);
                    }
                    else if (!Matches(original)) Stop("降低品质时装备来源发生改变。");
                    break;
                case Phase.ReopenDresser:
                    if (!CanLower) return;
                    var dresser = RequireSameDresser();
                    var context = AgentInventoryContext.Instance();
                    var targets = TargetSystem.Instance();
                    if (context == null || context->BlockingAddonId != 0 || targets == null || !dresser.IsTargetable) return;
                    if (handIns.Count != plan.PrismCount || handIns.Any(key => key.HighQuality || !Valid(key)))
                    { Stop("待存回的 NQ 装备已改变。"); return; }
                    SetPhase(Phase.WaitDresserOpen); // Interact once; never retry after a timeout.
                    targets->InteractWithObject((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)dresser.Address, true);
                    Status = "正在重新打开原投影台，准备存回 NQ 套装。";
                    break;
                case Phase.WaitDresserOpen:
                    RequireSameDresser();
                    if (!plugin.IsDresserOpen || manager == null || !manager->PrismBoxRequested || !manager->PrismBoxLoaded) return;
                    plugin.Inventory.Refresh();
                    if (!plugin.Inventory.IsComplete || !plugin.Inventory.AreOutfitsLoaded) return;
                    SetPhase(Phase.Store);
                    break;
                case Phase.Store: Store(manager); break;
                case Phase.StoreWait:
                    for (var index = 0; index < 800; index++)
                    {
                        if (manager->PrismBoxItemIds[index] != plan.Definition.Id) continue;
                        if (handIns.Any(key => Matches(key) || !manager->IsSetSlotUnlocked((uint)index,
                            Array.IndexOf(plan.Definition.Pieces.ToArray(), key.ItemId)))) return;
                        phase = Phase.Idle;
                        restoreAccepted = false;
                        Status = $"已转为 NQ 并存回：{plan.Definition.Name}，共 {handIns.Count} 件。";
                        plan = null; plugin.RequestRefresh(); return;
                    }
                    break;
            }
        }
        catch (Exception error) { Plugin.Log.Error(error, "Quality normalization stopped"); Stop(error.Message); }
    }

    private void ConfirmLower()
    {
        // The owned confirmation itself can mark the player occupied. It must not
        // satisfy the idle gate used before opening it, but the dresser must stay closed.
        if (plugin.IsDresserOpen || Plugin.Conditions[ConditionFlag.OccupiedInQuestEvent]) return;
        var target = restored[lowerIndex];
        if (Matches(target with { HighQuality = false })) { SetPhase(Phase.LowerWait); return; }
        var agent = AgentInventoryContext.Instance();
        var dialog = Plugin.GameGui.GetAddonByName("SelectYesno");
        if (agent == null || dialog.IsNull || !dialog.IsReady || !dialog.IsVisible) return;
        var module = RaptureAtkModule.Instance();
        nint handler = 0;
        ulong eventKind = 0;
        if (module != null && module->AddonCallbackMapping.TryGetValue(dialog.Id, out var callback, false))
        {
            handler = (nint)callback.EventInterface;
            eventKind = callback.EventKind;
        }
        var owned = handler == (nint)agent;
        var yesno = (AddonSelectYesno*)dialog.Address;
        var prompt = yesno->PromptText == null ? "" : SeString.Parse(yesno->PromptText->NodeText.AsSpan()).TextValue;
        var name = Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Item>().GetRow(target.ItemId).Name.ExtractText();
        var valid = ValidRestored(target);
        var protectedNq = Registered(target.ItemId, false);
        if (!QualityNormalizationPolicy.CanConfirmLower(owned, dialog.Id, agent->BlockingAddonId,
            target, (uint)agent->BlockedInventoryId, agent->BlockedInventorySlotId, valid,
            protectedNq, prompt, name, eventKind))
        {
            Plugin.Log.Warning("Lower confirmation mismatch: owner={Owned}, event={Kind}, dialog={Dialog}, blocker={Blocker}, "
                + "expected={Item}/{Container}/{Slot}, actual={ActualContainer}/{ActualSlot}, valid={Valid}, protected={Protected}, prompt={Prompt}.",
                owned, eventKind, dialog.Id, agent->BlockingAddonId, target.ItemId, target.Container, target.Slot,
                agent->BlockedInventoryId, agent->BlockedInventorySlotId, valid, protectedNq, prompt);
            Stop("降低品质确认窗口与本次装备不符，未确认。"); return;
        }
        var checkbox = yesno->ConfirmCheckBox;
        if (checkbox == null || !checkbox->AtkResNode->IsVisible())
        { Stop("未找到降低品质的确认勾选框，未确认。"); return; }
        if (!checkbox->IsChecked)
        {
            checkbox->SetChecked(true);
            if (!OutfitAutoCompleter.ClickButton((AtkUnitBase*)yesno, (AtkComponentButton*)checkbox))
            {
                checkbox->SetChecked(false);
                Stop("未找到降低品质勾选框的点击事件，未确认。");
            }
            return; // Validate again and click Yes on a later update.
        }
        if (!QualityNormalizationPolicy.CanClickLowerYes(true, true, checkbox->IsChecked)
            || yesno->YesButton == null || !yesno->YesButton->IsEnabled) return;
        SetPhase(Phase.LowerWait);
        if (!OutfitAutoCompleter.ClickButton((AtkUnitBase*)yesno, yesno->YesButton)) Stop("无法识别降低品质的确认按钮。");
    }

    private void Store(MirageManager* manager)
    {
        if (plan == null) return;
        if (handIns.Count != plan.PrismCount || handIns.Select(key => key.ItemId).Distinct().Count() != handIns.Count
            || handIns.Any(key => key.HighQuality || !Valid(key)))
        { Stop("待存回的 NQ 装备不完整或已改变。"); return; }
        if (manager->PrismBoxItemIds.Contains(plan.RawStoredId) || manager->PrismBoxItemIds.Contains(plan.Definition.Id))
        { Stop("套装记录已改变，未重复创建。"); return; }
        var inventory = InventoryManager.Instance();
        if (inventory == null || inventory->GetInventoryItemCount(QualityNormalizationPolicy.GlamourPrismId, false, false, false) < handIns.Count)
        { Stop("幻象棱晶不足，未存回。"); return; }
        var packed = OutfitSubmissionLayout.Pack(plan.Definition.Pieces,handIns,false);
        InventoryType* containers = stackalloc InventoryType[OutfitSubmissionLayout.Capacity];
        ushort* slots = stackalloc ushort[OutfitSubmissionLayout.Capacity];
        for (var i = 0; i < OutfitSubmissionLayout.Capacity; i++) { containers[i] = InventoryType.Invalid; slots[i] = 0; }
        for (var i = 0; i < packed.Length; i++) { containers[i] = (InventoryType)packed[i].Container; slots[i] = packed[i].Slot; }
        Plugin.Log.Information("Restoring NQ outfit {Id} using {Count} packed sources.",plan.Definition.Id,packed.Length);
        SetPhase(Phase.StoreWait);
        Status = "正在将 NQ 部件存回套装，等待游戏确认。";
        if (!manager->StoreNewOutfit(plan.Definition.Id, containers, slots)) Stop("游戏未接受 NQ 套装存入。");
    }

    private static void CloseNative(string name)
    {
        var addon = Plugin.GameGui.GetAddonByName(name);
        if (!addon.IsNull && addon.IsReady && addon.IsVisible)
            ((AtkUnitBase*)addon.Address)->FireCloseCallback();
    }

    private static bool Visible(string name) { var addon = Plugin.GameGui.GetAddonByName(name); return !addon.IsNull && addon.IsReady && addon.IsVisible; }
    private static CompletionItemKey? KeyOf(OutfitEquipment item) => OutfitInventoryReader.TryResolveContainer(item.Location, out var container) && item.Slot > 0
        ? new(item.ItemId, (uint)container, (ushort)(item.Slot - 1), item.HighQuality) : null;
    private static InventoryItem* Read(CompletionItemKey key)
    {
        var manager = InventoryManager.Instance(); if (manager == null) return null;
        var container = manager->GetInventoryContainer((InventoryType)key.Container);
        return container != null && container->IsLoaded && key.Slot < container->Size ? container->GetInventorySlot(key.Slot) : null;
    }
    private static bool Matches(CompletionItemKey key)
    {
        var item = Read(key); return item != null && !item->IsSymbolic && item->Quantity == 1 && item->ItemId == key.ItemId
            && ((item->Flags & InventoryItem.ItemFlags.HighQuality) != 0) == key.HighQuality;
    }
    private static bool ValidRestored(CompletionItemKey key)
    {
        var inventory = InventoryManager.Instance();
        return key.HighQuality && inventory != null && Valid(key)
            && inventory->GetInventoryItemCount(key.ItemId, true, true, true) == 1;
    }
    private static bool Valid(CompletionItemKey key) => Matches(key) && Read(key)->Condition >= 30000 && !Registered(key.ItemId, key.HighQuality);
    private static bool Registered(uint id, bool hq)
    {
        var module = RaptureGearsetModule.Instance(); if (module == null) return true;
        var raw = id + (hq ? 1_000_000u : 0);
        foreach (ref var set in module->Entries)
            if ((set.Flags & RaptureGearsetModule.GearsetFlag.Exists) != 0)
                foreach (ref var piece in set.Items) if (piece.ItemId == raw) return true;
        return false;
    }
    private void SetPhase(Phase next) { phase = next; deadline = Environment.TickCount64 + 15_000; nextAction = Environment.TickCount64 + 350; }
}
