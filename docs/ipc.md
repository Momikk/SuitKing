# SuitKing IPC v1

其他 Dalamud 插件可调用以下 IPC。除版本查询外，调用请放在 `IFramework.Run` 中，或从自己的 `IFramework.Update` 调用。IPC 名称和操作名区分大小写。

| IPC | Dalamud 泛型签名 | 参数 |
| --- | --- | --- |
| `SuitKing.GetApiVersion` | `<int>` | 无，当前返回 1 |
| `SuitKing.Query` | `<string, string>` | 操作名 |
| `SuitKing.Execute` | `<string, string, string>` | 操作名、参数；无参数传空字符串 |

查询和执行均返回 JSON 字符串，格式为 `{"ok":true,"code":"Ok","message":"","data":null}`。用 `ok` 判断调用是否受理，失败时可按 `code` 分支处理，例如 `WrongThread`、`Busy`、`InvalidArgument`、`StalePreview` 或 `Rejected`。`message` 可用于显示。`ok: true` 表示当前调用成功，批量任务的实际状态另用 `State` 读取。未安装或未加载 SuitKing 时，Dalamud IPC 调用可能抛出未注册异常。

| Query | data |
| --- | --- |
| `State` | 原生窗口是否打开、装备数据是否就绪、职业配装筛选开关、选中分类。各任务的 `running` 和 `status` 位于 `completion`、`creation`、`cabinet`、`quality`、`delivery` |
| `Equipment` | 按职业配装筛选开关返回装备快照，每件含 `itemId`、`name`、`location`、`slot`、`highQuality` 和 `state` 等字段 |
| `CabinetCandidates` | 收藏柜收纳候选，以现有收藏柜规则排序和过滤。实际收纳每个收藏柜条目最多一份 |
| `QualityPreview` | 当前预览的 `previewId`、套装、需取出并降为 NQ 的物品 ID、一起存回的装备和棱晶数量。没有有效预览时为 `null` |
| `DeliveryPreview` | `pending` 表示还在生成，`preview` 含 `previewId`、`stopLevel`、`items`、`sealsAfter`、`stopReason`。没有有效预览时 `preview` 为 `null` |

| Execute | 参数 | 用途 |
| --- | --- | --- |
| `OpenMain` / `CloseMain` | 空 | 打开或关闭插件主窗口 |
| `SelectCategory` | `All`、`Saved`、`CanComplete`、`QualityConflict`、`NotCreated`、`CanStoreCabinet` 或 `Unknown` | 切换已打开主窗口的分类 |
| `SetHideGearsetItems` | `true` 或 `false` | 保存职业配装筛选设置 |
| `RefreshEquipment` | 空 | 请求在下一次游戏更新中刷新装备快照 |
| `StartCompletion` / `StopCompletion` | 空 | 开始或停止套装补齐，需打开投影台 |
| `StartCreation` / `StopCreation` | 空 | 开始或停止建立套装，需打开投影台 |
| `StartCabinetStore` / `StopCabinetStore` | 空 | 开始或停止收藏柜收纳，需打开收藏柜存入页 |
| `OpenQualityPreview` / `StopQuality` | 空 | 打开品质转换预览，或取消预览 / 停止转换 |
| `ConfirmQuality` | `previewId` | 确认当前套装永久降为 NQ 并存回 |
| `OpenDeliveryPreview` / `CloseDeliveryPreview` | 空 | 打开或关闭筹备预览，需打开原生筹备稀有品页 |
| `GenerateDeliveryPreview` | `1` 至 `9999` 的停止品级 | 打开并生成预览，只选择低于该品级且符合过滤规则的装备 |
| `ConfirmDelivery` | `previewId` | 确认当前预览，开始交付并消耗其中的装备 |
| `StopDelivery` | 空 | 停止继续交付 |

品质转换与筹备交付均先查询实际清单，再把当次 `previewId` 原样传入对应的确认操作。重新生成、关闭预览或确认后，旧编号失效。任务沿用游戏界面、职业配装、物品来源等原有条件，已发出的游戏请求仍可能在停止后完成。

```csharp
var query = pluginInterface.GetIpcSubscriber<string, string>("SuitKing.Query");
var execute = pluginInterface.GetIpcSubscriber<string, string, string>("SuitKing.Execute");

var result = await framework.Run(() => execute.InvokeFunc("GenerateDeliveryPreview", "660"));
// 在后续 Framework 更新中查询，等 pending 为 false 并读取返回的清单。
var preview = await framework.Run(() => query.InvokeFunc("DeliveryPreview"));
```
