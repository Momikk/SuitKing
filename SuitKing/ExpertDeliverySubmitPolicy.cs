namespace SuitKing;

internal readonly record struct ExpertDeliveryRowIdentity(uint ItemId, uint Container, int Slot);
internal readonly record struct ExpertDeliverySourceSelection(int EventKind, int RowIndex, bool UpdateState = true);

internal static class ExpertDeliverySubmitPolicy
{
    internal static bool SameTarget(ExpertDeliveryCandidate expected, ExpertDeliveryCandidate actual)
        => expected.Key == actual.Key && expected.ItemLevel == actual.ItemLevel && expected.SealReward == actual.SealReward
            && !actual.Protected && actual.Available;
    internal static bool CanConfirm(ExpertDeliveryCandidate expected, ExpertDeliveryCandidate actual,
        int stopLevel, long held, long maximum, bool owned, bool dialogItemMatches, int dialogReward)
        => SameTarget(expected, actual) && expected.ItemLevel > 0 && expected.ItemLevel < stopLevel
            && held >= 0 && held < maximum && expected.SealReward > 0 && expected.SealReward <= maximum-held
            && owned && dialogItemMatches
            && dialogReward == expected.SealReward;
    internal static bool IsHqPrompt(bool sourceIsHq, bool owned, ulong eventKind, string prompt)
        => sourceIsHq && owned && eventKind == 4
            && string.Concat(prompt.Where(c => !char.IsWhiteSpace(c))) == "确定要交易优质道具吗？";
    internal static string NameKey(string text)
        => string.Concat(text.Where(c => !char.IsWhiteSpace(c) && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.PrivateUse));
    internal static bool RowMatches(ExpertDeliveryRowIdentity row,CompletionItemKey source)
        => source.ItemId!=0 && row.ItemId%1_000_000==source.ItemId && row.Container==source.Container
            && row.Slot>=0 && row.Slot==source.Slot;
    internal static ExpertDeliverySourceSelection SelectSource(ExpertDeliveryCandidate expected, ExpertDeliveryCandidate fresh,
        IReadOnlyList<ExpertDeliveryRowIdentity> nativeRows)
    {
        if (!SameTarget(expected,fresh) || expected.Key.ItemId==0 || nativeRows.Count is <1 or >4096)
            throw new InvalidOperationException("筹备来源或原生记录不匹配，未选择装备。");
        var selected=-1;
        for (var i=0;i<nativeRows.Count;i++)
        {
            if (!RowMatches(nativeRows[i],expected.Key)) continue;
            if (selected>=0) throw new InvalidOperationException("原生记录存在重复来源，未选择装备。");
            selected=i;
        }
        if (selected<0) throw new InvalidOperationException("原生记录中未找到目标来源，未选择装备。");
        // Command 1 is the left-click delivery path. Command 3 opens an item context menu.
        return new(1,selected,true);
    }
    internal static bool Acknowledged(bool sourceReadable, bool sourceStillPresent, bool dialogOpen,
        long before, long after, int reward)
        => sourceReadable && !sourceStillPresent && !dialogOpen && reward > 0 && after >= before
            && after-before >= reward;
}
