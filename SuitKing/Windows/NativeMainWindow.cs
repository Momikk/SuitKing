using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;
using Lumina.Data.Parsing.Uld;

namespace SuitKing.Windows;

internal sealed unsafe class NativeMainWindow(Plugin plugin) : NativeAddon
{
    private CheckboxNode? hideCheckbox;
    private TextButtonNode? completeButton;
    private TextButtonNode? qualityButton;
    private TextButtonNode? createButton;
    private string lastCompletionStatus = "";
    private TabBarNode? tabs;
    private ListNode<NativeEquipmentRow, NativeEquipmentRowNode>? list;
    private TextNode? footer;
    private TextNode? cabinetHint;
    private bool lastCabinetMode;
    private ResNode? header;
    private bool pendingOpen;
    private OutfitStorageState? selectedState = OutfitStorageState.CanComplete;
    private IReadOnlyList<OutfitEquipment>? lastEquipment;
    private bool lastHide;
    private bool lastReady;
    private bool dirty = true;
    private static readonly (string Label, OutfitStorageState? State)[] Categories =
    [ ("全部", null), ("已保存", OutfitStorageState.Saved), ("可补齐", OutfitStorageState.CanComplete),
      ("品质冲突", OutfitStorageState.QualityConflict), ("未建立", OutfitStorageState.NotCreated), ("收藏柜", OutfitStorageState.CanStoreCabinet), ("待确认", OutfitStorageState.Unknown) ];

    internal void OpenOnCompletable()
    {
        selectedState = plugin.IsCabinetOpen ? OutfitStorageState.CanStoreCabinet : OutfitStorageState.CanComplete;
        dirty = true;
        pendingOpen = !IsOpen;
        TickPendingOpen();
        if (tabs is not null) SelectDefaultTab();
    }

    internal void TickPendingOpen()
    {
        if (!pendingOpen || IsAllocated) return;
        pendingOpen = false;
        Open();
    }

    public override void Close()
    {
        pendingOpen = false;
        base.Close();
    }

    protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> values)
    {
        var origin = ContentStartPosition;
        var width = ContentSize.X;
        hideCheckbox = new CheckboxNode
        {
            String = "隐去套装中的物品", IsChecked = plugin.Config.HideGearsetItems,
            Position = origin, Size = new Vector2(160, 24),
            OnClick = value =>
            {
                plugin.Config.HideGearsetItems = value;
                plugin.SaveConfiguration();
                dirty = true;
            }
        };
        AddNode(hideCheckbox);
        completeButton = new TextButtonNode
        {
            String = "自动补齐", Position = origin + new Vector2(170, -2), Size = new Vector2(112, 28),
            TextTooltip = "静默补入未被过滤的可补齐装备，同套缺件合并处理，消耗幻象棱晶。再次点击可停止。",
            OnClick = () => { if (plugin.IsCabinetOpen) plugin.CabinetStore.Toggle(); else plugin.Completion.Toggle(); },
        };
        AddNode(completeButton);
        qualityButton = new TextButtonNode { String = "冲突转 NQ", Position = origin + new Vector2(294, -2), Size = new(112, 28),
            TextTooltip = "逐套预览：取出冲突的 HQ 部件，永久降为 NQ，再与待补齐部件一起存回。",
            OnClick = () => plugin.Quality.OpenPreview() };
        AddNode(qualityButton);
        createButton = new TextButtonNode { String = "建立套装", Position = origin + new Vector2(418, -2), Size = new(112, 28),
            OnClick = () => plugin.Creation.Toggle() };
        AddNode(createButton);
        cabinetHint = new TextNode { Position = origin + new Vector2(294, 0), Size = new Vector2(width - 294, 24), FontSize = 12, AlignmentType = AlignmentType.Left };
        AddNode(cabinetHint);
        tabs = new TabBarNode { Position = origin + new Vector2(0, 29), Size = new Vector2(width, 28) };
        AddNode(tabs);
        foreach (var (label, state) in Categories)
            tabs.AddTab(label, () => { selectedState = state; dirty = true; list?.ResetScroll(); });
        SelectDefaultTab();

        header = new ResNode { Position = origin + new Vector2(0, 62), Size = new Vector2(width - 24, 24) };
        AddNode(header);
        var labels = new[] { "图标", "装备名称", "部位", "所属套装", "套装状态" };
        for (var i = 0; i < labels.Length; i++)
        {
            var (x, cellWidth) = NativeEquipmentRowNode.Column(i, header.Width);
            var text = new TextNode
            {
                String = labels[i], Position = new Vector2(x, 0), Size = new Vector2(cellWidth, 24),
                FontSize = 13, AlignmentType = AlignmentType.Left,
            };
            text.AttachNode(header);
        }
        list = new ListNode<NativeEquipmentRow, NativeEquipmentRowNode>
        {
            OptionsList = [], AutoResetScroll = false, ItemSpacing = 0,
            Position = origin + new Vector2(0, 88), Size = new Vector2(width, ContentSize.Y - 120),
        };
        list.NoResultsTextNode.TextId = 0;
        list.NoResultsTextNode.String = "等待套装与过滤数据加载。";
        AddNode(list);
        footer = new TextNode
        {
            Position = origin + new Vector2(0, ContentSize.Y - 24), Size = new Vector2(width, 24),
            FontSize = 12, AlignmentType = AlignmentType.Left,
        };
        AddNode(footer);
        dirty = true;
        RefreshContents();
    }

    private void SelectDefaultTab()
    {
        if (tabs is null) return;
        for (var i = 0; i < tabs.TabButtons.Count; i++)
        {
            tabs.TabButtons[i].IsChecked = Categories[i].State == selectedState;
            tabs.TabButtons[i].IsSelected = Categories[i].State == selectedState;
        }
    }

    protected override void OnUpdate(AtkUnitBase* addon)
    {
        if (!IsOpen || list is null) return;
        var cabinet = plugin.IsCabinetOpen;
        var running = plugin.Completion.IsRunning || plugin.CabinetStore.IsRunning || plugin.Quality.IsRunning || plugin.Creation.IsRunning;
        if (completeButton is not null)
        {
            completeButton.String = cabinet ? (plugin.CabinetStore.IsRunning ? "停止收纳" : "一键存入收藏柜")
                : (plugin.Completion.IsRunning ? "停止补齐" : "自动补齐");
            completeButton.TextTooltip = cabinet ? "存入尚未收录的装备；忽略穿戴、职业配装及重复副本。再次点击可停止。"
                : "静默补入未被过滤的可补齐装备，同套缺件合并处理，消耗幻象棱晶。再次点击可停止。";
            completeButton.IsEnabled = !plugin.Creation.IsRunning && !plugin.Quality.IsRunning && (running || (plugin.Inventory.IsComplete && (cabinet ? plugin.IsCabinetStoreOpen && plugin.Inventory.IsCabinetLoaded : plugin.IsDresserOpen && plugin.Inventory.AreOutfitsLoaded)));
            hideCheckbox!.IsVisible = !cabinet;
            hideCheckbox.IsEnabled = !running;
            tabs!.IsVisible = !cabinet;
            qualityButton!.IsVisible = !cabinet;
            qualityButton.String = plugin.Quality.IsRunning ? "停止转 NQ" : "冲突转 NQ";
            qualityButton.IsEnabled = plugin.Quality.IsRunning || (!running && plugin.IsDresserOpen && Ready);
            var count = OutfitDisplayFilter.Apply(plugin.Inventory.Equipment,plugin.Config.HideGearsetItems,"")
                .Count(item => item.Assessment?.State == OutfitStorageState.NotCreated && item.Condition >= 30000);
            createButton!.IsVisible = !cabinet;
            createButton.String = plugin.Creation.IsRunning ? "停止建立" : "建立套装";
            createButton.IsEnabled = plugin.Creation.IsRunning || (!running && plugin.IsDresserOpen && Ready && count > 0);
            createButton.TextTooltip = $"将未被过滤的未建立装备创建为套装，预计最多消耗 {count} 个幻象棱晶。同套部件保持相同品质，重复副本只用一件。再次点击停止。";
        }
        if (dirty || lastCabinetMode != cabinet || lastCompletionStatus != CurrentStatus || !ReferenceEquals(lastEquipment, plugin.Inventory.Equipment)
            || lastHide != plugin.Config.HideGearsetItems || lastReady != Ready)
            RefreshContents();
    }

    private string CurrentStatus => plugin.IsCabinetOpen ? plugin.CabinetStore.Status
        : plugin.Creation.IsRunning || !string.IsNullOrEmpty(plugin.Creation.Status) ? plugin.Creation.Status
        : plugin.Quality.IsRunning || !string.IsNullOrEmpty(plugin.Quality.Status) ? plugin.Quality.Status : plugin.Completion.Status;
    private bool Ready => plugin.Inventory.IsComplete && (plugin.IsCabinetOpen ? plugin.Inventory.IsCabinetLoaded : plugin.Inventory.AreOutfitsLoaded);

    private void RefreshContents()
    {
        if (list is null || tabs is null || footer is null) return;
        var inventory = plugin.Inventory;
        var cabinet = plugin.IsCabinetOpen;
        if (header is not null) header.Position = ContentStartPosition + new Vector2(0, cabinet ? 34 : 62);
        list.Position = ContentStartPosition + new Vector2(0, cabinet ? 60 : 88);
        list.Height = ContentSize.Y - (cabinet ? 92 : 120);
        var hide = !cabinet && plugin.Config.HideGearsetItems;
        var equipment = cabinet ? inventory.CabinetEquipment : OutfitDisplayFilter.Apply(inventory.Equipment, hide, "");
        if (cabinetHint is not null)
        {
            var count = inventory.CabinetEquipment.Count;
            cabinetHint.Position = ContentStartPosition + new Vector2(cabinet ? 0 : 542, 0);
            cabinetHint.Width = cabinet ? 166 : ContentSize.X - 542;
            cabinetHint.String = cabinet ? "忽略穿戴及职业配装" : count > 0 ? $"请先去处理收藏柜（{count} 件）"
                : !inventory.IsCabinetLoaded ? "请先打开收藏柜确认状态" : "";
        }
        for (var i = 0; i < Categories.Length; i++)
        {
            var (label, state) = Categories[i];
            var count = equipment.Count(item => Matches(item, state));
            tabs.TabButtons[i].String = $"{label} {count}";
        }
        var matches = equipment.Where(item => cabinet || Matches(item, selectedState))
            .OrderBy(item => EquipmentParts.Rank(item.Part)).ThenBy(item => item.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Location, StringComparer.Ordinal).ThenBy(item => item.Slot).ToArray();
        var rows = new List<NativeEquipmentRow>();
        string? group = null;
        foreach (var item in matches)
        {
            var nextGroup = EquipmentParts.Group(item.Part);
            if (group != nextGroup)
            {
                group = nextGroup;
                rows.Add(new(group, null));
            }
            rows.Add(new(null, item));
        }
        // Keep the scroll position and reuse native row nodes on the one-second data refresh.

        var contextOpen = plugin.IsDresserOpen || cabinet;
        list.NoResultsTextNode.String = !contextOpen ? "请打开投影台或收藏柜。"
            : Ready ? "这个类别暂时没有装备。" : "等待收纳与过滤数据加载。";
        if (!list.OptionsList.SequenceEqual(rows)) list.OptionsList = rows;
        var hidden = hide ? inventory.Equipment.Count(item => item.RegisteredToGearset == true) : 0;
        footer.String = Ready ? $"显示 {matches.Length} 件" + (hide ? $" · 已隐藏 {hidden} 件" : "")
            : contextOpen ? inventory.Status : "请打开投影台或收藏柜。";
        if (!string.IsNullOrEmpty(CurrentStatus)) footer.String = CurrentStatus;
        if (cabinet && !plugin.IsCabinetStoreOpen) footer.String = "请切换到收藏柜的存入界面以收纳装备。";
        lastCompletionStatus = CurrentStatus;
        lastEquipment = inventory.Equipment;
        lastHide = plugin.Config.HideGearsetItems;
        lastReady = Ready;
        lastCabinetMode = cabinet;
        dirty = false;
    }

    private static bool Matches(OutfitEquipment item, OutfitStorageState? state)
        => state is null || (item.Assessment?.State ?? OutfitStorageState.Unknown) == state;

    protected override void OnFinalize(AtkUnitBase* addon)
    {
        // Detach owned children before the game's finalizer; never keep native pointers across reopening.
        if (plugin.Completion.IsRunning) plugin.Completion.Stop("套装之王窗口已关闭。");
        if (plugin.CabinetStore.IsRunning) plugin.CabinetStore.Stop("套装之王窗口已关闭。");
        if (plugin.Quality.IsRunning) plugin.Quality.Stop("套装之王窗口已关闭。");
        if (plugin.Creation.IsRunning) plugin.Creation.Stop("套装之王窗口已关闭。");
        createButton?.Dispose(); createButton = null;
        plugin.QualityPreview.Close();
        qualityButton?.Dispose(); qualityButton = null;
        cabinetHint?.Dispose();
        cabinetHint = null;
        completeButton?.Dispose();
        completeButton = null;
        hideCheckbox?.Dispose();
        tabs?.Dispose();
        list?.Dispose();
        header?.Dispose();
        footer?.Dispose();
        header = null;
        hideCheckbox = null;
        tabs = null;
        list = null;
        footer = null;
        lastEquipment = null;
    }
}