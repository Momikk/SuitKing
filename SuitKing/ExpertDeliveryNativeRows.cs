using System.Runtime.InteropServices;

namespace SuitKing;

// CN client SHA256 958B78E2...: AgentGrandCompanySupply::ReceiveEvent, RVA 0x1001570.
// The callback walks this sorted index array and applies these filters BEFORE counting rows.
// Addon +0x290 is allocation capacity; its paged +0x288 cache is not a selection map.
internal static unsafe class ExpertDeliveryNativeRows
{
    [StructLayout(LayoutKind.Explicit, Size = 0x98)]
    internal struct Agent
    {
        [FieldOffset(0x68)] internal Item* Items;
        [FieldOffset(0x70)] internal int* SortedIndices;
        [FieldOffset(0x78)] internal int Count;
        [FieldOffset(0x90)] internal ushort Tab;
        [FieldOffset(0x93)] internal byte Filter;
    }
    [StructLayout(LayoutKind.Explicit, Size = 0xA0)]
    internal struct Item
    {
        [FieldOffset(0x68)] internal uint Container;
        [FieldOffset(0x80)] internal uint Possessed;
        [FieldOffset(0x84)] internal uint ItemId;
        [FieldOffset(0x97)] internal byte Slot;
        [FieldOffset(0x99)] internal byte Tab;
        [FieldOffset(0x9B)] internal byte Excluded;
        [FieldOffset(0x9D)] internal byte InGearset;
        internal readonly ExpertDeliveryRowIdentity Identity => new(ItemId,Container,Slot);
    }
    internal static ExpertDeliveryRowIdentity[]? Read(void* address)
    {
        if (address==null) return null;
        var agent=(Agent*)address;
        if (agent->Tab!=2 || agent->Filter>2 || agent->Items==null || agent->SortedIndices==null
            || agent->Count is <1 or >4096) return null;
        var rows=new List<ExpertDeliveryRowIdentity>();
        var seen=new HashSet<int>();
        for (var i=0;i<agent->Count;i++)
        {
            var index=agent->SortedIndices[i];
            if (index<0 || index>=agent->Count || !seen.Add(index)) return null;
            ref var item=ref agent->Items[index];
            if (item.Tab!=2 || item.Excluded!=0 || item.Possessed==0) continue;
            if (agent->Filter==1 && item.InGearset!=0) continue;
            if (agent->Filter==2 && IsArmoury(item.Container)) continue;
            rows.Add(item.Identity);
        }
        return rows.ToArray();
    }
    // Exact container predicate called by the native filter (RVA 0x86C080).
    private static bool IsArmoury(uint container)
        => container is >=3200 and <=3209 or 3300 or 3400 or 3500;
    internal static bool StillMatches(void* address,IReadOnlyList<ExpertDeliveryRowIdentity> snapshot)
    {
        var fresh=Read(address);
        return fresh!=null && fresh.SequenceEqual(snapshot);
    }
}
