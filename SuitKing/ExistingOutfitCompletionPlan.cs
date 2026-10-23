namespace SuitKing;

internal sealed record ExistingOutfitCompletionPlan(OutfitDefinition Definition, StoredOutfit Stored,
    IReadOnlyList<CompletionItemKey> Sources)
{
    internal uint RawId => Stored.Id + (Stored.HighQuality ? 1_000_000u : 0);

    // Input order preserves the user's filtered list and catalog membership order.
    // Never include saved pieces, opposite-quality copies, or unrelated sources.
    internal static ExistingOutfitCompletionPlan? First(IReadOnlyList<CompletionItemKey> candidates,
        IReadOnlyList<StoredOutfit> stored, Func<uint, OutfitInfo?> find)
    {
        foreach (var source in candidates)
        {
            var definitions = find(source.ItemId)?.Sets;
            if (definitions == null) continue;
            foreach (var definition in definitions)
            {
                if (definition.Pieces.Count != 11 || !definition.Pieces.Contains(source.ItemId)) continue;
                foreach (var target in stored.Where(set => set.Id == definition.Id && set.HighQuality == source.HighQuality
                    && set.BoxIndex is >= 0 and < 800 && !set.Pieces.Contains(source.ItemId)))
                {
                    var usedItems = new HashSet<uint>();
                    var usedSlots = new HashSet<(uint, ushort)>();
                    var sources = candidates.Where(key => key.HighQuality == target.HighQuality && key.ItemId != 0
                        && key.Container != 9999 && definition.Pieces.Contains(key.ItemId) && !target.Pieces.Contains(key.ItemId)
                        && usedItems.Add(key.ItemId) && usedSlots.Add((key.Container, key.Slot)))
                        .Take(OutfitSubmissionLayout.Capacity).ToArray();
                    if (sources.Contains(source))
                        return new(definition, target, OutfitSubmissionLayout.Pack(definition.Pieces, sources, target.HighQuality));
                }
            }
        }
        return null;
    }

    internal static bool IsAcknowledged(IReadOnlyList<CompletionItemKey> sources,
        Func<CompletionItemKey, bool> unlocked, Func<CompletionItemKey, bool> stillInSource, bool sourcesReadable)
        => sourcesReadable && sources.Count > 0 && sources.All(key => unlocked(key) && !stillInSource(key));
}
