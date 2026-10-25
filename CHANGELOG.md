# 更新记录

## 0.1.0.28 — 2026-10-25

· 筹备稀有品增加实际提交和停止按钮，默认停止品级为 660。

### English

· Add expert-delivery submission and stop controls; default stopping item level is 660.

## 0.1.0.27 — 2026-10-25

· 增加筹备稀有品预览：按品级筛选并排除职业配装，仅展示，不提交。

### English

· Add an expert-delivery preview with item-level and gearset filtering; no items are submitted.

## 0.1.0.26 — 2026-10-23

· 自动补齐改为静默快速处理，同套缺件合并存入。

### English

· Complete outfits silently and faster by submitting missing pieces together.

## 0.1.0.25 — 2026-10-18

· 转 NQ 时自动关闭投影台，降级后重新打开并存回套装。

### English

· Close the dresser before lowering quality, then reopen it and store the NQ outfit.

## 0.1.0.24 — 2026-10-18

· 修复转 NQ 时卡在降低品质确认的问题。<br>
· 缩小品质处理预览，减少留白。

### English

· Fix HQ-to-NQ getting stuck at the quality confirmation.<br>
· Make the quality preview more compact.

## 0.1.0.23 — 2026-10-18

· 品质冲突处理改为通过原生窗口取出装备。

### English

· Retrieve conflicting outfit pieces through the native windows.

## 0.1.0.22 — 2026-10-17

· 修复重复打开“冲突转 NQ”预览时报错的问题。

### English

· Fix an error when reopening the HQ-to-NQ preview.

## 0.1.0.21 — 2026-10-17

· 调整冲突转 NQ 的取出时机，修正失败提示并增加诊断记录。

### English

· Adjust HQ-to-NQ retrieval timing, correct failure messages and add diagnostics.

## 0.1.0.20 — 2026-10-17

· 修复“建立套装”及转 NQ 后存回没有实际存入的问题。

### English

· Fix Create Outfit and HQ-to-NQ restoration failing to store the selected equipment.

## 0.1.0.19 — 2026-10-17

· 新增“建立套装”按钮，为未建立装备创建套装，支持中途停止。

### English

· Add a stoppable Create Outfit button for equipment without an existing outfit.

## 0.1.0.18 — 2026-10-17

· 修复 HQ/NQ 不一致的装备被误判为可补齐。<br>
· 新增“冲突转 NQ”，确认后自动取出 HQ 部件、降品质并存回。

### English

· Fix HQ/NQ mismatches being marked as completable.<br>
· Add a previewed HQ-to-NQ workflow that retrieves, lowers and restores conflicting outfit pieces.

## 0.1.0.17 — 2026-10-17

· 修复已打开收藏柜仍提示切换存入界面、无法一键存入的问题。

### English

· Fix one-click armoire storage incorrectly asking to open the deposit window.

## 0.1.0.16 — 2026-10-17

· 增加“可存收藏柜”状态与分类，自动补齐旁提示先处理收藏柜。<br>
· 打开收藏柜自动显示待收纳装备，支持一键存入和中途停止。<br>
· 收藏柜始终忽略穿戴与职业配装装备；重复副本和不满足原生条件的装备不自动存入。<br>
· 收藏柜数据未加载时显示待确认，避免自动补齐提前消耗未收录装备。

国服 API 15 编译及 88 项回归/国服数据检查通过；实际收藏柜显示与批量存入待游戏内验收。

### English

· Add a Cabinet category and “Can store in armoire” status, with a reminder beside Auto Complete.<br>
· Open the pending-item list with the armoire and store items with one click; click again to stop.<br>
· Always exclude equipped and saved gearset items; skip duplicate copies and items rejected by native requirements.<br>
· Keep collection status unknown until armoire data loads, preventing premature outfit consumption.

CN API 15 build and 88 regression/CN-data checks pass. In-game cabinet display and batch storage remain unverified.

## 0.1.0.15 — 2026-10-14

文档更新 / Documentation update

· 精简 README，合并功能与使用说明，移除预览，缩短反馈与声明。<br>
· Simplify the README: merge features and usage, remove the preview, and shorten feedback and the project statement.


