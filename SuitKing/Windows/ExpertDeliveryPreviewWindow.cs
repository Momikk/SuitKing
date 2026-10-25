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
    private TextButtonNode? submit;
    private ExpertDeliveryPreviewPlan? approved;
    private TextNode? summary;
    private TextNode? stop;
    private ListNode<ExpertDeliveryCandidate, ExpertDeliveryPreviewRow>? list;
    private readonly List<TextNode> labels = [];
    private int lastLevel = 660;
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
        Label("先生成预览，再点击提交 · 固定排除穿戴及职业配装物品",0);
        Label("停止品级（仅提交低于此值的装备）",30);
        level = new NumericInputNode { Position = origin + new Vector2(270,26), Size = new(96,28), Min = 1, Max = 9999, Value = lastLevel };
        level.OnValueUpdate = value => { lastLevel = value; plugin.Delivery.CancelPending(); SetStatus("品级已改变，请重新生成预览。"); };
        AddNode(level);
        preview = new TextButtonNode { String = "生成预览", Position = origin + new Vector2(378,26), Size = new(112,28),
            OnClick = () => plugin.Delivery.Request(level.Value) };
        AddNode(preview);
        submit = new TextButtonNode { String = "提交", Position = origin + new Vector2(502,26), Size = new(100,28),
            IsEnabled = false, TextTooltip = "将预览中的装备逐件换取军票；装备会被消耗，再次点击停止。",
            OnClick = () => { if (plugin.Delivery.Submission.IsRunning) plugin.Delivery.Submission.Stop("已停止；已确认的提交仍可能完成。");
                else if (approved != null) plugin.Delivery.Submission.Start(lastLevel, approved); } };
        AddNode(submit);
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
        approved = null;
        summary.String = value;
        if (submit != null) submit.IsEnabled = false;
        if (list != null) { list.OptionsList = []; list.NoResultsTextNode.String = value; }
        if (stop != null) stop.String = "";
    }
    internal void ShowPlan(ExpertDeliveryPreviewPlan value)
    {
        if (!IsAllocated || list == null || summary == null || stop == null) return;
        approved = value;
        list.OptionsList = value.Items.ToList();
        list.NoResultsTextNode.String = "没有拟提交的装备。";
        summary.String = $"拟提交 {value.Items.Count} 件 · 预计获得 {value.Items.Sum(item => (long)item.SealReward):N0} 军票 · 预计余额 {value.SealsAfter:N0}";
        stop.String = value.StopReason; stop.TextTooltip = value.StopReason;
    }
    protected override void OnUpdate(AtkUnitBase* addon)
    {
        var running = plugin.Delivery.Submission.IsRunning;
        if (level != null) level.IsEnabled = !running;
        if (preview != null) preview.IsEnabled = !running;
        if (submit != null) { submit.String = running ? "停止提交" : "提交"; submit.IsEnabled = running || approved?.Items.Count > 0; }
        if (running && summary != null) summary.String = plugin.Delivery.Submission.Status;
    }
    internal void FinishSubmission(string status)
    {
        SetStatus(status);
    }
    protected override void OnFinalize(AtkUnitBase* addon)
    {
        plugin.Delivery.Submission.Stop("预览窗口已关闭。");
        plugin.Delivery.CancelPending();
        approved = null;
        foreach (var node in labels) node.Dispose(); labels.Clear();
        level?.Dispose(); preview?.Dispose(); submit?.Dispose(); submit = null; summary?.Dispose(); stop?.Dispose(); list?.Dispose();
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
