using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SuitKing;

internal sealed unsafe class ExpertDeliverySubmitter(Plugin plugin)
{
    private enum Phase { Idle, Select, Dialog, Result }
    private Phase phase;
    private readonly ExpertDeliveryPreviewReader reader = new();
    private ExpertDeliveryCandidate[] approved = [];
    private ExpertDeliveryCandidate? current;
    private int index;
    private int stopLevel;
    private ulong character;
    private byte company;
    private uint territory;
    private long deadline;
    private long before;
    private bool hqConfirmed;
    private string? error;
    private CompletionItemKey? locating;
    private bool searchStarted;
    private int lastScroll;
    private long locateAfter;
    internal bool IsRunning => phase != Phase.Idle;
    internal bool WaitingForNative => phase is Phase.Dialog or Phase.Result;
    internal string Status { get; private set; } = "";
    internal void NotifyError(string message) { if (IsRunning) error = message; }
    internal void Start(int level, ExpertDeliveryPreviewPlan plan)
    {
        if (IsRunning || plan.Items.Count == 0) return;
        try
        {
            if (!plugin.Delivery.ContextOpen || Has("GrandCompanySupplyReward") || Has("SelectYesno"))
                throw new InvalidOperationException("请先关闭已有提交确认窗口。");
            if (plugin.Completion.IsRunning || plugin.Creation.IsRunning || plugin.Quality.IsRunning || plugin.CabinetStore.IsRunning)
                throw new InvalidOperationException("请先停止其他装备操作。");
            var player = PlayerState.Instance();
            if (player == null || player->GrandCompany == 0) throw new InvalidOperationException("军团数据未就绪。");
            company = player->GrandCompany; character = Plugin.Player.ContentId; territory = Plugin.ClientState.TerritoryType;
            approved = plan.Items.ToArray(); index = 0; stopLevel = level; error = null; current = null; locating = null;
            reader.Reset(); SetPhase(Phase.Select);
            Status = $"准备提交 {approved.Length} 件，停止品级 {stopLevel}。";
        }
        catch (Exception ex) { plugin.DeliveryPreview.FinishSubmission(ex.Message); }
    }
    internal void Stop(string reason)
    {
        if (!IsRunning) return;
        phase = Phase.Idle; current = null;
        Status = $"{reason} 已提交 {index} / {approved.Length} 件。";
        plugin.DeliveryPreview.FinishSubmission(Status);
    }
    internal void Tick()
    {
        if (!IsRunning) return;
        try
        {
            if (error != null) { Stop("游戏提示："+error); return; }
            var player = PlayerState.Instance();
            if (plugin.IsDresserOpen || plugin.IsCabinetOpen || plugin.Completion.IsRunning || plugin.Creation.IsRunning
                || plugin.Quality.IsRunning || plugin.CabinetStore.IsRunning)
            { Stop("出现其他装备操作，已停止筹备。"); return; }
            if (Plugin.Player.ContentId != character || player == null || player->GrandCompany != company
                || Plugin.ClientState.TerritoryType != territory
                || Plugin.Conditions[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas]
                || Plugin.Conditions[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas51])
            { Stop("角色或军团已改变。"); return; }
            if (Environment.TickCount64 > deadline) { Stop("等待游戏响应超时，未重复提交。"); return; }
            if (phase == Phase.Select) SelectNext();
            else if (phase == Phase.Dialog) ConfirmDialog();
            else if (phase == Phase.Result) WaitResult();
        }
        catch (Exception ex) { Plugin.Log.Warning(ex,"Expert delivery stopped"); Stop(ex.Message); }
    }
    private void SelectNext()
    {
        if (!plugin.Delivery.ContextOpen || Has("SelectYesno") || Has("GrandCompanySupplyReward"))
            throw new InvalidOperationException("筹备页已改变或出现其他确认窗口。");
        if (index >= approved.Length) { Stop("本次清单已提交完成。"); return; }
        if (!reader.TryRead(stopLevel,out var plan)) return;
        if (plan!.Items.Count == 0) { Stop(plan.StopReason); return; }
        current = approved[index];
        if (!ExpertDeliverySubmitPolicy.SameTarget(current,plan.Items[0]))
            throw new InvalidOperationException("清单顺序、来源或奖励已改变，请重新预览。");
        var handle = Plugin.GameGui.GetAddonByName(ExpertDeliveryPreviewController.AddonName);
        var addon = (AddonGrandCompanySupplyList*)handle.Address;
        var list = addon->ExpertDeliveryList;
        if (list == null || list->ListLength is < 1 or > 4096) return;
        if (locating != current.Key)
        { locating = current.Key; searchStarted = false; lastScroll = -1; locateAfter = 0; }
        if (Environment.TickCount64 < locateAfter) return;
        var rows = new List<ExpertDeliveryNativeRow>();
        var visited = new HashSet<nint>();
        if (list->ItemRendererList == null || list->AllocatedItemRendererListLength is < 0 or > 4096) return;
        for (var slot=0; slot<Math.Min(list->ListLength,list->AllocatedItemRendererListLength); slot++)
        {
            var renderer=list->ItemRendererList[slot].AtkComponentListItemRenderer;
            if (renderer==null || !visited.Add((nint)renderer) || renderer->ListItemIndex<0
                || renderer->ListItemIndex>=list->ListLength || renderer->OwnerNode==null
                || !renderer->OwnerNode->AtkResNode.IsVisible()) continue;
            var uld=&renderer->UldManager;
            if (uld->NodeList==null || uld->NodeListCount>100) continue;
            var name=""; var reward=-1;
            for (var n=0;n<uld->NodeListCount;n++)
            {
                var node=uld->NodeList[n];
                if (node==null || node->Type!=NodeType.Text || node->NodeId>=100_000_000) continue;
                var text=SeString.Parse(((AtkTextNode*)node)->NodeText.AsSpan()).TextValue;
                if (ExpertDeliverySubmitPolicy.NameKey(text)==ExpertDeliverySubmitPolicy.NameKey(current.Name)) name=text;
                if (int.TryParse(text.Replace(",",""),out var number) && number==current.SealReward) reward=number;
            }
            if (name.Length>0) rows.Add(new(renderer->ListItemIndex,name,reward,!list->ItemRendererList[slot].IsDisabled));
        }
        var selected=ExpertDeliverySubmitPolicy.FindNativeRow(rows,current.Name,current.SealReward,list->ListLength);
        if (selected==-2) throw new InvalidOperationException("原生列表存在多个同名同奖励的装备行，未提交。");
        if (selected<0)
        {
            var scroll=list->ScrollBarComponent;
            if (scroll==null) throw new InvalidOperationException("原生行尚未显示且无法滚动，未提交。");
            var next=searchStarted ? Math.Min(scroll->ScrollMaxPosition,lastScroll+Math.Max(1,list->NumVisibleItems-1)) : 0;
            if (searchStarted && next<=lastScroll)
                throw new InvalidOperationException("已扫描原生列表，未找到目标装备与奖励，未提交。");
            searchStarted=true; lastScroll=next; scroll->SetScrollPosition(next);
            locateAfter=Environment.TickCount64+180;
            Status=$"正在定位原生装备行：{current.Name}";
            return;
        }
        if (!Live(current)) throw new InvalidOperationException("装备已移动、穿戴或登记到职业配装。");
        before = Balance(out _); hqConfirmed = false;
        SetPhase(Phase.Dialog); // The native click may finalize the list immediately.
        Status = $"正在提交 {index} / {approved.Length} 件：{current.Name}（品级 {current.ItemLevel}）";
        list->DispatchItemEvent(selected,AtkEventType.ListItemClick);
    }
    private void ConfirmDialog()
    {
        if (current == null) return;
        var yesno = Get("SelectYesno");
        if (yesno != null)
        {
            var agent = AgentGrandCompanySupply.Instance();
            var module=RaptureAtkModule.Instance();
            var owned=agent!=null && module!=null && module->AddonCallbackMapping.TryGetValue(yesno->Id,out var cb,false)
                && (nint)cb.EventInterface==(nint)agent && cb.EventKind==4;
            var dialog=(AddonSelectYesno*)yesno;
            var text=dialog->PromptText==null ? "" : SeString.Parse(dialog->PromptText->NodeText.AsSpan()).TextValue;
            if (!ExpertDeliverySubmitPolicy.IsHqPrompt(current.Key.HighQuality,owned,4,text) || !Live(current))
                throw new InvalidOperationException("出现不属于本次 HQ 装备的确认，已停止。");
            if (hqConfirmed) return; // Wait for the already-clicked prompt to finish closing.
            CheckCapacity();
            if (dialog->YesButton==null || !dialog->YesButton->IsEnabled) return;
            hqConfirmed=true; // Never click the same HQ confirmation twice.
            if (!OutfitAutoCompleter.ClickButton(yesno,dialog->YesButton))
                throw new InvalidOperationException("未找到 HQ 确认按钮事件。");
            return;
        }
        var reward=(AddonGrandCompanySupplyReward*)Get("GrandCompanySupplyReward");
        if (reward==null)
        {
            // Some native variants may finalize directly after their owned HQ prompt.
            if (hqConfirmed && !SourceMatches(current.Key)) WaitResult();
            return;
        }
        var module2=RaptureAtkModule.Instance();
        var ownedReward=module2!=null && module2->AddonCallbackMapping.TryGetValue(reward->Id,out var entry,false)
            && (nint)entry.EventInterface!=0 && entry.EventKind==2;
        if (reward->AtkValues==null || reward->AtkValuesCount<12 || reward->AtkValues[3].Type!=AtkValueType.String
            || reward->AtkValues[4].Type!=AtkValueType.String || reward->AtkValues[9].Type!=AtkValueType.UInt)
            throw new InvalidOperationException("原生提交奖励数据不完整，未确认。");
        var title=reward->AtkValues[3].String.AsDalamudSeString().TextValue;
        var itemName=reward->AtkValues[4].String.AsDalamudSeString().TextValue;
        var held=Balance(out var max);
        var actual=current with {Protected=!Live(current)};
        if (!ExpertDeliverySubmitPolicy.CanConfirm(current,actual,stopLevel,held,max,ownedReward && title=="筹备稀有品",
            ExpertDeliverySubmitPolicy.NameKey(itemName)==ExpertDeliverySubmitPolicy.NameKey(current.Name),
            checked((int)reward->AtkValues[9].UInt)) || current.Key.HighQuality && !hqConfirmed)
            throw new InvalidOperationException("奖励窗口、品级、军票或装备来源不符，未确认。");
        if (reward->DeliverButton==null || !reward->DeliverButton->IsEnabled) return;
        before=held; SetPhase(Phase.Result); // Latch before the irreversible native confirmation.
        if (!OutfitAutoCompleter.ClickButton((AtkUnitBase*)reward,reward->DeliverButton))
            throw new InvalidOperationException("未找到原生提交按钮事件。");
    }
    private void CheckCapacity()
    {
        var held=Balance(out var max);
        if (current==null || held>=max || current.SealReward>max-held || current.ItemLevel>=stopLevel)
            throw new InvalidOperationException("军票容量不足或已达到停止品级。");
    }
    private void WaitResult()
    {
        if (current==null || Has("GrandCompanySupplyReward")) return;
        if (Has("SelectYesno")) throw new InvalidOperationException("提交后出现其他确认窗口，已停止。");
        var inv=InventoryManager.Instance();
        var container=inv==null ? null : inv->GetInventoryContainer((InventoryType)current.Key.Container);
        var readable=container!=null && container->IsLoaded && container->Items!=null && current.Key.Slot<container->Size;
        var held=Balance(out _);
        if (!ExpertDeliverySubmitPolicy.Acknowledged(readable,SourceMatches(current.Key),false,before,held,current.SealReward)) return;
        if (!plugin.Delivery.ContextOpen) return; // Wait for the native list to return before advancing.
        index++; current=null; reader.Reset(); SetPhase(Phase.Select);
    }
    private long Balance(out long max)
    {
        var inv=InventoryManager.Instance();
        if (inv==null) throw new InvalidOperationException("军票数据未就绪。");
        var held=inv->GetCompanySeals(company); max=inv->GetMaxCompanySeals(company);
        if (max<=0 || held>max) throw new InvalidOperationException("军票余额异常。");
        return held;
    }
    private static bool Live(ExpertDeliveryCandidate item)
    {
        if (OutfitInventoryReader.ContainerName((InventoryType)item.Key.Container)==null || !SourceMatches(item.Key)) return false;
        var sets=RaptureGearsetModule.Instance(); if (sets==null) return false;
        var raw=item.Key.ItemId+(item.Key.HighQuality?1_000_000u:0);
        foreach(ref var set in sets->Entries)
            if((set.Flags & RaptureGearsetModule.GearsetFlag.Exists)!=0)
                foreach(ref var piece in set.Items) if(piece.ItemId==raw) return false;
        return true;
    }
    private static bool SourceMatches(CompletionItemKey key)
    {
        var inv=InventoryManager.Instance(); if(inv==null) return false;
        var c=inv->GetInventoryContainer((InventoryType)key.Container);
        if(c==null || !c->IsLoaded || c->Items==null || key.Slot>=c->Size) return false;
        var item=c->GetInventorySlot(key.Slot);
        return item!=null && !item->IsSymbolic && item->Quantity==1 && item->ItemId==key.ItemId
            && ((item->Flags & InventoryItem.ItemFlags.HighQuality)!=0)==key.HighQuality;
    }
    private static AtkUnitBase* Get(string name)
    {
        var h=Plugin.GameGui.GetAddonByName(name);
        return !h.IsNull && h.IsReady && h.IsVisible ? (AtkUnitBase*)h.Address : null;
    }
    private static bool Has(string name)=>Get(name)!=null;
    private void SetPhase(Phase value) {phase=value; deadline=Environment.TickCount64+15_000;}
}
