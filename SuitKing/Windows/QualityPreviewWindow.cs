using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;
using Lumina.Data.Parsing.Uld;
using Lumina.Excel.Sheets;

namespace SuitKing.Windows;

internal sealed unsafe class QualityPreviewWindow(Plugin plugin) : NativeAddon
{
    private QualityNormalizationPlan? plan;
    private readonly List<TextNode> labels = [];
    private TextButtonNode? confirm;
    private TextButtonNode? cancel;

    internal void Present(QualityNormalizationPlan value)
    {
        if (IsAllocated) return;
        plan = value;
        // Header, six-to-thirteen content rows, one compact button row and margins.
        Size = new(520, 92 + (value.PrismCount + 4) * 22);
        Open();
    }

    protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> values)
    {
        if (plan == null) return;
        var origin = ContentStartPosition;
        float y = 0;
        void Line(string value, bool warning = false)
        {
            var node = new TextNode { String = value, Position = origin + new Vector2(0, y),
                Size = new(ContentSize.X, 22), FontSize = 13, AlignmentType = AlignmentType.Left,
                TextFlags = TextFlags.Ellipsis, TextTooltip = value };
            if (warning) node.TextColorID = 17;
            labels.Add(node); AddNode(node); y += 22;
        }
        Line(plan.Definition.Name);
        Line("HQ 降为 NQ 后无法恢复。仅处理下列本套装备。", true);
        foreach (var id in plan.Definition.Pieces.Where(id => plan.Stored.Pieces.Contains(id)))
            Line("取出并降为 NQ：" + Plugin.Data.GetExcelSheet<Item>().GetRow(id).Name.ExtractText());
        foreach (var item in plan.Additional) Line("一起存回：" + item.Name + "（NQ）");
        Line($"需要 {plan.Stored.Pieces.Count} 个物品栏空位、{plan.PrismCount} 个幻象棱晶。");
        Line("中途停止时，已取出的装备留在物品栏。", true);
        var buttonY = y + 12;
        var buttonX = (ContentSize.X - 346) / 2;
        confirm = new TextButtonNode { String = "确认转为 NQ 并存回", Position = origin + new Vector2(buttonX, buttonY),
            Size = new(230, 28), OnClick = () => { var expected = plan; Close(); if (expected != null) plugin.Quality.Confirm(expected); } };
        cancel = new TextButtonNode { String = "取消", Position = origin + new Vector2(buttonX + 246, buttonY),
            Size = new(100, 28), OnClick = () => { plugin.Quality.Stop("已取消预览。"); Close(); } };
        AddNode(confirm); AddNode(cancel);
    }

    protected override void OnFinalize(AtkUnitBase* addon)
    {
        foreach (var node in labels) node.Dispose();
        labels.Clear(); confirm?.Dispose(); cancel?.Dispose(); confirm = cancel = null; plan = null;
    }
}
