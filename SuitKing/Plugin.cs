using Dalamud.Game.Command;
using KamiToolKit;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using SuitKing.Windows;

namespace SuitKing;

public sealed class Plugin : IAsyncDalamudPlugin
{
    public const string DresserAddonName = "MiragePrismPrismBox";
    private const string SelectionAddonName = "MiragePrismPrismBoxCrystallize";
    private const string CommandName = "/suitking";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ITextureProvider Textures { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle Addons { get; private set; } = null!;
    [PluginService] internal static IDataManager Data { get; private set; } = null!;
    [PluginService] internal static IPlayerState Player { get; private set; } = null!;
    [PluginService] internal static ICondition Conditions { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static ITargetManager Targets { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;

    private NativeMainWindow mainWindow = null!;
    private readonly NativeEquipmentHighlighter highlighter;
    private long nextScan;
    private ulong characterId;
    private bool selectionWasOpen;
    private string? lastError;
    private bool isDisposing;

    internal OutfitInventoryReader Inventory { get; } = new();
    internal Configuration Config { get; }
    internal OutfitAutoCompleter Completion { get; }
    internal CabinetAutoStore CabinetStore { get; }
    internal QualityNormalizer Quality { get; }
    internal OutfitCreator Creation { get; }
    internal ExpertDeliveryPreviewController Delivery { get; private set; } = null!;
    internal ExpertDeliveryPreviewWindow DeliveryPreview { get; private set; } = null!;
    internal QualityPreviewWindow QualityPreview { get; private set; } = null!;
    public bool IsCabinetOpen { get; private set; }
    public bool IsCabinetStoreOpen => IsCabinetOpen && IsVisible("Cabinet");
    public bool IsDresserOpen { get; private set; }

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Completion = new OutfitAutoCompleter(this);
        CabinetStore = new CabinetAutoStore(this);
        Quality = new QualityNormalizer(this);
        Creation = new OutfitCreator(this);
        highlighter = new NativeEquipmentHighlighter(this);
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await KamiToolKitLibrary.InitializeAsync(PluginInterface);
        cancellationToken.ThrowIfCancellationRequested();
        await Framework.Run(() =>
        {
            mainWindow = new NativeMainWindow(this)
            {
                InternalName = "SuitKingNative", Title = "套装之王 SuitKing", Size = new(760, 520),
            };
            QualityPreview = new QualityPreviewWindow(this) { Title = "处理品质冲突", InternalName = "SuitKingQualityPreview" };
            DeliveryPreview = new ExpertDeliveryPreviewWindow(this) { Title = "筹备稀有品预览（不提交）", InternalName = "SuitKingDeliveryPreview", Size = new(660, 480) };
            Delivery = new ExpertDeliveryPreviewController(this);
            Delivery.Initialize();
            CommandsAdd(CommandName);
            CommandsAdd("/sking");
            PluginInterface.UiBuilder.OpenMainUi += OpenMainUi;
            Framework.Update += OnFrameworkUpdate;
            Log.Information("SuitKing native UI loaded (API {ApiLevel}).", PluginInterface.Manifest.DalamudApiLevel);
        });
    }

    private void CommandsAdd(string command) => CommandManager.AddHandler(command,
        new CommandInfo(OnCommand) { HelpMessage = "打开或关闭套装之王套装装备列表。" });

    public async ValueTask DisposeAsync()
    {
        isDisposing = true;
        await Framework.Run(() =>
        {
            if (Completion.IsRunning) Completion.Stop("插件已禁用。");
            if (CabinetStore.IsRunning) CabinetStore.Stop("插件已禁用。");
            if (Quality.IsRunning) Quality.Stop("插件已禁用。");
            if (Creation.IsRunning) Creation.Stop("插件已禁用。");
            Delivery?.Dispose();
            highlighter.Dispose();
            Framework.Update -= OnFrameworkUpdate;
            PluginInterface.UiBuilder.OpenMainUi -= OpenMainUi;
            CommandManager.RemoveHandler(CommandName);
            CommandManager.RemoveHandler("/sking");
        });
        if (mainWindow is not null) await mainWindow.DisposeAsync();
        if (QualityPreview is not null) await QualityPreview.DisposeAsync();
        if (DeliveryPreview is not null) await DeliveryPreview.DisposeAsync();
        await KamiToolKitLibrary.DisposeAsync();
        Inventory.Clear();
    }

    private bool IsVisible(string name)
    {
        var addon = GameGui.GetAddonByName(name);
        return !addon.IsNull && addon.IsReady && addon.IsVisible;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        Delivery.Tick();
        var contentId = Player.ContentId;
        if (contentId != characterId)
        {
            characterId = contentId;
            Inventory.Clear();
            nextScan = 0;
            IsDresserOpen = false;
            IsCabinetOpen = false;
            selectionWasOpen = false;
            mainWindow.Close();
        }
        var selectionOpen = contentId != 0 && IsVisible(SelectionAddonName);
        var open = contentId != 0 && (IsVisible(DresserAddonName) || selectionOpen);
        var cabinetOpen = contentId != 0 && (IsVisible("Cabinet") || IsVisible("CabinetWithdraw"));
        if (open != IsDresserOpen || cabinetOpen != IsCabinetOpen)
        {
            IsDresserOpen = open;
            IsCabinetOpen = cabinetOpen;
            if (Completion.IsRunning) Completion.Stop("收纳界面已切换。");
            if (CabinetStore.IsRunning) CabinetStore.Stop("收纳界面已切换。");
            var qualityTransition = Quality.IsRunning && Quality.AcceptsContextTransition(open, cabinetOpen);
            if (Quality.IsRunning && !qualityTransition) Quality.Stop("收纳界面已切换。");
            if (Creation.IsRunning) Creation.Stop("收纳界面已切换。");
            QualityPreview.Close();
            if (open || cabinetOpen) mainWindow.OpenOnCompletable();
            else
            {
                // Keep Stop available while our quality job temporarily leaves the dresser.
                if (!qualityTransition) mainWindow.Close();
                highlighter.Restore();
            }
            Inventory.Clear();
            nextScan = 0;
        }
        if (selectionOpen && !selectionWasOpen && !cabinetOpen)
        {
            mainWindow.OpenOnCompletable();
            nextScan = 0;
        }
        selectionWasOpen = selectionOpen;
        mainWindow.TickPendingOpen();
        Completion.Tick();
        CabinetStore.Tick();
        Quality.Tick();
        Creation.Tick();
        if (!(open || cabinetOpen) || Environment.TickCount64 < nextScan)
            return;
        nextScan = Environment.TickCount64 + 1000;
        try
        {
            Inventory.Refresh(!cabinetOpen);
            lastError = null;
        }
        catch (Exception exception)
        {
            Inventory.Fail("读取失败，请查看 /xllog 中的 SuitKing 报错。");
            nextScan = Environment.TickCount64 + 5000;
            if (lastError != exception.Message)
                Log.Error(exception, "SuitKing could not read outfit equipment.");
            lastError = exception.Message;
        }
    }

    internal void RequestRefresh() => nextScan = 0;
    internal void SaveConfiguration() => PluginInterface.SavePluginConfig(Config);
    private void OnCommand(string command, string args) => QueueWindowAction(window =>
    {
        if (window.IsOpen) window.Close();
        else window.OpenOnCompletable();
    });

    private void OpenMainUi() => QueueWindowAction(window => window.OpenOnCompletable());

    private void QueueWindowAction(Action<NativeMainWindow> action)
    {
        // UiBuilder events may run on the render thread. Native nodes belong to the game thread.
        _ = Framework.Run(() =>
        {
            if (isDisposing || mainWindow is null) return;
            try { action(mainWindow); }
            catch (Exception exception) { Log.Error(exception, "SuitKing native window action failed."); }
        });
    }
}