· 修复勾选“隐去套装中的物品”时，打开投影台和补齐后刷新短暂显示未过滤装备的问题。<br>
· 读取已保存装备套装的物品 ID 与 HQ/NQ 登记快照，连续一致后才发布完整装备分类结果。<br>
· 数据尚未就绪时保留上一份完整结果，登记状态未知的装备不会在过滤开启时放行。<br>
· 修正初始空列表的默认提示；首次打开可能等待约一秒，取消过滤仍可查看全部候选装备。<br>
· 自动补齐等待完整过滤数据，避免使用中间刷新结果。

国服 API 15 编译及 66 项回归/国服数据检查通过。截图中的刷新闪现为用户观察结果，本版实际打开投影台、取消过滤与连续补齐后的稳定显示仍待实机验证。

### English

· Prevent unfiltered equipment briefly appearing when opening or refreshing the dresser with hide-gearset enabled.<br>
· Read saved gearset item IDs with HQ/NQ identities and publish a complete equipment snapshot only after consistent samples.<br>
· Retain the last complete snapshot while loading, and exclude unknown registration from a filtered view.<br>
· Correct the initial empty-list placeholder. First opening may wait about one second; disabling the filter still shows all candidates.<br>
· Auto Complete waits for complete filtering data instead of using an intermediate refresh.

CN API 15 compilation and 66 regression/CN-data checks pass. In-game stability when opening, toggling the filter and completing consecutive items still requires validation.


## 0.1.0.14 — 2026-10-14

· 补上自动补齐的第四步：最终确认窗中先勾选“套装幻影化”，再点击“是”。<br>
· 勾选后等待游戏处理，再进行最终确认；不重复点击“幻影化”或最终“是”。<br>
· 最终确认前再次核对来源装备、已有套装、缺少部位和提交批次；不匹配时停止。<br>
· 提交后继续等待套装保存与来源装备移出，完成后才处理下一件。

用户截图确认 0.1.0.13 已完成前面的合并确认及幻影化步骤，停在最终确认窗。国服 API 15 编译与 56 项回归/国服数据检查通过；新增勾选与最终确认仍待实机验收。

### English

· Add the fourth step: check Store as Outfit Glamour in the final dialog, then click Yes.<br>
· Wait for checkbox processing before final confirmation; never repeat Store as Glamour or the final Yes.<br>
· Revalidate the source items, existing outfit, missing parts and hand-in batch before confirming; stop on mismatch.<br>
· Wait for stored outfit parts and source-item removal before processing the next item.

The user's screenshot confirms that 0.1.0.13 reached the final confirmation after the earlier steps. CN API 15 compilation and 56 regression/CN-data checks pass; the new checkbox and final Yes still require in-game validation.


## 0.1.0.13 — 2026-10-14

· 修复通用“是/否”合并确认窗依赖专用窗口编号的归属判断，改为核对游戏实际注册的回调处理者。<br>
· 按游戏富文本解码提示，匹配本机国服 Addon 表第 15663 条已有套装合并文字。<br>
· 将提示文字、弹窗归属和按钮事件失败分别报告，并在 /xllog 记录详细诊断。<br>
· 保留当前来源装备、已有套装和提交批次检查，不接受其他操作的确认提示。

编译与 52 项回归/国服数据检查通过，实际提示已从本机游戏数据核对。上一版报错同时包含文字与归属判断，无法仅凭截图确定是哪项失败；本版修正识别路径并补充诊断，实际确认点击仍待游戏内验证。

### English

· Validate generic merge-confirmation ownership through the game's registered callback handler instead of specialized dialog IDs.<br>
· Decode the game's rich-text prompt and compare it with CN Addon row 15663.<br>
· Report text, ownership and button-event failures separately, with detailed diagnostics in /xllog.<br>
· Retain source-item, existing-outfit and batch checks; reject unrelated confirmations.

Compilation and 52 regression/CN-data checks pass; the actual prompt was verified from local game data. The earlier combined error could not identify which check failed from the screenshot alone. Native confirmation still needs in-game validation.


## 0.1.0.12 — 2026-10-14

· 修复自动补齐在“是否并入已有套装”提示前，过早检查补齐模式并停止的问题。<br>
· 将补齐模式检查移至确认后的最终提交阶段；确认“是”之前仍核对来源装备、弹窗归属与提示文字。<br>
· 等待确认窗口完成关闭，不再因关闭动画尚未结束而立即停止；确认只点击一次。<br>
· 保留已有套装记录、缺少部位、批次装备及棱晶检查，避免创建新套装或重复提交。

