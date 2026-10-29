using System.Runtime.InteropServices;

namespace SuitKing;

// CN API 15 layout verified against the installed client's selection handler.
// Contains physical identity only; renderer text and pooled row labels are not read.
internal static unsafe class ExpertDeliveryNativeRows
{
    [StructLayout(LayoutKind.Explicit, Size = 0x298)]
    internal struct Window
    {
        [FieldOffset(0x288)] internal Row* Rows;
        [FieldOffset(0x290)] internal int Count;
    }
    [StructLayout(LayoutKind.Explicit, Size = 0x98)]
    internal struct Row
    {
        [FieldOffset(0x84)] internal uint ItemId;
        [FieldOffset(0x8C)] internal uint Container;
        [FieldOffset(0x90)] internal sbyte Slot;
        internal readonly ExpertDeliveryRowIdentity Identity => new(ItemId,Container,Slot);
    }
    internal static ExpertDeliveryRowIdentity[]? Read(void* address)
    {
        if (address==null) return null;
        var window=(Window*)address;
        if (window->Rows==null || window->Count is <1 or >4096) return null;
        var result=new ExpertDeliveryRowIdentity[window->Count];
        for (var i=0;i<result.Length;i++) result[i]=window->Rows[i].Identity;
        return result;
    }
    internal static bool StillMatches(void* address,int index,CompletionItemKey key)
    {
        if (address==null) return false;
        var window=(Window*)address;
        return window->Rows!=null && window->Count is >0 and <=4096 && index>=0 && index<window->Count
            && ExpertDeliverySubmitPolicy.RowMatches(window->Rows[index].Identity,key);
    }
}
