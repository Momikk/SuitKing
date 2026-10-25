namespace SuitKing;

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
    internal static bool Acknowledged(bool sourceReadable, bool sourceStillPresent, bool dialogOpen,
        long before, long after, int reward)
        => sourceReadable && !sourceStillPresent && !dialogOpen && reward > 0 && after >= before
            && after-before >= reward;
}
