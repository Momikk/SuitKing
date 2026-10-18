namespace SuitKing;

internal static class QualityNormalizationPolicy
{
    internal const uint GlamourPrismId = 21800;
    private const ulong LowerQualityEventKind = 77;
    internal static bool CanConfirmLower(bool ownerMatches, uint addonId, uint blockingAddonId,
        CompletionItemKey expected, uint blockedContainer, int blockedSlot, bool sourceValid, bool nqProtected,
        string prompt, string itemName, ulong eventKind)
    {
        prompt = Compact(prompt);
        itemName = Compact(itemName);
        return ownerMatches && eventKind == LowerQualityEventKind && addonId != 0 && addonId == blockingAddonId && expected.HighQuality
            && expected.Container == blockedContainer && expected.Slot == blockedSlot
            && sourceValid && !nqProtected && !string.IsNullOrWhiteSpace(itemName)
            && prompt.StartsWith("确定要降低", StringComparison.Ordinal)
            && prompt.Contains(itemName, StringComparison.Ordinal)
            && prompt.EndsWith("的品质变换成以下道具吗？", StringComparison.Ordinal);
    }

    internal static bool CanClickLowerYes(bool identityMatches, bool checkboxPresent, bool checkboxChecked)
        => identityMatches && checkboxPresent && checkboxChecked;

    // Native CN prompts include a SeString NewLine payload before the last two characters.
    private static string Compact(string value) => string.Concat(value.Where(character => !char.IsWhiteSpace(character)));
}
