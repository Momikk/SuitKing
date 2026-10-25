using System.Globalization;

namespace SuitKing;

internal sealed record ExpertDeliveryCandidate(CompletionItemKey Key, string Name, uint ItemLevel,
    int SealReward, string Location, uint IconId = 0, bool Protected = false, bool Available = true);
internal sealed record ExpertDeliveryPreviewPlan(IReadOnlyList<ExpertDeliveryCandidate> Items,
    long SealsAfter, string StopReason)
{
    internal static ExpertDeliveryPreviewPlan Build(IEnumerable<ExpertDeliveryCandidate> candidates,
        int stopLevel, long held, long maximum)
    {
        if (stopLevel is < 1 or > 9999) throw new InvalidOperationException("请输入 1 至 9999 的停止品级。");
        if (held < 0 || maximum <= 0 || held > maximum) throw new InvalidOperationException("军票数据未就绪，未生成清单。");
        var ordered = candidates.Where(item => !item.Protected)
            .OrderBy(item => item.ItemLevel).ThenBy(item => item.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Key.Container).ThenBy(item => item.Key.Slot).ToArray();
        var result = new List<ExpertDeliveryCandidate>();
        var sources = new HashSet<(uint, ushort)>();
        foreach (var item in ordered)
        {
            if (held >= maximum) return new(result, held, "军票已满，停止。");
            if (item.ItemLevel == 0 || item.Key.ItemId == 0 || item.SealReward <= 0
                || !sources.Add((item.Key.Container, item.Key.Slot)))
                return new(result, held, "装备或奖励数据异常，停止。");
            if (item.ItemLevel >= stopLevel)
                return new(result, held, $"下一件为「{item.Name}」（品级 {item.ItemLevel}），达到停止品级 {stopLevel}。");
            if (!item.Available) return new(result, held, $"「{item.Name}」当前不可提交，停止。");
            if (item.SealReward > maximum - held)
                return new(result, held, $"下一件「{item.Name}」会使军票超过上限，停止。");
            result.Add(item);
            held += item.SealReward;
        }
        return new(result, held, held >= maximum ? "军票已满，停止。" : "已到清单末尾。");
    }

    internal static bool TryParseSeals(string text, out long held, out long maximum)
    {
        held = maximum = 0;
        var parts = text.Replace(",", "").Replace("，", "").Split('/');
        return parts.Length == 2 && long.TryParse(parts[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out held)
            && long.TryParse(parts[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out maximum)
            && held >= 0 && maximum > 0 && held <= maximum;
    }
}
