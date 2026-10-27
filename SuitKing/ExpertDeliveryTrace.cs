namespace SuitKing;

// No native calls: diagnostics must not affect selection, confirmation or retry policy.
internal sealed class ExpertDeliveryTrace(Action<string> info, Action<string> warning, Func<long>? clock = null)
{
    private readonly Func<long> now = clock ?? (() => Environment.TickCount64);
    private string run = "not-started";
    private long started;
    private long nextWait;
    private int sequence;
    private string? dialog;
    internal void Begin()
    {
        run = Guid.NewGuid().ToString("N")[..8]; started = now(); sequence = 0;
        nextWait = 0; dialog = null;
    }
    internal void ResetStep() { nextWait = 0; dialog = null; }
    internal void Emit(string kind, string details, bool rejected = false)
    {
        var line = $"[Delivery run={run} seq={++sequence} +{now()-started}ms] {kind}: {Clean(details,12000)}";
        if (rejected) warning(line); else info(line);
    }
    internal void Wait(string details)
    {
        if (now() < nextWait) return;
        nextWait = now() + 2000; Emit("Wait",details);
    }
    internal void Dialog(string details, bool rejected = false)
    {
        if (!rejected && details == dialog) return;
        dialog = details; Emit("Dialog",details,rejected);
    }
    internal static string Clean(string value, int limit = 240)
    {
        var text = value.Replace("\r","\\r").Replace("\n","\\n").Replace("\t","\\t");
        return text.Length <= limit ? text : text[..limit]+"...[truncated]";
    }
}
