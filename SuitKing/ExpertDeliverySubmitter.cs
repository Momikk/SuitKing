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
    private readonly ExpertDeliveryTrace trace = new(
        message => Plugin.Log.Information("{DeliveryTrace}",message),
        message => Plugin.Log.Warning("{DeliveryTrace}",message));
    private string lastRows = "not captured";
    private string lastSelection = "not dispatched";
    private long phaseStarted;
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
    internal bool IsRunning => phase != Phase.Idle;
    internal bool WaitingForNative => phase is Phase.Dialog or Phase.Result;
    internal string Status { get; private set; } = "";
    internal void NotifyError(string message) { if (IsRunning) error = message; }
    internal void Start(int level, ExpertDeliveryPreviewPlan plan)
    {
        if (IsRunning || plan.Items.Count == 0) return;
        try
        {
            trace.Begin(); lastRows = "not captured"; lastSelection = "not dispatched";
            trace.Emit("StartRequested",$"version={typeof(Plugin).Assembly.GetName().Version}, count={plan.Items.Count}, stopLevel={level}");
            if (!plugin.Delivery.ContextOpen || Has("GrandCompanySupplyReward") || Has("SelectYesno") || ContextMenuOpen)
                throw new InvalidOperationException("请先关闭已有提交确认窗口或右键菜单。");
            if (plugin.Completion.IsRunning || plugin.Creation.IsRunning || plugin.Quality.IsRunning || plugin.CabinetStore.IsRunning)
                throw new InvalidOperationException("请先停止其他装备操作。");
            var player = PlayerState.Instance();
            if (player == null || player->GrandCompany == 0) throw new InvalidOperationException("军团数据未就绪。");
            company = player->GrandCompany; character = Plugin.Player.ContentId; territory = Plugin.ClientState.TerritoryType;
            approved = plan.Items.ToArray(); index = 0; stopLevel = level; error = null; current = null;
            reader.Reset(); SetPhase(Phase.Select);
            Status = $"准备提交 {approved.Length} 件，停止品级 {stopLevel}。";
            plugin.DeliveryPreview.SuspendForSubmission();
            trace.Emit("Start",$"count={approved.Length}, stopLevel={stopLevel}, company={company}, territory={territory}, seals={BalanceSnapshot()}, first={Describe(approved[0])}");
        }
        catch (Exception ex) { trace.Emit("StartRejected",ex.ToString(),true); plugin.DeliveryPreview.FinishSubmission(ex.Message); }
    }
    internal void Stop(string reason)
    {
        if (!IsRunning) return;
        trace.Emit("Stop",$"reason={reason}, phase={phase}, phaseMs={Environment.TickCount64-phaseStarted}, completed={index}/{approved.Length}, target={Describe(current)}, hqClickSent={hqConfirmed}, error={error ?? "none"}; lastSelection={lastSelection}");
        trace.Emit("LastRows",lastRows);
        LogStopContext();
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
            if (Environment.TickCount64 > deadline)
            {
                trace.Emit("Timeout",$"phase={phase}, phaseMs={Environment.TickCount64-phaseStarted}, target={Describe(current)}, hqClickSent={hqConfirmed}",true);
                Stop("等待游戏响应超时，未重复提交；详情见 /xllog。"); return;
            }
            if (phase == Phase.Select) SelectNext();
            else if (phase == Phase.Dialog) ConfirmDialog();
            else if (phase == Phase.Result) WaitResult();
        }
        catch (Exception ex) { trace.Emit("Exception",ex.ToString(),true); Stop(ex.Message); }
    }
    private void SelectNext()
    {
        if (!plugin.Delivery.ContextOpen || Has("SelectYesno") || Has("GrandCompanySupplyReward"))
            throw new InvalidOperationException("筹备页已改变或出现其他确认窗口。");
        if (plugin.DeliveryPreview.IsAllocated) { trace.Wait("phase=Select, waiting=preview release"); return; }
        if (index >= approved.Length) { Stop("本次清单已提交完成。"); return; }
        if (!reader.TryRead(stopLevel,out var plan)) { trace.Wait("phase=Select, waiting=inventory/gearset snapshot"); return; }
        if (plan!.Items.Count == 0) { Stop(plan.StopReason); return; }
        current = approved[index];
        if (!ExpertDeliverySubmitPolicy.SameTarget(current,plan.Items[0]))
        {
            trace.Emit("PlanMismatch",$"expected={Describe(current)}, refreshedFirst={Describe(plan.Items[0])}",true);
            throw new InvalidOperationException("清单顺序、来源或奖励已改变，请重新预览。");
        }
        var handle=Plugin.GameGui.GetAddonByName(ExpertDeliveryPreviewController.AddonName);
        if (handle.IsNull || !handle.IsReady || !handle.IsVisible) return;
        var addon=(AddonGrandCompanySupplyList*)handle.Address;
        var agent=AgentGrandCompanySupply.Instance();
        var nativeRows=ExpertDeliveryNativeRows.Read(agent);
        if (nativeRows==null) { trace.Wait("phase=Select, waiting=agent sorted source projection"); return; }
        var command=ExpertDeliverySubmitPolicy.SelectSource(current,plan.Items[0],nativeRows);
        if (!Live(current)) throw new InvalidOperationException("装备已移动、穿戴或登记到职业配装。");
        before=Balance(out var capacity); hqConfirmed=false;
        trace.Emit("Target",$"step={index+1}/{approved.Length}, {Describe(current)}");
        lastSelection=$"step={index+1}/{approved.Length}, target={Describe(current)}, route=AgentSortedLeftClick, command={command.EventKind}, nativeRow={command.RowIndex}, projectedCount={nativeRows.Length}, agentCount={agent->NumItems}, filter={((ExpertDeliveryNativeRows.Agent*)agent)->Filter}, updateState={command.UpdateState}, seals={before}/{capacity}";
        trace.Emit("SelectSource",lastSelection);
        lastRows=CaptureRows(addon); // Diagnostic only: cached row text never determines the source.
        trace.Emit("RowsBeforeClick",lastRows);
        trace.Emit("SourceBeforeClick",SourceSnapshot());
        LogAgentTarget();
        AtkValue* values=stackalloc AtkValue[3];
        values[0]=new() {Type=AtkValueType.Int,Int=command.EventKind};
        values[1]=new() {Type=AtkValueType.Int,Int=command.RowIndex};
        values[2]=default; // Native left-click keeps the unused third argument undefined.
        if (!ExpertDeliveryNativeRows.StillMatches(agent,nativeRows))
        { trace.Wait("phase=Select, waiting=agent sorted source projection changed before dispatch"); return; }
        SetPhase(Phase.Dialog); // Latch before the native request may finalize this window.
        Status=$"正在提交 {index} / {approved.Length} 件：{current.Name}（品级 {current.ItemLevel}）";
        // Verified normal left-click: Int(1), Int(filtered agent row index), Undefined.
        // Resolve physical identity through the SAME sorted indices and filters as the callback.
        // The paged addon cache and its allocation capacity never determine selection.
        // Command 3 is a right-click context-menu request and is intentionally not used.
        addon->FireCallback(3,values,command.UpdateState);
        trace.Emit("SelectDispatched",lastSelection);
    }
    private static string CaptureRows(AddonGrandCompanySupplyList* addon)
    {
        try
        {
            var list=addon->ExpertDeliveryList;
            if (list==null || list->ItemRendererList==null || list->AllocatedItemRendererListLength is <0 or >4096)
                return "renderer diagnostics unavailable; source selection does not require renderers";
            var rows=new List<string>(); var seen=new HashSet<nint>();
            for (var i=0;i<Math.Min(list->ListLength,list->AllocatedItemRendererListLength);i++)
            {
                var renderer=list->ItemRendererList[i].AtkComponentListItemRenderer;
                if (renderer==null || !seen.Add((nint)renderer) || renderer->OwnerNode==null
                    || !renderer->OwnerNode->AtkResNode.IsVisible()) continue;
                var uld=&renderer->UldManager;
                if (uld->NodeList==null || uld->NodeListCount>100) continue;
                var texts=new List<string>();
                for (var n=0;n<uld->NodeListCount;n++)
                {
                    var node=uld->NodeList[n];
                    if (node!=null && node->Type==NodeType.Text && node->NodeId<100_000_000)
                        texts.Add(ExpertDeliveryTrace.Clean(SeString.Parse(((AtkTextNode*)node)->NodeText.AsSpan()).TextValue));
                }
                rows.Add($"pool={i}, row={renderer->ListItemIndex}, text=[{string.Join("|",texts)}]");
            }
            return $"diagnosticOnly=true, addon={addon->Id}, count={list->ListLength}, sort={addon->SelectedSortBy}, filter={addon->SelectedFilter}; {string.Join("; ",rows)}";
        }
        catch (Exception ex) { return "unavailable: "+ex.GetType().Name; }
    }
    private void ConfirmDialog()
    {
        if (current == null) return;
        if (ContextMenuOpen) throw new InvalidOperationException("出现装备右键菜单，未提交；详情见 /xllog。");
        var yesno = Get("SelectYesno");
        if (yesno != null)
        {
            var agent = AgentGrandCompanySupply.Instance();
            var module=RaptureAtkModule.Instance();
            ulong kind=ulong.MaxValue; nint owner=0; var mapped=false;
            if (module!=null && module->AddonCallbackMapping.TryGetValue(yesno->Id,out var cb,false))
            { mapped=true; owner=(nint)cb.EventInterface; kind=cb.EventKind; }
            var owned=agent!=null && mapped && owner==(nint)agent && kind==4;
            var dialog=(AddonSelectYesno*)yesno;
            var text=dialog->PromptText==null ? "" : SeString.Parse(dialog->PromptText->NodeText.AsSpan()).TextValue;
            var live=Live(current);
            var pending=ExpertDeliveryNativeRows.PendingMatches(agent,yesno->Id,current.Key);
            var accepted=ExpertDeliverySubmitPolicy.IsHqPrompt(current.Key.HighQuality,owned,kind,text) && live && (hqConfirmed || pending);
            trace.Dialog($"type=HQ, step={index+1}, addon={yesno->Id}, target={Describe(current)}, prompt={ExpertDeliveryTrace.Clean(text)}, mapped={mapped}, owner=0x{owner:X}, agent=0x{(nint)agent:X}, kind={kind}, owned={owned}, live={live}, pendingMatches={pending}, accepted={accepted}, hqClickSent={hqConfirmed}, buttonEnabled={dialog->YesButton!=null && dialog->YesButton->IsEnabled}",!accepted);
            if (!accepted)
                throw new InvalidOperationException("出现不属于本次 HQ 装备的确认，已停止。");
            if (hqConfirmed) return; // Wait for the already-clicked prompt to finish closing.
            CheckCapacity();
            if (dialog->YesButton==null || !dialog->YesButton->IsEnabled) return;
            // SelectYesno::ReceiveEvent RVA 0x1129256 forwards Int(0) with updateState=true
            // for its normal Yes button. Send that exact result after checking pending source;
            // do not rely on synthetic button press state or resend a vanished prompt.
            AtkValue response=new() {Type=AtkValueType.Int,Int=0};
            hqConfirmed=true;
            trace.Emit("HqConfirmDispatch",$"step={index+1}, addon={yesno->Id}, target={Describe(current)}, pendingMatches={pending}, response=Int(0), updateState=true, route=NativeYesResult");
            yesno->FireCallback(1,&response,true);
            var after=(ExpertDeliveryNativeRows.Agent*)agent;
            trace.Emit("HqConfirmReturned",$"step={index+1}, pendingAddon={after->PendingAddon}, selectedItem={after->SelectedItem}, target={Describe(current)}");
            return;
        }
        var reward=(AddonGrandCompanySupplyReward*)Get("GrandCompanySupplyReward");
        if (reward==null)
        {
            // Some native variants may finalize directly after their owned HQ prompt.
            if (hqConfirmed && !SourceMatches(current.Key)) WaitResult();
            else trace.Wait($"phase=Dialog, waiting=native confirmation/reward, target={Describe(current)}, hqClickSent={hqConfirmed}, sourceMatches={SourceMatches(current.Key)}, listOpen={plugin.Delivery.ContextOpen}");
            return;
        }
        var module2=RaptureAtkModule.Instance();
        ulong rewardKind=ulong.MaxValue; nint rewardOwner=0; var rewardMapped=false;
        if (module2!=null && module2->AddonCallbackMapping.TryGetValue(reward->Id,out var entry,false))
        { rewardMapped=true; rewardOwner=(nint)entry.EventInterface; rewardKind=entry.EventKind; }
        var ownedReward=rewardMapped && rewardOwner!=0 && rewardKind==2;
        if (reward->AtkValues==null || reward->AtkValuesCount<12 || reward->AtkValues[3].Type!=AtkValueType.String
            || reward->AtkValues[4].Type!=AtkValueType.String || reward->AtkValues[9].Type!=AtkValueType.UInt)
        {
            trace.Emit("RewardDataInvalid",$"addon={reward->Id}, values={reward->AtkValuesCount}, hasValues={reward->AtkValues!=null}",true);
            throw new InvalidOperationException("原生提交奖励数据不完整，未确认。");
        }
        var title=reward->AtkValues[3].String.AsDalamudSeString().TextValue;
        var itemName=reward->AtkValues[4].String.AsDalamudSeString().TextValue;
        var held=Balance(out var max);
        var actual=current with {Protected=!Live(current)};
        var nameMatches=ExpertDeliverySubmitPolicy.NameKey(itemName)==ExpertDeliverySubmitPolicy.NameKey(current.Name);
        var dialogReward=checked((int)reward->AtkValues[9].UInt);
        var canConfirm=ExpertDeliverySubmitPolicy.CanConfirm(current,actual,stopLevel,held,max,ownedReward && title=="筹备稀有品",
            nameMatches,dialogReward) && (!current.Key.HighQuality || hqConfirmed);
        trace.Dialog($"type=Reward, step={index+1}, addon={reward->Id}, title={title}, target={Describe(current)}, actualName={itemName}, nameMatches={nameMatches}, reward={current.SealReward}/{dialogReward}, mapped={rewardMapped}, owner=0x{rewardOwner:X}, kind={rewardKind}, owned={ownedReward}, live={!actual.Protected}, stopLevel={stopLevel}, seals={held}/{max}, hqClickSent={hqConfirmed}, buttonEnabled={reward->DeliverButton!=null && reward->DeliverButton->IsEnabled}, accepted={canConfirm}",!canConfirm);
        if (!canConfirm)
        {
            Plugin.Log.Warning("Expert delivery reward rejected: expected={Name}/{Item}/{Container}/{Slot}, HQ={HQ}, hqConfirmed={Confirmed}, "
                + "actual={Actual}, reward={ExpectedReward}/{ActualReward}, owned={Owned}, live={Live}, seals={Held}/{Max}.",
                current.Name,current.Key.ItemId,current.Key.Container,current.Key.Slot,current.Key.HighQuality,hqConfirmed,
                itemName,current.SealReward,dialogReward,ownedReward,!actual.Protected,held,max);
            throw new InvalidOperationException(!nameMatches
                ? $"奖励页为「{itemName}」，与待提交「{current.Name}」不符，未确认。"
                : "奖励、品级、军票或装备来源不符，未确认；详情见 /xllog。");
        }
        if (reward->DeliverButton==null || !reward->DeliverButton->IsEnabled) return;
        before=held; SetPhase(Phase.Result); // Latch before the irreversible native confirmation.
        if (!ClickDeliveryButton((AtkUnitBase*)reward,reward->DeliverButton))
            throw new InvalidOperationException("未找到原生提交按钮事件。");
    }
    private bool ClickDeliveryButton(AtkUnitBase* addon, AtkComponentButton* button)
    {
        if (addon == null || button == null || !button->IsEnabled || button->OwnerNode == null
            || !button->OwnerNode->AtkResNode.IsVisible()) return false;
        var evt = button->OwnerNode->AtkResNode.AtkEventManager.Event;
        for (var i = 0; evt != null && i < 64; i++, evt = evt->NextEvent)
        {
            if (evt->State.EventType != AtkEventType.ButtonClick || evt->Listener != (AtkEventListener*)addon) continue;
            // Match the addon-button delivery used by ClickAddonButton: keep the registered
            // event and parameter, and invoke the owning addon listener via ReceiveEvent.
            trace.Emit("ButtonDispatch",$"phase={phase}, step={index+1}, target={Describe(current)}, addon={addon->Id}, button=0x{(nint)button:X}, param={evt->Param}, event={evt->State.EventType}, flags={evt->State.StateFlags}, route=ReceiveEvent");
            addon->ReceiveEvent(evt->State.EventType,(int)evt->Param,evt);
            return true; // Dispatch attempted; only a native transition/result acknowledges it.
        }
        return false;
    }
    private void CheckCapacity()
    {
        var held=Balance(out var max);
        if (current==null || held>=max || current.SealReward>max-held || current.ItemLevel>=stopLevel)
            throw new InvalidOperationException("军票容量不足或已达到停止品级。");
    }
    private void WaitResult()
    {
        if (current==null) return;
        if (Has("GrandCompanySupplyReward")) { trace.Wait($"phase=Result, waiting=reward window close, target={Describe(current)}"); return; }
        if (Has("SelectYesno")) throw new InvalidOperationException("提交后出现其他确认窗口，已停止。");
        var inv=InventoryManager.Instance();
        var container=inv==null ? null : inv->GetInventoryContainer((InventoryType)current.Key.Container);
        var readable=container!=null && container->IsLoaded && container->Items!=null && current.Key.Slot<container->Size;
        var held=Balance(out _);
        var sourceMatches=SourceMatches(current.Key);
        if (!ExpertDeliverySubmitPolicy.Acknowledged(readable,sourceMatches,false,before,held,current.SealReward))
        { trace.Wait($"phase=Result, target={Describe(current)}, sourceReadable={readable}, sourceMatches={sourceMatches}, seals={before}->{held}, expectedReward={current.SealReward}"); return; }
        if (!plugin.Delivery.ContextOpen) { trace.Wait("phase=Result, waiting=native list return after acknowledgement"); return; }
        trace.Emit("Acknowledged",$"step={index+1}/{approved.Length}, target={Describe(current)}, sourceReadable={readable}, sourceMatches={sourceMatches}, seals={before}->{held}, gained={held-before}");
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
    private static bool ContextMenuOpen => Has("ContextMenu") || Has("ContextIconMenu") || Has("AddonContextSub");
    private void SetPhase(Phase value)
    {
        trace.Emit("Phase",$"from={phase}, to={value}, step={index+1}/{approved.Length}, target={Describe(current)}");
        phase=value; phaseStarted=Environment.TickCount64; deadline=phaseStarted+15_000; trace.ResetStep();
    }
    private static string Describe(ExpertDeliveryCandidate? item) => item == null ? "none"
        : $"{ExpertDeliveryTrace.Clean(item.Name)}/id={item.Key.ItemId}/HQ={item.Key.HighQuality}/ilvl={item.ItemLevel}/reward={item.SealReward}/container={item.Key.Container}/slot0={item.Key.Slot}";
    private string BalanceSnapshot()
    {
        try { var held=Balance(out var max); return $"{held}/{max}"; }
        catch (Exception ex) { return "unavailable: "+ex.GetType().Name; }
    }
    private string SourceSnapshot()
    {
        try
        {
            if (current==null) return "no target";
            var inv=InventoryManager.Instance();
            var container=inv==null ? null : inv->GetInventoryContainer((InventoryType)current.Key.Container);
            if (container==null) return "container unavailable";
            if (!container->IsLoaded || container->Items==null || current.Key.Slot>=container->Size)
                return $"loaded={container->IsLoaded}, size={container->Size}, hasItems={container->Items!=null}";
            var slot=container->GetInventorySlot(current.Key.Slot);
            return slot==null ? "slot unavailable" : $"actualId={slot->ItemId}, quantity={slot->Quantity}, flags={slot->Flags}, symbolic={slot->IsSymbolic}, expected={Describe(current)}";
        }
        catch (Exception ex) { return "unavailable: "+ex.GetType().Name; }
    }
    private void LogStopContext()
    {
        try
        {
            trace.Emit("StopContext",$"listOpen={plugin.Delivery.ContextOpen}, yesno={Has("SelectYesno")}, reward={Has("GrandCompanySupplyReward")}, sealsBefore={before}, sealsNow={BalanceSnapshot()}, source=[{SourceSnapshot()}]");
        }
        catch (Exception ex) { trace.Emit("DiagnosticUnavailable",ex.GetType().Name); }
    }
    private void LogAgentTarget()
    {
        // Diagnostic-only reads; unavailable metadata must never change submission behavior.
        try
        {
            var agent=AgentGrandCompanySupply.Instance();
            if (agent==null || agent->ItemArray==null || agent->NumItems is <0 or >4096)
            { trace.Emit("AgentTarget","unavailable"); return; }
            var matches=new List<string>();
            for (var i=0;i<agent->NumItems;i++)
            {
                var row=agent->ItemArray[i];
                if (current==null || row.Inventory!=(InventoryType)current.Key.Container || row.Slot!=current.Key.Slot) continue;
                matches.Add($"array={i}, position={row.Position}, rawId={row.ItemId}, reward={row.SealReward}, available={row.TurnInAvailable}");
            }
            trace.Emit("AgentTarget",$"addon={agent->AddonId}, tab={agent->SelectedTab}, count={agent->NumItems}, target={Describe(current)}, matches=[{string.Join("; ",matches)}]");
        }
        catch (Exception ex) { trace.Emit("DiagnosticUnavailable",ex.GetType().Name); }
    }
}