用户截图确认了先前的提前停止。编译与 49 项回归/国服数据检查通过；修复后的实际“是”和“幻影化”点击仍待游戏内验证。

### English

· Fix premature stopping before accepting the existing-outfit merge prompt.<br>
· Check existing-outfit mode at final submission after confirmation, while retaining source, dialog ownership and prompt checks before Yes.<br>
· Wait for the confirmation window to close instead of stopping during its closing animation; click confirmation only once.<br>
· Retain existing-outfit, missing-part, batch-membership and prism checks to prevent new outfit creation or duplicate submission.

The user's screenshot confirms the prior premature stop. Compilation and 49 regression/CN-data checks pass; the repaired native Yes and Store as Glamour clicks still need in-game validation.


## 0.1.0.11 — 2026-10-14

- 在“隐去套装中的物品”旁新增“自动补齐”按钮，处理本次点击时未被过滤的可补齐装备。
- 按打开套装幻影化、确认并入已有套装、点击幻影化的原生流程处理；不创建新套装、不取出装备。
- 运行时可点击“停止补齐”，底部显示进度与原因；已提交的操作仍可能完成。
- 每次提交核对物品栏格子、品质、套装缺少的部位与本次批次，防止原生自动选入被过滤或其他装备。
- 需要修理的装备跳过；材料不足、确认窗口不匹配、关闭窗口、切换角色或等待超时会停止，不重复提交。
- 确认套装已保存部件且源装备移出后才继续下一件。自动补齐消耗游戏要求的幻象棱晶。

国服 API 15 编译与 49 项离线/国服数据检查通过。实际三步确认、自动收纳、进度及停止表现仍待游戏内验收。多套装物品由游戏选择目标，若选择结果不符合补齐条件则停止。

### English

- Add an Auto Complete button beside the hide-gearset checkbox, acting on completable items included when the batch starts.
- Open the native outfit conversion, confirm merging into an existing outfit, and press Store as Glamour; never create new outfits or restore items.
- Allow stopping subsequent actions and show progress or the stop reason. Already submitted requests may still finish.
- Check inventory slot, quality, missing outfit part and batch membership for every hand-in, including native auto-filled items.
- Skip items needing repair. Stop on insufficient prisms, unrelated confirmations, closing windows, character changes or timeouts without retrying submission.
- Continue only after the outfit part is recorded and the source item leaves inventory. The operation consumes the game's required glamour prisms.

CN API 15 compilation and 49 offline/CN-data checks pass. The three-step native flow, storage, progress and stopping still need in-game validation. The game chooses the destination for multi-outfit items; a non-completable choice stops the batch.


## 0.1.0.10 — 2026-10-14

- 修复原生装备图标未随节点尺寸缩放，导致越过行分隔线并遮挡名称的问题。
- 开启图标纹理自适应与拉伸，将完整图标缩放到 24 × 24 像素，保持 28 像素紧凑行距。
- 保留“隐去套装中的物品”原生用语、左侧过滤和默认可补齐分类。

国服 API 15 编译通过。图标实际显示、滚动复用与不同 UI 缩放下的效果仍待游戏内确认。

### English

- Fix native equipment icons rendering beyond their row and overlapping names because node dimensions alone did not scale the texture.
- Enable texture auto-fit and stretch to display the full icon within 24 × 24 pixels, keeping compact 28 px rows.
- Retain the native Chinese checkbox wording, left-side filter and default Completable category.

CN API 15 compilation passes. Icon rendering, recycled rows during scrolling and different UI scales still require in-game confirmation.


## 0.1.0.9 — 2026-10-14

同版本文字修订：过滤开关改为“隐去套装中的物品”，与游戏原生用语一致。
Same-version text revision: match the game-native Chinese checkbox label.

- 主窗口改用 KamiToolKit 创建的游戏原生窗口、勾选框、分类标签、图标及滚动列表。
- 使用紧凑行距：每行 28 像素，装备图标 24 像素；长名称省略显示，悬停查看完整信息。
- 将“隐藏套装中的物品”移至左侧，保留过滤选择与默认“可补齐”分类。
- 保留部位分组、自动刷新及原生装备选择列表的可补齐名称标红。
- 改用异步加载和卸载，关闭窗口时释放自建节点；更新安装包，包含原生 UI 依赖与 MIT 许可证。

