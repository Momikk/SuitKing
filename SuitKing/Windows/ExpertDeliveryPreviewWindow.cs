using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Interfaces;
using KamiToolKit.Nodes;
using Lumina.Data.Parsing.Uld;

namespace SuitKing.Windows;

internal sealed unsafe class ExpertDeliveryPreviewWindow(Plugin plugin) : NativeAddon
{
    private NumericInputNode? level;
    private TextButtonNode? preview;
    private TextNode? summary;
    private TextNode? stop;
    private ListNode<ExpertDeliveryCandidate, ExpertDeliveryPreviewRow>? list;
    private readonly List<TextNode> labels = [];
    private int lastLevel = 600;
    internal void Present() { if (!IsAllocated) Open(); }
    protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> values)
    {
        var origin = ContentStartPosition;
        void Label(string value, float y)
        {
            var node = new TextNode { String = value, Position = origin + new Vector2(0,y), Size = new(ContentSize.X,22),
                FontSize = 13, AlignmentType = AlignmentType.Left };
            labels.Add(node); AddNode(node);
        }
        Label("只读 demo · 不会提交装备 · 固定排除穿戴及职业配装物品",0);
        Label("停止品级（仅预览低于此值的装备）",30);
        level = new NumericInputNode { Position = origin + new Vector2(292,26), Size = new(104,28), Min = 1, Max = 9999, Value = lastLevel };
        level.OnValueUpdate = value => { lastLevel = value; plugin.Delivery.CancelPending(); SetStatus("品级已改变，请重新生成预览。"); };
        AddNode(level);
        preview = new TextButtonNode { String = "生成预览", Position = origin + new Vector2(410,26), Size = new(130,28),
            OnClick = () => plugin.Delivery.Request(level.Value) };
        AddNode(preview);
        summary = new TextNode { Position = origin + new Vector2(0,62), Size = new(ContentSize.X,24), FontSize = 13, AlignmentType = AlignmentType.Left };
        AddNode(summary);
        var rowWidth = ContentSize.X - 16;
        foreach (var column in new[] { ("装备名称",0f,rowWidth-300), ("品级",rowWidth-290,60f),
            ("军票",rowWidth-220,80f), ("所在位置",rowWidth-130,130f) })
        {
            var node = new TextNode { String = column.Item1, Position = origin + new Vector2(column.Item2,90),
                Size = new(column.Item3,22), FontSize = 13, AlignmentType = AlignmentType.Left };
            labels.Add(node); AddNode(node);
        }
        list = new ListNode<ExpertDeliveryCandidate, ExpertDeliveryPreviewRow> { Position = origin + new Vector2(0,116),
            Size = new(ContentSize.X,ContentSize.Y-186), ItemSpacing = 0, OptionsList = [] };
        list.NoResultsTextNode.TextId = 0; list.NoResultsTextNode.String = "输入停止品级，点击生成预览。";
        AddNode(list);
        stop = new TextNode { Position = origin + new Vector2(0,ContentSize.Y-60), Size = new(ContentSize.X,24),
            FontSize = 12, AlignmentType = AlignmentType.Left, TextFlags = TextFlags.Ellipsis };
        AddNode(stop);
        Label("按品级升序排列；这是当前快照，装备变动后请重新生成。",ContentSize.Y-30);
        SetStatus("输入停止品级，点击生成预览。");
    }
    internal void SetStatus(string value)
    {
        if (!IsAllocated || summary == null) return;
        summary.String = value;
        if (list != null) { list.OptionsList = []; list.NoResultsTextNode.String = value; }
        if (stop != null) stop.String = "";
    }
    internal void ShowPlan(ExpertDeliveryPreviewPlan value)
    {
        if (!IsAllocated || list == null || summary == null || stop == null) return;
        list.OptionsList = value.Items.ToList();
        list.NoResultsTextNode.String = "没有拟提交的装备。";
        summary.String = $"拟提交 {value.Items.Count} 件 · 预计获得 {value.Items.Sum(item => (long)item.SealReward):N0} 军票 · 预计余额 {value.SealsAfter:N0}";
        stop.String = value.StopReason; stop.TextTooltip = value.StopReason;
    }
    protected override void OnFinalize(AtkUnitBase* addon)
    {
        plugin.Delivery.CancelPending();
        foreach (var node in labels) node.Dispose(); labels.Clear();
        level?.Dispose(); preview?.Dispose(); summary?.Dispose(); stop?.Dispose(); list?.Dispose();
        level = null; preview = null; summary = stop = null; list = null;
    }
}

internal sealed class ExpertDeliveryPreviewRow : ListItemNode<ExpertDeliveryCandidate>, IListItemNode
{
    public static float ItemHeight => 28;
    private readonly TextNode name;
    private readonly TextNode level;
    private readonly TextNode reward;
    private readonly TextNode location;
    public ExpertDeliveryPreviewRow()
    {
        EnableSelection = false;
        TextNode Cell() => new() { FontSize = 13, AlignmentType = AlignmentType.Left, TextFlags = TextFlags.Ellipsis };
        name = Cell(); level = Cell(); reward = Cell(); location = Cell();
        name.AttachNode(this); level.AttachNode(this); reward.AttachNode(this); location.AttachNode(this);
    }
    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();
        if (name == null) return;
        name.Position = new(0,0); name.Size = new(Width-300,28);
        level.Position = new(Width-290,0); level.Size = new(60,28);
        reward.Position = new(Width-220,0); reward.Size = new(80,28);
        location.Position = new(Width-130,0); location.Size = new(130,28);
    }
    protected override void SetNodeData(ExpertDeliveryCandidate data)
    {
        name.String = data.Name + (data.Key.HighQuality ? "（HQ）" : ""); level.String = data.ItemLevel.ToString();
        reward.String = data.SealReward.ToString(); location.String = data.Location;
        TextTooltip = $"{data.Name} · 品级 {data.ItemLevel} · {data.SealReward} 军票 · {data.Location}";
    }
}
