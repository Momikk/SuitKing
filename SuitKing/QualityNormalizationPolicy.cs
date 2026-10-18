namespace SuitKing;

internal enum QualityNormalizationPhase
{
    Idle, Restore, RestoreSelect, RestoreSubmit, RestoreConfirm, RestoreWait,
    CloseSelection, WaitSelectionClosed, CloseDresser, WaitDresserClosed,
    Lower, LowerConfirm, LowerWait, ReopenDresser, WaitDresserOpen, Store, StoreWait
}

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

    internal static bool AcceptsContextTransition(QualityNormalizationPhase phase, bool dresserOpen, bool cabinetOpen)
        => !cabinetOpen && (dresserOpen ? phase == QualityNormalizationPhase.WaitDresserOpen
            : phase is QualityNormalizationPhase.WaitSelectionClosed or QualityNormalizationPhase.WaitDresserClosed);

    internal static bool AllowsClosedDresser(QualityNormalizationPhase phase)
        => phase is QualityNormalizationPhase.WaitSelectionClosed or QualityNormalizationPhase.CloseDresser
            or QualityNormalizationPhase.WaitDresserClosed or QualityNormalizationPhase.Lower
            or QualityNormalizationPhase.LowerConfirm or QualityNormalizationPhase.LowerWait
            or QualityNormalizationPhase.ReopenDresser or QualityNormalizationPhase.WaitDresserOpen;

    internal static bool CanLowerOutside(bool dresserOpen, bool cabinetOpen, bool busy)
        => !dresserOpen && !cabinetOpen && !busy;

    internal static bool SameDresser(ulong expectedId, uint expectedDataId, uint expectedTerritory,
        ulong actualId, uint actualDataId, uint actualTerritory, float distance)
        => expectedId != 0 && expectedTerritory != 0 && expectedId == actualId
            && expectedDataId == actualDataId && expectedTerritory == actualTerritory
            && float.IsFinite(distance) && distance >= 0 && distance <= 6;

    // Native CN prompts include a SeString NewLine payload before the last two characters.
    private static string Compact(string value) => string.Concat(value.Where(character => !char.IsWhiteSpace(character)));
}
