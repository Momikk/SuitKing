using System.Text.Json;

namespace SuitKing;

internal sealed class SuitKingIpc(Plugin plugin) : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly List<Action> unregister = [];

    internal void Register()
    {
        var apiVersion = Plugin.PluginInterface.GetIpcProvider<int>("SuitKing.GetApiVersion");
        var query = Plugin.PluginInterface.GetIpcProvider<string, string>("SuitKing.Query");
        var execute = Plugin.PluginInterface.GetIpcProvider<string, string, string>("SuitKing.Execute");

        try
        {
            apiVersion.RegisterFunc(() => 1);
            unregister.Add(apiVersion.UnregisterFunc);
            query.RegisterFunc(operation => Invoke(() => Query(operation)));
            unregister.Add(query.UnregisterFunc);
            execute.RegisterFunc((operation, argument) => Invoke(() => Execute(operation, argument)));
            unregister.Add(execute.UnregisterFunc);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        foreach (var remove in unregister) remove();
        unregister.Clear();
    }

    private string Invoke(Func<Reply> action)
    {
        if (!Plugin.Framework.IsInFrameworkUpdateThread)
            return Serialize(new(false, "WrongThread", "请在 Dalamud Framework 线程调用。"));
        if (plugin.IsDisposing)
            return Serialize(new(false, "Unavailable", "插件正在卸载。"));

        try
        {
            return Serialize(action());
        }
        catch (Exception error)
        {
            Plugin.Log.Error(error, "SuitKing IPC request failed.");
            return Serialize(new(false, "Error", "操作失败，请查看 SuitKing 日志。"));
        }
    }

    private Reply Query(string operation) => operation switch
    {
        "State" => Success(new
        {
            plugin.IsDresserOpen,
            plugin.IsCabinetOpen,
            plugin.IsCabinetStoreOpen,
            expertDeliveryOpen = plugin.Delivery.ContextOpen,
            mainWindowOpen = plugin.MainWindow.IsOpen,
            selectedCategory = plugin.MainWindow.SelectedCategory,
            plugin.Config.HideGearsetItems,
            inventoryComplete = plugin.Inventory.IsComplete,
            plugin.Inventory.AreOutfitsLoaded,
            plugin.Inventory.IsCabinetLoaded,
            inventoryStatus = plugin.Inventory.Status,
            inventoryUpdatedAt = plugin.Inventory.UpdatedAt,
            completion = Job(plugin.Completion.IsRunning, plugin.Completion.Status),
            creation = Job(plugin.Creation.IsRunning, plugin.Creation.Status),
            cabinet = Job(plugin.CabinetStore.IsRunning, plugin.CabinetStore.Status),
            quality = Job(plugin.Quality.IsRunning, plugin.Quality.Status),
            delivery = Job(plugin.Delivery.Submission.IsRunning, plugin.Delivery.Submission.Status),
        }),
        "Equipment" => Success(OutfitDisplayFilter.Apply(plugin.Inventory.Equipment, plugin.Config.HideGearsetItems, "")
            .Select(Equipment)),
        "CabinetCandidates" => Success(plugin.Inventory.CabinetEquipment.Select(Equipment)),
        "QualityPreview" => Success(plugin.QualityPreview.Preview is { } quality && plugin.QualityPreview.PreviewId is { } qualityId
            ? new { previewId = qualityId, outfitId = quality.Definition.Id, outfitName = quality.Definition.Name,
                restoreAndLowerItemIds = quality.Definition.Pieces.Where(quality.Stored.Pieces.Contains),
                additional = quality.Additional.Select(Equipment), quality.PrismCount }
            : null, plugin.Quality.Status),
        "DeliveryPreview" => Success(new
        {
            pending = plugin.Delivery.PreviewPending,
            preview = plugin.Delivery.ContextOpen && plugin.DeliveryPreview.Preview is { } delivery
                && plugin.DeliveryPreview.PreviewId is { } deliveryId
                ? new { previewId = deliveryId, stopLevel = plugin.DeliveryPreview.PreviewStopLevel, delivery.SealsAfter,
                    delivery.StopReason, delivery.Items }
                : null,
        }, plugin.DeliveryPreview.PreviewStatus),
        _ => InvalidOperation(),
    };

    private Reply Execute(string operation, string argument) => operation switch
    {
        "OpenMain" => Perform(plugin.MainWindow.OpenOnCompletable),
        "CloseMain" => Perform(plugin.MainWindow.Close),
        "SelectCategory" => plugin.MainWindow.SelectCategory(argument) ? Success()
            : new(false, "InvalidArgument", "分类不可用。"),
        "SetHideGearsetItems" => SetFilter(argument),
        "RefreshEquipment" => Perform(plugin.RequestRefresh),
        "StartCompletion" => Start(plugin.Completion.Toggle, () => plugin.Completion.IsRunning, () => plugin.Completion.Status),
        "StopCompletion" => Stop(plugin.Completion.IsRunning, () => plugin.Completion.Stop("已请求停止补齐。")),
        "StartCreation" => Start(plugin.Creation.Toggle, () => plugin.Creation.IsRunning, () => plugin.Creation.Status),
        "StopCreation" => Stop(plugin.Creation.IsRunning, () => plugin.Creation.Stop("已请求停止建立套装。")),
        "StartCabinetStore" => Start(plugin.CabinetStore.Toggle, () => plugin.CabinetStore.IsRunning, () => plugin.CabinetStore.Status),
        "StopCabinetStore" => Stop(plugin.CabinetStore.IsRunning, () => plugin.CabinetStore.Stop("已请求停止收纳。")),
        "OpenQualityPreview" => OpenQualityPreview(),
        "ConfirmQuality" => ConfirmQuality(argument),
        "StopQuality" => StopQuality(),
        "OpenDeliveryPreview" => OpenDeliveryPreview(),
        "GenerateDeliveryPreview" => GenerateDeliveryPreview(argument),
        "ConfirmDelivery" => ConfirmDelivery(argument),
        "StopDelivery" => Stop(plugin.Delivery.Submission.IsRunning,
            () => plugin.Delivery.Submission.Stop("已请求停止交付。")),
        "CloseDeliveryPreview" => Perform(plugin.DeliveryPreview.Close),
        _ => InvalidOperation(),
    };

    private Reply SetFilter(string argument)
    {
        if (!bool.TryParse(argument, out var value))
            return new(false, "InvalidArgument", "请输入 true 或 false。");
        return plugin.HasRunningJob ? Busy() : Perform(() => plugin.SetHideGearsetItems(value));
    }

    private Reply OpenQualityPreview()
    {
        if (plugin.HasRunningJob) return Busy();
        plugin.Quality.OpenPreview();
        return plugin.QualityPreview.IsAllocated && plugin.QualityPreview.PreviewId is not null
            ? Success(message: plugin.Quality.Status) : Rejected(plugin.Quality.Status);
    }

    private Reply ConfirmQuality(string id)
    {
        if (plugin.HasRunningJob) return Busy();
        if (plugin.QualityPreview.Preview is null || id != plugin.QualityPreview.PreviewId) return StalePreview();
        plugin.QualityPreview.ConfirmPreview();
        return Started(plugin.Quality.IsRunning, plugin.Quality.Status);
    }

    private Reply StopQuality()
    {
        if (plugin.Quality.IsRunning) return Perform(() => plugin.Quality.Stop("已请求停止品质转换。"));
        if (plugin.QualityPreview.PreviewId is not null) return Perform(plugin.QualityPreview.CancelPreview);
        return Success();
    }

    private Reply OpenDeliveryPreview()
    {
        if (plugin.HasRunningJob) return Busy();
        if (!plugin.Delivery.ContextOpen) return Rejected("请先打开原生筹备稀有品页。");
        plugin.DeliveryPreview.Present();
        return plugin.DeliveryPreview.IsAllocated ? Success() : Rejected("无法打开筹备预览。");
    }

    private Reply GenerateDeliveryPreview(string argument)
    {
        if (!int.TryParse(argument, out var level) || level is < 1 or > 9999)
            return new(false, "InvalidArgument", "请输入 1 至 9999 的停止品级。");
        var opened = OpenDeliveryPreview();
        if (!opened.Ok) return opened;
        plugin.DeliveryPreview.SetStopLevel(level);
        plugin.Delivery.Request(level);
        return Success(message: plugin.DeliveryPreview.PreviewStatus);
    }

    private Reply ConfirmDelivery(string id)
    {
        if (plugin.HasRunningJob) return Busy();
        if (!plugin.Delivery.ContextOpen || plugin.DeliveryPreview.Preview is not { } plan
            || id != plugin.DeliveryPreview.PreviewId) return StalePreview();
        if (plan.Items.Count == 0) return Rejected(plan.StopReason);
        plugin.DeliveryPreview.ConfirmPreview();
        return Started(plugin.Delivery.Submission.IsRunning, plugin.Delivery.Submission.Status);
    }

    private Reply Start(Action start, Func<bool> running, Func<string> status)
    {
        if (plugin.HasRunningJob) return Busy();
        start();
        return Started(running(), status());
    }

    private static Reply Started(bool running, string status)
        => running ? Success(message: status) : Rejected(status);

    private static Reply Stop(bool running, Action stop) => running ? Perform(stop) : Success();

    private static Reply Perform(Action action)
    {
        action();
        return Success();
    }

    private static object Equipment(OutfitEquipment item) => new
    {
        item.ItemId, item.Name, item.HighQuality, item.Location, item.Slot, item.Condition,
        item.SetNames, item.RegisteredToGearset, item.IconId, item.Part, item.CabinetId, item.InCabinet,
        state = item.Assessment?.State.ToString() ?? "Unknown",
        details = item.Assessment?.Details,
    };

    private static object Job(bool running, string status) => new { running, status };
    private static Reply InvalidOperation() => new(false, "UnknownOperation", "未知的 SuitKing IPC 操作。");
    private static Reply Busy() => new(false, "Busy", "已有装备操作正在运行。");
    private static Reply StalePreview() => new(false, "StalePreview", "预览已失效，请重新生成并读取编号。");
    private static Reply Rejected(string message) => new(false, "Rejected", message);
    private static Reply Success(object? data = null, string message = "") => new(true, "Ok", message, data);
    private static string Serialize(Reply value) => JsonSerializer.Serialize(value, Json);
    private sealed record Reply(bool Ok, string Code, string Message, object? Data = null);
}