国服 API 15 编译与离线回归检查通过。原生外观、滚动、主题/缩放及反复关闭与卸载仍待游戏内验收。

### English

- Replace the main window with game-native controls built with KamiToolKit, including tabs, checkbox, icons and a scrolling equipment list.
- Use compact 28 px rows with 24 px icons; truncate long labels and show full details on hover.
- Move the hide-gearset-items checkbox to the left; retain its saved choice and open on the Completable tab by default.
- Preserve equipment grouping, automatic refresh and completable-item name highlighting in the game's equipment selector.
- Load and unload asynchronously, release owned nodes when closing, and include the native UI dependency and its MIT license in the download.

CN API 15 compilation and offline regression checks pass. Native rendering, scrolling, themes/scaling, repeated closing and unloading still require in-game validation.


## 0.1.0.8 — 2026-10-14

- 将分类下拉框改为单击标签，每次打开默认显示“可补齐”。
- 装备列表改为图标、名称、部位、所属套装和状态五列，按游戏部位顺序排列并分组。
- 移除顶部大段说明、扫描时间、手动刷新按钮和搜索框；保留自动刷新与装备套装过滤。
- 原生“幻影化”装备选择列表的可补齐装备名称标红；关闭原生界面或禁用插件时恢复。
- 标红不受插件当前分类影响，数据待确认时不标红，同名但状态有歧义时保守跳过。

编译、排序与高亮目标筛选检查通过；原生颜色显示、滚动翻页和恢复仍待游戏内验收。

## 0.1.0.7 — 2026-10-14

- 新增“已保存相同套装”“可补齐现有套装”“尚未建立套装”三类状态。
- 按投影台内的套装记录和具体部件是否保存来判断；散件存放不算建立套装。
- 新增状态筛选、颜色提示与分类数量，鼠标悬停状态可查看各所属套装详情。
- 多套装情况下优先显示可补齐状态；数据未加载时显示“待确认”，避免误判为未建立。

三类状态不自动操作游戏，品质与收纳条件仍需游戏确认。编译和离线分类检查通过；截图中两类状态与尚未建立套装的实机结果仍待验收。

## 0.1.0.6 — 2026-10-14

- 新增“隐藏套装中的物品”勾选框，过滤已登记到装备套装（职业配装）中的装备。
- 使用游戏原生装备套装登记判断，显示过滤后数量及隐藏数量。
- 保存过滤选择；默认不隐藏，且不改变游戏窗口自身的勾选状态。
- 过滤可以与名称搜索同时使用，未确认登记状态的物品保留显示并提示。

已确认先前 36 件与游戏 30 件的差异来自游戏的装备套装过滤，并非多读了 6 件物品。新开关的实际显示和保存表现仍待游戏内验收。

## 0.1.0.5 — 2026-10-14

- 新增套装幻影化装备汇总，覆盖物品栏四页和兵装库各装备分类，无需逐页翻找。
- 显示装备名称、HQ/NQ、所在位置和所属套装，重复副本分别列出。
- 新增装备与套装名称搜索、立即刷新和每秒自动刷新。
- 打开投影台自动显示列表，关闭投影台或切换角色时清空结果。
- 数据未完整加载时提示等待，读取失败时清空结果并延迟重试。

范围仅限物品栏与兵装库；支持套装幻影化不代表已集齐整套。编译、离线筛选与安装包核对通过，实际游戏内显示及刷新仍待验收。

## 0.1.0.4 — 2026-10-13

- 将内部标识、代码命名空间、程序集、项目目录和 GitHub 仓库统一改为 SuitKing。
- 更新发布清单、下载地址及文档链接。

从命名空间为 `glamourking` 的旧版迁移时，请先卸载旧插件再安装 SuitKing；统一库链无需更换。

## 0.1.0.3 — 2026-10-13

- 显示名称改为“套装之王 SuitKing”，同步窗口文字和使用说明。
- 新增 `/suitking` 与 `/sking`，移除 `/glamking`。

## 0.1.0.2 — 2026-10-13

- 插件安装器显示名称改为“幻化之王 GlamourKing”，中英文并存。

## 0.1.0.1 — 2026-10-13

- 添加斗篷图标及图标地址。

## 0.1.0.0 — 2026-10-13

- 发布首个 Dalamud API 15 基准版本。
- 打开幻化柜时显示测试窗口，关闭时收起。
- 提供 `/glamking` 手动开关窗口。
