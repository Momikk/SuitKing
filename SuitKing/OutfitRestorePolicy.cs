namespace SuitKing;

internal static class OutfitRestorePolicy
{
    internal static bool MatchesSource(uint expectedId, int expectedIndex, uint actualId, uint actualIndex, bool fullOutfit)
        => expectedId is >= 1_000_000 and < 2_000_000 && expectedIndex is >= 0 and < 800
            && actualId == expectedId && actualIndex == expectedIndex && fullOutfit;

    internal static bool CanConfirm(bool owned, ushort expectedMask, uint selectedMask,
        uint expectedId, uint dialogId, uint excludedMask, bool sourceValid, string prompt)
        => owned && sourceValid && expectedMask is > 0 and < 2048 && selectedMask == expectedMask
            && expectedId is >= 1_000_000 and < 2_000_000 && dialogId == expectedId
            && excludedMask == ~(uint)expectedMask
            && prompt == "确定要将所选幻影变回道具吗？";
}
