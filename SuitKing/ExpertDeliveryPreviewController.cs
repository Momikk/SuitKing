using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;

namespace SuitKing;

internal sealed unsafe class ExpertDeliveryPreviewController(Plugin plugin) : IDisposable
{
    internal const string AddonName = "GrandCompanySupplyList";
    private readonly ExpertDeliveryPreviewReader reader = new();
    private TextButtonNode? button;
    private ulong character;
    private int? pendingLevel;
    private long deadline;
    private long retryAfter;
    private bool wasContextOpen;
    private ulong contextCharacter;

    internal ExpertDeliverySubmitter Submission { get; } = new(plugin);
    internal void Initialize()
    {
        Plugin.Addons.RegisterListener(AddonEvent.PreFinalize, AddonName, FinalizeAddon);
        Plugin.Toasts.ErrorToast += OnError;
    }
    private void OnError(ref SeString message, ref bool handled) => Submission.NotifyError(message.TextValue);
    private static bool NativeDialogOpen
    {
        get
        {
            foreach (var name in new[] { "GrandCompanySupplyReward", "SelectYesno" })
            {
                var h = Plugin.GameGui.GetAddonByName(name);
                if (!h.IsNull && h.IsReady && h.IsVisible) return true;
            }
            return false;
        }
    }
    internal bool ContextOpen
    {
        get
        {
            var handle = Plugin.GameGui.GetAddonByName(AddonName);
            return Plugin.Player.ContentId != 0 && !handle.IsNull && handle.IsReady && handle.IsVisible
                && ((AddonGrandCompanySupplyList*)handle.Address)->SelectedTab == 2;
        }
    }
    internal void Request(int level)
    {
        reader.Reset(); character = Plugin.Player.ContentId; pendingLevel = level;
        deadline = Environment.TickCount64 + 5000;
        plugin.DeliveryPreview.SetStatus("正在读取筹备列表与职业配装过滤……");
    }
    internal void CancelPending() => pendingLevel = null;
    internal void Tick()
    {
        try
        {
            Submission.Tick();
            if (Environment.TickCount64 < retryAfter) return;
            var handle = Plugin.GameGui.GetAddonByName(AddonName);
            if (!handle.IsNull && handle.IsReady && handle.IsVisible && ContextOpen)
            {
                if (wasContextOpen && contextCharacter != Plugin.Player.ContentId)
                { pendingLevel = null; plugin.DeliveryPreview.Close(); }
                wasContextOpen = true; contextCharacter = Plugin.Player.ContentId;
                var addon = (AtkUnitBase*)handle.Address;
                if (button == null && addon->RootNode != null)
                {
                    button = new TextButtonNode { String = "筹备预览", Size = new(160, 28),
                        TextTooltip = "输入停止品级，先生成预览，再提交换取军票，固定排除职业配装。",
                        OnClick = () => plugin.DeliveryPreview.Present() };
                    try { button.AttachNode(addon); } catch { button.Dispose(); button = null; throw; }
                }
                if (button != null) { button.Position = new(400, 38); button.IsVisible = true; }
            }
            else
            {
                if (button != null) button.IsVisible = false;
                pendingLevel = null;
                if (wasContextOpen && !Submission.WaitingForNative && !NativeDialogOpen)
                { wasContextOpen = false; Submission.Stop("筹备界面已关闭。"); plugin.DeliveryPreview.Close(); }
            }
            if (pendingLevel is not { } level) return;
            if (Plugin.Player.ContentId != character || !ContextOpen) { CancelPending(); plugin.DeliveryPreview.Close(); return; }
            if (Environment.TickCount64 > deadline) throw new InvalidOperationException("读取超时，请重新生成预览。");
            if (reader.TryRead(level, out var plan))
            {
                pendingLevel = null;
                plugin.DeliveryPreview.ShowPlan(plan!);
            }
        }
        catch (Exception error)
        {
            pendingLevel = null; retryAfter = Environment.TickCount64 + 2000;
            plugin.DeliveryPreview.SetStatus(error.Message);
            Plugin.Log.Warning(error, "Expert-delivery read-only preview failed");
        }
    }
    private void FinalizeAddon(AddonEvent type, AddonArgs args)
    {
        pendingLevel = null;
        button?.Dispose(); button = null;
        if (!Submission.WaitingForNative) { wasContextOpen = false; Submission.Stop("筹备界面已关闭。"); plugin.DeliveryPreview.Close(); }
    }
    public void Dispose()
    {
        Plugin.Toasts.ErrorToast -= OnError;
        Submission.Stop("插件已禁用。");
        Plugin.Addons.UnregisterListener(AddonEvent.PreFinalize, AddonName, FinalizeAddon);
        pendingLevel = null; button?.Dispose(); button = null;
    }
}
