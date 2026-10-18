using System.Runtime.CompilerServices;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SuitKing;

// Uses the CN dresser's context action, selection window and final confirmation.
// No inventory request is sent until the validated final button is clicked.
internal sealed unsafe class NativeOutfitRestorer
{
    internal const string SelectionName = "MiragePrismPrismSetRevert";
    internal const string ConfirmationName = "MiragePrismPrismSetRevertC";
    private int contextIndex = -1;
    private ushort selectionId;

    internal bool Open(QualityNormalizationPlan plan)
    {
        var agent = AgentMiragePrismPrismBox.Instance();
        if (agent == null || agent->Data == null || agent->Data->IsPopulatingList) return false;
        if (Addon(SelectionName) != null || Addon(ConfirmationName) != null)
            throw new InvalidOperationException("已有取出窗口，请先关闭后重新预览。");
        contextIndex = -1;
        for (var index = 0; index < agent->Data->PrismBoxItems.Length; index++)
        {
            ref var item = ref agent->Data->PrismBoxItems[index];
            if (!OutfitRestorePolicy.MatchesSource(plan.RawStoredId, plan.Stored.BoxIndex,
                    item.ItemId, item.Slot, IsFullOutfit(ref item))) continue;
            if (contextIndex >= 0) throw new InvalidOperationException("原生列表中无法唯一识别目标套装。");
            contextIndex = index;
        }
        if (contextIndex < 0) throw new InvalidOperationException("原生投影台列表未找到目标 HQ 套装，请重新打开投影台。");
        selectionId = 0;
        // CN context-menu Restore action: event kind 9 uses TempContextItemIndex.
        agent->Data->TempContextItemIndex = contextIndex;
        AtkValue result = default;
        agent->ReceiveEvent(&result, null, 0, 9);
        Plugin.Log.Information("Opened native outfit retrieval: outfit={Id}, slot={Slot}, context={Context}, mask={Mask:X4}.",
            plan.RawStoredId, plan.Stored.BoxIndex, contextIndex, plan.RestoreMask);
        return true;
    }

    internal bool SelectAll(QualityNormalizationPlan plan)
    {
        var addon = Selection(plan);
        if (addon == null) return false;
        // The plan retrieves every stored piece, so native Select All is exact.
        return Click(addon, 9);
    }

    internal bool SubmitSelection(QualityNormalizationPlan plan)
    {
        var addon = Selection(plan);
        return addon != null && Click(addon, 11);
    }

    internal AtkComponentButton* FinalButton(QualityNormalizationPlan plan, out AtkUnitBase* dialog)
    {
        dialog = Addon(ConfirmationName);
        if (dialog == null) return null;
        var agent = AgentMiragePrismPrismBox.Instance();
        var source = MatchesContext(plan, agent);
        var owned = agent != null && agent->ContextAddon2Id == dialog->Id && Owned(dialog, agent)
            && selectionId != 0 && agent->ContextAddonId == selectionId;
        if (dialog->AtkValues == null || dialog->AtkValuesCount < 3)
            throw new InvalidOperationException("原生取出确认数据不完整。");
        var promptNode = dialog->GetTextNodeById(2);
        var prompt = promptNode == null ? "" : SeString.Parse(promptNode->NodeText.AsSpan()).TextValue;
        // The final addon receives the full 32-bit complement, not an 11-bit mask.
        var validTypes = dialog->AtkValues[0].Type == AtkValueType.UInt && dialog->AtkValues[1].Type == AtkValueType.UInt;
        if (!OutfitRestorePolicy.CanConfirm(owned, plan.RestoreMask,
                agent == null || agent->Data == null ? 0 : SelectedMask(agent->Data),
                plan.RawStoredId, dialog->AtkValues[0].UInt, dialog->AtkValues[1].UInt, source && validTypes, prompt))
            throw new InvalidOperationException("取出确认与本次 HQ 套装或所选部件不符，未确认。");
        var button = dialog->GetComponentButtonById(14);
        return button != null && button->IsEnabled ? button : null;
    }

    private AtkUnitBase* Selection(QualityNormalizationPlan plan)
    {
        var addon = Addon(SelectionName);
        if (addon == null) return null;
        var agent = AgentMiragePrismPrismBox.Instance();
        if (!MatchesContext(plan, agent) || agent->ContextAddonId != addon->Id || !Owned(addon, agent)
            || selectionId != 0 && selectionId != addon->Id)
            throw new InvalidOperationException("原生取出窗口已改变，未继续操作。");
        var manager = MirageManager.Instance();
        if (manager == null || addon->AtkValues == null || addon->AtkValuesCount < 3
            || addon->AtkValues[0].Type != AtkValueType.UInt || addon->AtkValues[1].Type != AtkValueType.UInt
            || addon->AtkValues[0].UInt != plan.RawStoredId
            || addon->AtkValues[1].UInt != (uint)(manager->PrismBoxStain0Ids[plan.Stored.BoxIndex]
                | manager->PrismBoxStain1Ids[plan.Stored.BoxIndex] << 8))
            throw new InvalidOperationException("原生取出套装内容已变化，未继续操作。");
        selectionId = addon->Id;
        return addon;
    }

    private bool MatchesContext(QualityNormalizationPlan plan, AgentMiragePrismPrismBox* agent)
    {
        if (agent == null || agent->Data == null || agent->Data->IsPopulatingList
            || contextIndex < 0 || contextIndex >= agent->Data->PrismBoxItems.Length
            || agent->Data->TempContextItemIndex != contextIndex) return false;
        ref var item = ref agent->Data->PrismBoxItems[contextIndex];
        return OutfitRestorePolicy.MatchesSource(plan.RawStoredId, plan.Stored.BoxIndex,
            item.ItemId, item.Slot, IsFullOutfit(ref item));
    }

    // These two CN fields are internal in the bundled FFCS assembly. Their offsets
    // were verified against the native restore handler and both live dialog stages.
    private static bool IsFullOutfit(ref PrismBoxItem item)
        => *((byte*)Unsafe.AsPointer(ref item) + 0x88) == byte.MaxValue;
    private static uint SelectedMask(MiragePrismPrismBoxData* data)
        => *(uint*)((byte*)data + 0x11AE68);

    private static bool Owned(AtkUnitBase* addon, AgentMiragePrismPrismBox* agent)
    {
        var module = RaptureAtkModule.Instance();
        return module != null && module->AddonCallbackMapping.TryGetValue(addon->Id, out var callback, false)
            && (nint)callback.EventInterface == (nint)agent;
    }

    private static bool Click(AtkUnitBase* addon, uint nodeId)
    {
        var button = addon->GetComponentButtonById(nodeId);
        if (button == null) throw new InvalidOperationException("原生取出按钮结构不符。");
        if (!button->IsEnabled) return false;
        if (!OutfitAutoCompleter.ClickButton(addon, button))
            throw new InvalidOperationException("未找到原生取出按钮的点击事件。");
        return true;
    }

    private static AtkUnitBase* Addon(string name)
    {
        var addon = Plugin.GameGui.GetAddonByName(name);
        return addon.IsNull || !addon.IsReady || !addon.IsVisible ? null : (AtkUnitBase*)addon.Address;
    }
}
