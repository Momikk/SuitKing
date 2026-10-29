using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;

namespace SuitKing;

// Reads the native expert-delivery candidates and real inventory slots. No submission API.
internal sealed unsafe class ExpertDeliveryPreviewReader
{
    private readonly GearsetSnapshotGate gate = new();
    internal long Held { get; private set; }
    internal long Maximum { get; private set; }
    internal IReadOnlyList<ExpertDeliveryCandidate> Candidates { get; private set; } = [];
    internal void Reset() { gate.Clear(); Candidates = []; }
    internal bool TryRead(int stopLevel, out ExpertDeliveryPreviewPlan? plan)
    {
        plan = null;
        var handle = Plugin.GameGui.GetAddonByName(ExpertDeliveryPreviewController.AddonName);
        var agent = AgentGrandCompanySupply.Instance();
        if (handle.IsNull || !handle.IsReady || !handle.IsVisible || agent == null || agent->SelectedTab != 2)
            throw new InvalidOperationException("请保持筹备稀有品页打开。");
        var addon = (AddonGrandCompanySupplyList*)handle.Address;
        if (addon->SelectedTab != 2 || agent->AddonId != addon->Id || agent->NumItems is < 0 or > 4096
            || (agent->NumItems > 0 && agent->ItemArray == null))
            throw new InvalidOperationException("筹备列表尚未就绪。");
        var gearsets = RaptureGearsetModule.Instance();
        var inventory = InventoryManager.Instance();
        if (gearsets == null || inventory == null) throw new InvalidOperationException("物品与职业配装数据未就绪。");
        var registered = new HashSet<uint>();
        foreach (ref var set in gearsets->Entries)
            if ((set.Flags & RaptureGearsetModule.GearsetFlag.Exists) != 0)
                foreach (ref var piece in set.Items) if (piece.ItemId != 0) registered.Add(piece.ItemId);
        if (!gate.IsStable(registered)) return false;
        var text = addon->SealCountTextNode == null ? "" : SeString.Parse(addon->SealCountTextNode->NodeText.AsSpan()).TextValue;
        if (!ExpertDeliveryPreviewPlan.TryParseSeals(text, out var held, out var maximum))
            throw new InvalidOperationException("无法读取军票余额，未生成清单。");
        Held = held; Maximum = maximum;
        var rows = new List<ExpertDeliveryCandidate>();
        var sheet = Plugin.Data.GetExcelSheet<Item>();
        for (var index = 0; index < agent->NumItems; index++)
        {
            var native = agent->ItemArray[index];
            if (native.Position < 11) continue; // First eleven entries are daily supply/provisioning.
            var location = OutfitInventoryReader.ContainerName(native.Inventory);
            if (location == null) continue; // Excludes equipped, retainers and symbolic inventory.
            var container = inventory->GetInventoryContainer(native.Inventory);
            if (container == null || !container->IsLoaded || container->Type != native.Inventory
                || container->Size is < 0 or > 140 || container->Items == null || native.Slot >= container->Size)
                throw new InvalidOperationException("装备来源尚未加载，未生成清单。");
            var item = container->GetInventorySlot(native.Slot);
            if (item == null || item->IsSymbolic || item->Quantity != 1 || item->ItemId == 0
                || item->ItemId != native.ItemId % 1_000_000)
                throw new InvalidOperationException("筹备列表与物品栏正在变化，请重新生成。");
            var hq = (item->Flags & InventoryItem.ItemFlags.HighQuality) != 0;
            if (GearsetRegistration.IsRegistered(registered, item->ItemId, hq)) continue;
            if (!sheet.TryGetRow(item->ItemId, out var data) || data.LevelItem.RowId == 0)
                throw new InvalidOperationException("无法读取装备品级，未生成清单。");
            rows.Add(new(new(item->ItemId, (uint)native.Inventory, native.Slot, hq), data.Name.ExtractText(),
                data.LevelItem.RowId, native.SealReward, location + $"·{native.Slot + 1}", data.Icon,
                Available: native.TurnInAvailable != 0));
        }
        Candidates = rows.ToArray(); // Complete fresh candidates, before cutoff/capacity truncation.
        plan = ExpertDeliveryPreviewPlan.Build(rows, stopLevel, held, maximum);
        return true;
    }
}
