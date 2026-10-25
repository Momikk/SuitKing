using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;

namespace SuitKing;

internal sealed class OutfitInventoryReader
{
    private static readonly (InventoryType Type, string Name)[] Containers =
    [
        (InventoryType.Inventory1, "物品栏 1"), (InventoryType.Inventory2, "物品栏 2"),
        (InventoryType.Inventory3, "物品栏 3"), (InventoryType.Inventory4, "物品栏 4"),
        (InventoryType.ArmoryMainHand, "兵装库·主手"), (InventoryType.ArmoryOffHand, "兵装库·副手"),
        (InventoryType.ArmoryHead, "兵装库·头部"), (InventoryType.ArmoryBody, "兵装库·身体"),
        (InventoryType.ArmoryHands, "兵装库·手部"), (InventoryType.ArmoryLegs, "兵装库·腿部"),
        (InventoryType.ArmoryFeets, "兵装库·脚部"), (InventoryType.ArmoryEar, "兵装库·耳部"),
        (InventoryType.ArmoryNeck, "兵装库·颈部"), (InventoryType.ArmoryWrist, "兵装库·腕部"),
        (InventoryType.ArmoryRings, "兵装库·戒指"),
    ];
    internal static bool TryResolveContainer(string name, out InventoryType type)
    {
        foreach (var entry in Containers)
            if (entry.Name == name) { type = entry.Type; return true; }
        type = InventoryType.Invalid;
        return false;
    }
    internal static string? ContainerName(InventoryType type)
        => Containers.Where(entry => entry.Type == type).Select(entry => entry.Name).FirstOrDefault();
    private readonly GearsetSnapshotGate gearsetGate = new();
    private OutfitCatalog? catalog;
    private Dictionary<uint, uint>? cabinetIds;
    private IReadOnlyList<InventoryEntry> cabinetCopies = [];
    internal OutfitInfo? FindOutfit(uint itemId) => catalog?.Get(itemId);
    public bool IsCabinetLoaded { get; private set; }
    public int CountCabinetCopies(uint itemId) => cabinetCopies.Count(item => item.ItemId == itemId);
    public IReadOnlyList<OutfitEquipment> CabinetEquipment => CabinetPolicy.Candidates(Equipment);
    public IReadOnlyList<OutfitEquipment> Equipment { get; private set; } = [];
    public string Status { get; private set; } = "打开投影台后读取物品栏和兵装库。";
    public bool IsComplete { get; private set; }
    public bool AreOutfitsLoaded { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public void Clear()
    {
        gearsetGate.Clear();
        cabinetCopies = [];
        IsCabinetLoaded = false;
        Equipment = [];
        IsComplete = false;
        AreOutfitsLoaded = false;
        UpdatedAt = null;
        Status = "打开投影台后读取物品栏和兵装库。";
    }

    public void Fail(string message)
    {
        Clear();
        Status = message;
    }

    // Only called from Framework.Update. No pointer survives this call and no game memory is written.
    public unsafe void Refresh(bool requireDresser = true)
    {
        var manager = InventoryManager.Instance();
        if (manager == null)
        {
            Fail("物品数据尚未就绪，等待加载。" );
            return;
        }
        catalog ??= new(Plugin.Data.GetExcelSheet<MirageStoreSetItemLookup>(), Plugin.Data.GetExcelSheet<Item>());
        cabinetIds ??= Plugin.Data.GetExcelSheet<Lumina.Excel.Sheets.Cabinet>()
            .Where(row => row.Item.RowId != 0).GroupBy(row => row.Item.RowId)
            .ToDictionary(group => group.Key, group => group.First().RowId);
        var ui = UIState.Instance();
        IsCabinetLoaded = ui != null && ui->Cabinet.IsCabinetLoaded();
        var collected = IsCabinetLoaded ? cabinetIds.Values.Where(id => ui->Cabinet.IsItemInCabinet(id)).ToHashSet() : null;
        OutfitInfo? FindCandidate(uint id)
        {
            var outfit = catalog.Get(id);
            if (outfit != null) return outfit;
            if (!cabinetIds.ContainsKey(id) || !Plugin.Data.GetExcelSheet<Item>().TryGetRow(id, out var item)) return null;
            return new(item.Name.ExtractText(), "—", [], item.Icon, OutfitCatalog.GetPart(item));
        }
        var gearsets = RaptureGearsetModule.Instance();
        // Read saved gearset IDs once. Unlike per-item native lookup, this does not depend
        // on transient item-row/icon loading during dresser refreshes. HQ identities differ.
        if (gearsets == null)
        {
            IsComplete = false;
            Status = "等待装备套装登记数据加载，保留上次完整结果。";
            return;
        }
        var registeredIds = new HashSet<uint>();
        foreach (ref var set in gearsets->Entries)
        {
            if ((set.Flags & RaptureGearsetModule.GearsetFlag.Exists) == 0) continue;
            foreach (ref var piece in set.Items)
                if (piece.ItemId != 0) registeredIds.Add(piece.ItemId);
        }
        if (!gearsetGate.IsStable(registeredIds))
        {
            IsComplete = false;
            Status = "等待装备套装过滤稳定，保留上次完整结果。";
            return;
        }
        var entries = new List<InventoryEntry>();
        var missing = new List<string>();
        foreach (var (type, name) in Containers)
        {
            var container = manager->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded)
            {
                missing.Add(name);
                continue;
            }
            if (container->Type != type || container->Size < 0 || container->Size > 140
                || (container->Size != 0 && container->Items == null))
                throw new InvalidOperationException($"{name}数据结构异常，已停止读取。");
            for (var slot = 0; slot < container->Size; slot++)
            {
                var item = container->Items + slot;
                // These real inventory containers should never contain symbolic proxy items.
                if (item->IsSymbolic)
                    throw new InvalidOperationException($"{name}存在未预期的代理物品，已停止读取。");
                if (item->ItemId == 0 || item->Quantity <= 0 || FindCandidate(item->ItemId) is null)
                    continue;
                entries.Add(new(item->ItemId,
                    (item->Flags & InventoryItem.ItemFlags.HighQuality) != 0, name, slot + 1, item->Condition,
                    GearsetRegistration.IsRegistered(registeredIds, item->ItemId,
                        (item->Flags & InventoryItem.ItemFlags.HighQuality) != 0)));
            }
        }
        catalog ??= new(Plugin.Data.GetExcelSheet<MirageStoreSetItemLookup>(), Plugin.Data.GetExcelSheet<Item>());
        var stored = DresserOutfitReader.Read();
        AreOutfitsLoaded = stored is not null;
        IsComplete = missing.Count == 0;
        if (!IsComplete || (requireDresser && !AreOutfitsLoaded) || !requireDresser && !IsCabinetLoaded)
        {
            Status = "等待物品、套装及过滤数据加载，保留上次完整结果。";
            return;
        }
        cabinetCopies = entries.Where(item => cabinetIds.ContainsKey(item.ItemId)).ToArray();
        Equipment = OutfitListBuilder.Build(entries, FindCandidate).Select(item =>
        {
            uint? cabinetId = cabinetIds.TryGetValue(item.ItemId, out var rowId) ? rowId : null;
            bool? inCabinet = collected is null ? null : cabinetId.HasValue && collected.Contains(cabinetId.Value);
            var assessment = OutfitStateEvaluator.Evaluate(item.ItemId, catalog.Get(item.ItemId)?.Sets, stored, item.HighQuality);
            return item with
            {
                CabinetId = cabinetId, InCabinet = inCabinet,
                Assessment = CabinetPolicy.Assess(assessment, cabinetId, inCabinet, item.RegisteredToGearset),
            };
        }).Where(item => catalog.Get(item.ItemId) != null || item.RegisteredToGearset == false
            && item.CabinetId.HasValue && item.InCabinet != true).ToArray();
        IsComplete = missing.Count == 0;
        UpdatedAt = DateTimeOffset.Now;
        Status = IsComplete
            ? $"已扫描全部物品栏和兵装库，找到 {Equipment.Count} 件套装幻影化装备。"
            : $"当前结果不完整，等待加载：{string.Join("、", missing)}";
    }

}
