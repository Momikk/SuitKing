using SuitKing;
using Lumina;
using Lumina.Excel.Sheets;

var catalog = new Dictionary<uint, OutfitInfo>
{
    [10] = new("套装上衣", "套装甲"),
    [20] = new("套装戒指", "套装乙、套装丙"),
};
OutfitInfo? Find(uint id) => catalog.GetValueOrDefault(id);
var fixture = new List<InventoryEntry>
{
    new(10, false, "物品栏 1", 1, 30000),
    new(10, true, "物品栏 4", 35, 30000),
    new(20, false, "兵装库·戒指", 50, 10000),
    new(999, false, "兵装库·身体", 2, 30000),
    new(0, false, "物品栏 2", 1, 30000),
    new(10, false, "兵装库·主手", 2, 30000),
};
var result = OutfitListBuilder.Build(fixture, Find);
void Check(bool ok, string name)
{
    if (!ok) throw new Exception(name);
    Console.WriteLine($"PASS: {name}");
}
Check(result.Count == 4, "All supplied pages included; ordinary and empty slots excluded");
Check(result.Count(x => x.ItemId == 10) == 3, "Duplicate owned copies retained");
Check(result.Any(x => x.HighQuality && x.Location == "物品栏 4" && x.Slot == 35), "HQ and inventory position retained");
Check(result.Any(x => x.Location == "兵装库·主手"), "Weapon category included");
Check(result.Single(x => x.ItemId == 20).SetNames.Contains("套装丙"), "Multiple outfit memberships retained");
Check(result.Single(x => x.ItemId == 20).Condition == 10000, "Damaged gear remains identifiable instead of silently disappearing");
Check(OutfitListBuilder.Build([], Find).Count == 0, "Empty inventory handled");
Check(OutfitListBuilder.Build(fixture.Where(x => x.ItemId != 10), Find).Count == 1, "Removed items disappear on refreshed snapshot");

var thirtySix = Enumerable.Range(1, 36).Select(i => new OutfitEquipment((uint)i, $"装备{i}",
    i % 2 == 0, "兵装库·身体", i, 30000, "测试套装", i <= 6)).ToArray();
Check(OutfitDisplayFilter.Apply(thirtySix, false, "").Count == 36, "Gearset filter off retains all 36 entries");
Check(OutfitDisplayFilter.Apply(thirtySix, true, "").Count == 30, "Gearset filter on hides exactly six registered entries");
Check(OutfitDisplayFilter.Apply(thirtySix, true, "装备1").All(item => item.RegisteredToGearset != true
    && item.Name.Contains("装备1")), "Search and gearset filter apply together");
var copies = new[] { new OutfitEquipment(10, "重复副本", false, "物品栏 1", 1, 30000, "测试套装", true),
    new OutfitEquipment(10, "重复副本", true, "物品栏 2", 1, 30000, "测试套装", false) };
Check(OutfitDisplayFilter.Apply(copies, true, "").Single().HighQuality, "Filter uses per-copy registration instead of item-id membership");
Check(OutfitDisplayFilter.Apply([copies[0]], true, "").Count == 0, "Fully filtered inventory is empty");
Check(OutfitDisplayFilter.Apply([copies[1] with { RegisteredToGearset = null }], true, "").Count == 0,
    "Pending registration cannot leak unfiltered equipment into a hidden-gearset list");
var setA = new OutfitDefinition(100, "套装甲", new uint[] { 10, 20 });
var setB = new OutfitDefinition(200, "套装乙", new uint[] { 10, 30 });
var partial = new StoredOutfit(100, new HashSet<uint> { 10 });
Check(OutfitStateEvaluator.Evaluate(10, [setA], [partial]).State == OutfitStorageState.Saved,
    "Saved piece stays saved even when other pieces in that outfit are missing");
Check(OutfitStateEvaluator.Evaluate(20, [setA], [partial]).State == OutfitStorageState.CanComplete,
    "Missing piece in existing outfit is completable");
Check(OutfitStateEvaluator.Evaluate(10, [setA], []).State == OutfitStorageState.NotCreated,
    "Loaded empty outfit collection means outfit not created");
Check(OutfitStateEvaluator.Evaluate(10, [setA], null).State == OutfitStorageState.Unknown,
    "Unloaded collection must never be treated as not created");
Check(OutfitStateEvaluator.Evaluate(10, null, []).State == OutfitStorageState.Unknown,
    "Missing set definitions stay unknown");
Check(OutfitStateEvaluator.Evaluate(10, [setB], [partial]).State == OutfitStorageState.NotCreated,
    "An unrelated existing outfit does not mark this outfit as created");
var mixed = OutfitStateEvaluator.Evaluate(10, [setA, setB], [partial, new(200, new HashSet<uint> { 30 })]);
Check(mixed.State == OutfitStorageState.CanComplete && mixed.Details.Contains("套装甲：已保存")
    && mixed.Details.Contains("套装乙：可补齐"), "Multiple memberships retain per-outfit states and prioritize completable");
Check(OutfitStateEvaluator.Evaluate(10, [setA], [partial, new(100, new HashSet<uint> { 20 })]).State
    == OutfitStorageState.CanComplete, "Duplicate outfits preserve missing-slot opportunity");
Check(OutfitStateEvaluator.Evaluate(10, [setA], [new(100, new HashSet<uint> { 10, 20 })]).State
    == OutfitStorageState.Saved, "Fully stored outfit reports saved");
Check(OutfitStateEvaluator.Evaluate(10, [setA], [partial]).State == OutfitStorageState.Saved
    && OutfitStateEvaluator.Evaluate(10, [setA], []).State == OutfitStorageState.NotCreated,
    "Refresh after restoring outfit removes old saved state");
var partFixture = EquipmentParts.Order.Reverse().Select((part, i) => new InventoryEntry((uint)i + 1000, false, "物品栏", i, 30000)).ToArray();
var partNames = partFixture.ToDictionary(entry => entry.ItemId, entry => new OutfitInfo("装备" + entry.ItemId, "套装", Part: EquipmentParts.Order.Reverse().ElementAt(entry.Slot)));
Check(OutfitListBuilder.Build(partFixture, id => partNames.GetValueOrDefault(id)).Select(item => item.Part).SequenceEqual(EquipmentParts.Order),
    "Equipment order is main/off/head/body/hands/legs/feet/ears/neck/wrists/rings");
Check(EquipmentParts.Group("头部") == EquipmentParts.Group("手部") && EquipmentParts.Group("耳部") == EquipmentParts.Group("颈部"),
    "Part groups match game category order");
var highlightFixture = new[] { copies[0] with { Name = "可补齐装备", Assessment = new(OutfitStorageState.CanComplete, "") },
    copies[0] with { Name = "已保存装备", Assessment = new(OutfitStorageState.Saved, "") },
    copies[0] with { Name = "未建立装备", Assessment = new(OutfitStorageState.NotCreated, "") },
    copies[0] with { Name = "待确认装备", Assessment = new(OutfitStorageState.Unknown, "") } };
var targets = NativeNameHighlightPolicy.Targets(highlightFixture);
Check(targets.SetEquals(["可补齐装备"]), "Native highlighting excludes saved, uncreated and unknown items");
Check(NativeNameHighlightPolicy.Match("可补齐装备", targets) != null, "Exact native equipment label matches");
Check(NativeNameHighlightPolicy.Match("可补齐装备★", targets) != null, "Formatting symbol suffix does not prevent highlight");
Check(NativeNameHighlightPolicy.Match("可补齐装备改良型", targets) == null, "Prefix sharing does not highlight another equipment name");
Check(NativeNameHighlightPolicy.Targets([highlightFixture[0], highlightFixture[1] with { Name = "可补齐装备" }]).Count == 0,
    "Ambiguous equal names with different states are not painted");
Check(NativeNameHighlightPolicy.Targets([]).Count == 0, "Cleared snapshot has no native highlight targets");
var sourceKey = new CompletionItemKey(10, 0, 1, false);
var otherKey = new CompletionItemKey(20, 3200, 5, false);
IReadOnlySet<CompletionItemKey> batch = new HashSet<CompletionItemKey> { sourceKey, otherKey };
Check(OutfitCompletionPolicy.IsMergePrompt("发现了缺少此装备的现有套装，\n要将此装备并入其中吗？"), "Only the screenshot's existing-outfit merge prompt is accepted");
Check(!OutfitCompletionPolicy.IsMergePrompt("要将此装备放入投影台吗？"), "Ordinary item storage confirmation is not auto-accepted");
Check(!OutfitCompletionPolicy.IsMergePrompt("要删除此装备吗？"), "Unrelated destructive confirmation is rejected");
Check(OutfitCompletionPolicy.ValidateHandIns(batch, [sourceKey], sourceKey, 1) is null, "One allowed missing piece with sufficient prisms can be submitted");
Check(OutfitCompletionPolicy.ValidateHandIns(batch, [sourceKey, otherKey], sourceKey, 2) is null, "Native auto-filled pieces are allowed only when both belong to the batch");
Check(OutfitCompletionPolicy.ValidateHandIns(batch, [], sourceKey, 2) is not null, "Empty native hand-in never submits");
Check(OutfitCompletionPolicy.ValidateHandIns(batch, [otherKey], sourceKey, 2) is not null, "Native window must still contain the source equipment");
Check(OutfitCompletionPolicy.ValidateHandIns(batch, [sourceKey, sourceKey], sourceKey, 2) is not null, "Duplicate source slot is rejected");
Check(OutfitCompletionPolicy.ValidateHandIns(batch, [sourceKey, new(30, 0, 9, false)], sourceKey, 2) is not null, "Native auto-fill cannot consume filtered or unrelated equipment");
Check(OutfitCompletionPolicy.ValidateHandIns(batch, [sourceKey with { HighQuality = true }], sourceKey, 2) is not null, "Replacing an NQ copy with HQ invalidates the source identity");
Check(OutfitCompletionPolicy.ValidateHandIns(batch, [sourceKey, otherKey], sourceKey, 1) is not null, "Insufficient prisms block the whole native hand-in");
Check(OutfitCompletionPolicy.ValidateHandIns(batch, [sourceKey, otherKey with { Slot = 6 }], sourceKey, 2) is not null, "Moving a piece to a different slot invalidates batch authorization");
Check(!OutfitCompletionPolicy.IsMergePrompt("", ""), "Missing native prompt data cannot authorize confirmation");
Check(!OutfitCompletionPolicy.IsMergePrompt("要删除此装备吗？", "发现了缺少此装备的现有套装，要将此装备并入其中吗？"),
    "Live prompt comparison still rejects a foreign confirmation");
Check(!OutfitCompletionPolicy.CanConfirmFinalStorage(true, true, false), "Final Yes cannot be clicked before outfit-glamour checkbox is checked");
Check(!OutfitCompletionPolicy.CanConfirmFinalStorage(false, true, true), "Checked foreign final dialog is never accepted");
Check(!OutfitCompletionPolicy.CanConfirmFinalStorage(true, false, true), "Final dialog needs a validated hand-in batch");
Check(OutfitCompletionPolicy.CanConfirmFinalStorage(true, true, true), "Owned validated checked outfit can reach final Yes");
IReadOnlySet<uint> savedGearsetIds = new HashSet<uint> { 10, 1_000_020 };
Check(GearsetRegistration.IsRegistered(savedGearsetIds, 10, false), "Saved NQ gearset identity is hidden without an item-row lookup");
Check(!GearsetRegistration.IsRegistered(savedGearsetIds, 10, true), "NQ membership does not accidentally hide an unregistered HQ copy");
Check(GearsetRegistration.IsRegistered(savedGearsetIds, 20, true), "HQ gearset identity includes the game's HQ offset");
Check(!GearsetRegistration.IsRegistered(savedGearsetIds, 20, false), "HQ membership does not hide an unregistered NQ copy");
Check(OutfitDisplayFilter.Apply([copies[1] with { RegisteredToGearset = null }], false, "").Count == 1,
    "Turning off the filter still displays pending gearset identities");
var gearsetGate = new GearsetSnapshotGate();
Check(!gearsetGate.IsStable(savedGearsetIds), "First registration sample waits before publishing an initial list");
Check(gearsetGate.IsStable(savedGearsetIds), "A second consistent registration sample can publish the filtered list");
Check(!gearsetGate.IsStable(new HashSet<uint>()), "A transient empty registration sample cannot publish an unfiltered list");
Check(!gearsetGate.IsStable(savedGearsetIds) && gearsetGate.IsStable(savedGearsetIds), "Transient refresh recovers directly to the stable filtered snapshot");
gearsetGate.Clear();
Check(!gearsetGate.IsStable(savedGearsetIds), "Character/session reset cannot reuse old registration readiness");
var completable = new OutfitAssessment(OutfitStorageState.CanComplete, "existing outfit");
Check(CabinetPolicy.Assess(completable, 7, false, false).State == OutfitStorageState.CanStoreCabinet,
    "Uncollected cabinet piece takes priority over outfit completion");
Check(CabinetPolicy.Assess(new(OutfitStorageState.Saved, ""), 7, false, false).State == OutfitStorageState.CanStoreCabinet,
    "Cabinet eligibility is independent from already saved outfit pieces");
Check(CabinetPolicy.Assess(completable, 7, true, false) == completable, "Collected cabinet piece keeps its outfit status");
Check(CabinetPolicy.Assess(completable, null, false, false) == completable, "Ordinary outfit piece is not a cabinet candidate");
Check(CabinetPolicy.Assess(completable, 7, null, false).State == OutfitStorageState.Unknown,
    "Unloaded cabinet data cannot authorize outfit consumption");
Check(CabinetPolicy.Assess(completable, 7, false, true) == completable,
    "Registered job gear is never offered for cabinet storage");
Check(CabinetPolicy.Assess(completable, 7, false, null) == completable,
    "Unknown registration is never offered for cabinet storage");
Check(CabinetPolicy.CanStore(7, false, false, 1), "One unprotected uncollected copy can be submitted");
Check(!CabinetPolicy.CanStore(7, true, false, 1), "Already collected piece cannot be stored again");
Check(!CabinetPolicy.CanStore(7, null, false, 1), "Unknown cabinet collection blocks storage");
Check(!CabinetPolicy.CanStore(7, false, true, 1), "Job gear protection holds even with display filter disabled");
Check(!CabinetPolicy.CanStore(7, false, null, 1), "Pending job gear registration blocks storage");
Check(!CabinetPolicy.CanStore(7, false, false, 2), "Duplicate copies cannot be chosen by an ambiguous cabinet row request");
Check(!CabinetPolicy.CanStore(7, false, false, 0), "Removed source copy cannot be submitted");
Check(!CabinetPolicy.CanStore(null, false, false, 1), "Missing cabinet row cannot authorize storage");
Check(CabinetPolicy.CanStore(0, false, false, 1), "Cabinet collection row zero is a valid storage target");
var cabinetHead = new OutfitEquipment(10, "测试头部", false, "兵装库·头部", 1, 30000, "测试套装",
    false, new(OutfitStorageState.CanStoreCabinet, ""), CabinetId: 0, InCabinet: false);
var cabinetFeet = cabinetHead with { ItemId = 20, Location = "兵装库·脚部", CabinetId = 7 };
Check(CabinetPolicy.Candidates([cabinetHead, cabinetFeet]).Count == 2,
    "Cabinet candidates span armory categories without depending on the visible inventory page");
Check(CabinetPolicy.Candidates([cabinetHead with { RegisteredToGearset = true }, cabinetFeet]).SequenceEqual([cabinetFeet]),
    "Cross-category cabinet selection still excludes registered gear");
Check(CabinetPolicy.Candidates([cabinetHead with { InCabinet = true }, cabinetFeet with { InCabinet = null }]).Count == 0,
    "Collected and unknown cabinet records are never submitted from stale candidate labels");
Check(CabinetPolicy.Candidates([cabinetHead with { CabinetId = null }]).Count == 0,
    "Candidate selection requires an actual Cabinet row mapping");
// Quality identity and normalization use the user's NQ shoe / HQ partial-outfit case.
var sleepSet = new OutfitDefinition(45518, "朴素寝室套装", [0,0,40404,40405,0,40406,40407,0,0,0,0]);
var sleepHq = new StoredOutfit(45518, new HashSet<uint> {40406}, true, 210);
Check(OutfitStateEvaluator.Evaluate(40407, [sleepSet], [sleepHq]).State == OutfitStorageState.QualityConflict,
    "NQ shoes cannot fill an HQ outfit missing those shoes");
Check(OutfitStateEvaluator.Evaluate(40407, [sleepSet], [sleepHq], true).State == OutfitStorageState.CanComplete,
    "Matching HQ shoes can fill the existing HQ outfit");
Check(OutfitStateEvaluator.Evaluate(40406, [sleepSet], [sleepHq]).State == OutfitStorageState.Saved,
    "Appearance already in an opposite-quality outfit remains saved");
Check(OutfitStateEvaluator.Evaluate(40407, [sleepSet], [sleepHq with { HighQuality = false }], true).State == OutfitStorageState.QualityConflict,
    "HQ-to-NQ mismatch is also excluded from normal auto completion");
Check(OutfitStateEvaluator.Evaluate(40407, [sleepSet,new OutfitDefinition(999,"其他套装",[40407])],
    [sleepHq,new StoredOutfit(999,new HashSet<uint>{40407})]).State == OutfitStorageState.QualityConflict,
    "An unresolved quality conflict remains visible even if another outfit already has the appearance");
var nqShoe = new OutfitEquipment(40407, "朴素寝室拖鞋", false, "兵装库·脚部", 3, 30000, sleepSet.Name,
    false, new(OutfitStorageState.QualityConflict, ""));
OutfitInfo? SleepFind(uint id) => sleepSet.Pieces.Contains(id) ? new("寝室装备",sleepSet.Name,[sleepSet]) : null;
Check(OutfitRestorePolicy.MatchesSource(1045518,210,1045518,210,true), "Native restore source matches the observed HQ outfit row");
Check(!OutfitRestorePolicy.MatchesSource(1045518,210,45518,210,true), "Native restore rejects the NQ copy");
Check(!OutfitRestorePolicy.MatchesSource(1045518,210,1045518,211,true), "Native restore rejects another dresser slot");
Check(!OutfitRestorePolicy.MatchesSource(1045518,210,1045518,210,false), "Native restore rejects a piece renderer in place of a full outfit row");
Check(!OutfitRestorePolicy.MatchesSource(1045518,800,1045518,800,true), "Native restore rejects an out-of-range source index");
const string restorePrompt = "确定要将所选幻影变回道具吗？";
bool RestoreConfirm(bool owned=true,ushort mask=32,uint selected=32,uint raw=1045518,uint shown=1045518,
    uint excluded=0xFFFFFFDF,bool valid=true,string prompt=restorePrompt)
    => OutfitRestorePolicy.CanConfirm(owned,mask,selected,raw,shown,excluded,valid,prompt);
Check(RestoreConfirm(), "Native restore confirmation accepts the observed CN dialog snapshot");
Check(!RestoreConfirm(owned:false), "Native restore rejects another handler's confirmation");
Check(!RestoreConfirm(mask:0,selected:0,excluded:uint.MaxValue), "Native restore never confirms an empty selection");
Check(!RestoreConfirm(mask:2048,selected:2048,excluded:0xFFFFF7FF), "Native restore rejects bits outside eleven outfit columns");
Check(!RestoreConfirm(selected:64), "Native restore rejects a different selected part");
Check(!RestoreConfirm(selected:96), "Native restore rejects additional unplanned parts");
Check(!RestoreConfirm(shown:1045519), "Native restore rejects another outfit in the final window");
Check(!RestoreConfirm(raw:45518,shown:45518), "Native restore rejects NQ input to the HQ lowering workflow");
Check(!RestoreConfirm(excluded:0xFFDF), "Final native excluded mask requires the full 32-bit complement");
Check(!RestoreConfirm(valid:false), "Native restore rejects changed source context");
Check(!RestoreConfirm(prompt:"确定要降低品质吗？"), "Native restore rejects unrelated confirmation text");
var normalize = QualityNormalizationPlan.First([nqShoe], [sleepHq], SleepFind);
Check(normalize != null && normalize.RawStoredId == 1045518, "Normalization preserves the exact HQ storage identity");
Check(normalize!.RestoreMask == 1 << 5, "Only the saved pants slot is restored; empty slots are not requested");
Check(normalize.PrismCount == 2 && normalize.Additional.Single() == nqShoe,
    "Preview includes restored HQ pants and the authorized NQ shoes, costing two prisms");
Check(QualityNormalizationPlan.First([nqShoe with { RegisteredToGearset = true }], [sleepHq], SleepFind) == null,
    "Registered NQ gear cannot enter a quality normalization plan");
Check(QualityNormalizationPlan.First([nqShoe with { RegisteredToGearset = null }], [sleepHq], SleepFind) == null,
    "Pending registration cannot enter a normalization plan");
Check(QualityNormalizationPlan.First([nqShoe with { Condition = 29999 }], [sleepHq], SleepFind) == null,
    "Damaged NQ input does not cause HQ gear to be taken out");
Check(QualityNormalizationPlan.First([nqShoe], [sleepHq, sleepHq with { HighQuality = false }], SleepFind) == null,
    "Mixed or duplicate outfit records require manual review");
Check(QualityNormalizationPlan.First([nqShoe], [sleepHq with { BoxIndex = -1 }], SleepFind) == null,
    "Missing source dresser index cannot authorize restoration");
Check(QualityNormalizationPlan.First([nqShoe], [sleepHq with { Pieces = new HashSet<uint>() }], SleepFind) == null,
    "Empty saved outfits cannot create a restore request");
Check(QualityNormalizationPlan.First([nqShoe,nqShoe with { Slot = 5 }], [sleepHq], SleepFind)!.Additional.Count == 1,
    "Only one exact physical NQ source is planned per outfit slot");
Check(QualityNormalizationPlan.First([nqShoe], [sleepHq with { BoxIndex = 800 }], SleepFind) == null,
    "Out-of-range dresser source cannot create a restore request");
Check(QualityNormalizationPlan.First([nqShoe], [sleepHq with { Pieces = new HashSet<uint>{99999} }], SleepFind) == null,
    "Unmapped saved pieces cannot be taken out by a partial restore mask");
var lowerKey = new CompletionItemKey(40406, 0, 10, true);
const string lowerPrompt = "确定要降低朴素寝室打底裤的品质变换成以下道具吗？";
bool Lower(bool owned=true,uint addon=129,uint blocker=129,uint container=0,int slot=10,bool valid=true,bool protect=false,string prompt=lowerPrompt,string name="朴素寝室打底裤",ulong eventKind=77)
    => QualityNormalizationPolicy.CanConfirmLower(owned,addon,blocker,lowerKey,container,slot,valid,protect,prompt,name,eventKind);
Check(Lower(), "Owned native lower-quality prompt matches blocker, item name and physical slot");
Check(!Lower(owned:false), "Foreign callback handler blocks irreversible quality confirmation");
Check(!Lower(blocker:130) && !Lower(addon:0), "Only the exact blocking addon can lower quality");
Check(!Lower(container:1) && !Lower(slot:11), "Moved or different source slots cannot be lowered");
Check(!Lower(valid:false) && !Lower(protect:true), "Changed or newly protected source blocks quality lowering");
Check(!Lower(prompt:"要丢弃朴素寝室打底裤吗？"), "Discard prompt can never be accepted as quality lowering");
Check(!Lower(name:"另一件装备") && !Lower(name:""), "Wrong or absent item name cannot authorize lower quality");
Check(!QualityNormalizationPolicy.CanConfirmLower(true,129,129,lowerKey with {HighQuality=false},0,10,true,false,lowerPrompt,"朴素寝室打底裤",77),
    "An NQ source is never submitted to irreversible HQ lowering");

const string nativeLowerPrompt = "确定要降低朴素寝室打底裤\uE03C的品质变换成以下道具\n吗？";
Check(Lower(prompt:nativeLowerPrompt), "Observed CN quality prompt accepts the native newline and HQ glyph");
Check(Lower(prompt:nativeLowerPrompt.Replace("\n", "\r\n")), "Windows newline formatting does not change quality confirmation identity");
Check(!Lower(prompt:nativeLowerPrompt.Replace("朴素寝室打底裤", "另一件装备")), "Formatted prompt still rejects another item");
Check(!Lower(prompt:nativeLowerPrompt.Replace("确定要降低", "确定要丢弃")), "Formatted discard prompt cannot authorize lowering");
Check(!Lower(eventKind:76), "Only the observed InventoryContext lower-quality callback event can be accepted");
Check(!QualityNormalizationPolicy.CanClickLowerYes(true,true,false), "Quality cannot be lowered before the acknowledgment is checked");
Check(!QualityNormalizationPolicy.CanClickLowerYes(true,false,true), "Missing acknowledgment control blocks quality lowering");
Check(!QualityNormalizationPolicy.CanClickLowerYes(false,true,true), "A checked acknowledgment cannot authorize a mismatched item");
Check(QualityNormalizationPolicy.CanClickLowerYes(true,true,true), "Validated and checked lower-quality confirmation may proceed");

var newShoe = nqShoe with { Assessment = new(OutfitStorageState.NotCreated, "") };
var newPants = newShoe with { ItemId = 40406, Name = "朴素寝室打底裤", Location = "物品栏 1", Slot = 1 };
var newPlans = NewOutfitPlan.Build([newShoe], SleepFind);
Check(newPlans.Count == 1 && newPlans[0].Items.Count == 1 && newPlans[0].RawId == 45518,
    "A single available piece can establish a partial outfit");
Check(NewOutfitPlan.Build([newShoe,newPants],SleepFind).Single().Items.Count == 2,
    "Same-quality pieces across inventory and armory are submitted together");
Check(NewOutfitPlan.Build([newShoe with {HighQuality=true},newPants with {HighQuality=true}],SleepFind).Single().RawId == 1045518,
    "HQ outfit creation preserves HQ instead of silently lowering quality");
var mixedPlans = NewOutfitPlan.Build([newShoe with {HighQuality=true},newPants],SleepFind);
Check(mixedPlans.Count == 1 && !mixedPlans[0].HighQuality && mixedPlans[0].Items.Single() == newPants,
    "One outfit never mixes HQ and NQ or schedules duplicate qualities; NQ is preferred");
Check(NewOutfitPlan.Build([newShoe,newShoe with {Slot=8}],SleepFind).Single().Items.Count == 1,
    "Only one physical copy is selected for each outfit slot");
Check(NewOutfitPlan.Build([newShoe with {Condition=29999}],SleepFind).Count == 0,
    "Damaged gear is excluded before new outfit creation");
Check(NewOutfitPlan.Build([newShoe with {RegisteredToGearset=null}],SleepFind).Count == 0,
    "Unknown gearset registration cannot authorize outfit creation");
Check(NewOutfitPlan.Build(OutfitDisplayFilter.Apply([newShoe with {RegisteredToGearset=true}],true,""),SleepFind).Count == 0,
    "New outfit creation honors the same saved-gearset display filter");
Check(NewOutfitPlan.Build(OutfitDisplayFilter.Apply([newShoe with {RegisteredToGearset=true}],false,""),SleepFind).Count == 1,
    "Turning off the display filter intentionally exposes registered creation candidates");
foreach (var state in new[]{OutfitStorageState.Saved,OutfitStorageState.CanComplete,OutfitStorageState.QualityConflict,OutfitStorageState.CanStoreCabinet,OutfitStorageState.Unknown})
    Check(NewOutfitPlan.Build([newShoe with {Assessment=new(state,"")}],SleepFind).Count==0,
        $"{state} cannot be consumed by the create-outfit button");
Check(NewOutfitPlan.Build([newShoe],_=>null).Count==0, "Unmapped items cannot create an outfit");
Check(NewOutfitPlan.Build([newShoe],_=>new("bad","bad",[new(1,"bad",[40407])])).Count==0,
    "Malformed slot mappings cannot create a native submission");
var sharedSet = new OutfitDefinition(45519,"另一套装",[0,0,0,40405,0,0,40407,0,0,0,0]);
OutfitInfo? SharedFind(uint id) => new("shared","sets",[sleepSet,sharedSet]);
var splitPlans = NewOutfitPlan.Build([newShoe,newPants,newShoe with {ItemId=40405,Slot=9}],SharedFind);
Check(splitPlans.SelectMany(plan=>plan.Items).Distinct().Count()==splitPlans.Sum(plan=>plan.Items.Count),
    "Overlapping outfit memberships never reuse a physical source across planned sets");
var shoeSource = new CompletionItemKey(40407,3205,2,false);
var pantsSource = new CompletionItemKey(40406,0,3,false);
var compactShoe = OutfitSubmissionLayout.Pack(sleepSet.Pieces,[shoeSource],false);
Check(compactShoe.Length==1 && compactShoe[0]==shoeSource,
    "A shoe-only outfit starts with the shoe instead of empty main-hand/head/body entries");
var compactSleep = OutfitSubmissionLayout.Pack(sleepSet.Pieces,[shoeSource,pantsSource],false);
Check(compactSleep.SequenceEqual([pantsSource,shoeSource]),
    "Partial sleepwear restoration packs pants then shoes without sheet-column holes");
Check(OutfitSubmissionLayout.Pack(sleepSet.Pieces,[shoeSource with {HighQuality=true}],true).Single().HighQuality,
    "HQ native submission preserves source quality and uses the plan's HQ raw outfit ID");
bool PackRejected(IReadOnlyList<uint> columns,IReadOnlyList<CompletionItemKey> keys,bool hq=false)
{
    try { OutfitSubmissionLayout.Pack(columns,keys,hq); return false; }
    catch(InvalidOperationException) { return true; }
}
Check(PackRejected(sleepSet.Pieces,[]),"An empty native outfit request is never sent");
Check(PackRejected(sleepSet.Pieces,[shoeSource,shoeSource]),"Duplicate packed sources are rejected");
Check(PackRejected(sleepSet.Pieces,[shoeSource,pantsSource with {Container=shoeSource.Container,Slot=shoeSource.Slot}]),
    "Two packed entries cannot reference the same physical slot");
Check(PackRejected(sleepSet.Pieces,[shoeSource with {HighQuality=true}]),"Mixed or unexpected quality is rejected before submission");
Check(PackRejected(sleepSet.Pieces,[shoeSource with {Container=9999}]),"Invalid inventory containers cannot terminate a packed request early");
Check(PackRejected(sleepSet.Pieces,[shoeSource with {ItemId=0}]),"Empty sheet columns cannot masquerade as submitted gear");
Check(PackRejected(sleepSet.Pieces,[shoeSource with {ItemId=99999}]),"Unrelated items cannot enter a packed outfit request");
Check(PackRejected([40407],[shoeSource]),"Malformed sheet column count is rejected before native packing");
var nineColumns = new uint[]{1,2,3,4,5,6,7,8,9,0,0};
var nineSources = Enumerable.Range(1,9).Reverse().Select(i=>new CompletionItemKey((uint)i,0,(ushort)i,false)).ToArray();
Check(OutfitSubmissionLayout.Pack(nineColumns,nineSources,false).Select(key=>key.ItemId).SequenceEqual(Enumerable.Range(1,9).Select(i=>(uint)i)),
    "The native nine-entry limit is accepted and sorted into sheet order");
Check(PackRejected(Enumerable.Range(1,11).Select(i=>(uint)i).ToArray(),Enumerable.Range(1,10).Select(i=>new CompletionItemKey((uint)i,0,(ushort)i,false)).ToArray()),
    "More than nine submitted sources cannot overflow the native entry limit");
var oversizedDefinition = new OutfitDefinition(555,"超出原生容量",Enumerable.Range(1,11).Select(i=>(uint)i).ToArray());
var oversizedStored = new StoredOutfit(555,Enumerable.Range(1,9).Select(i=>(uint)i).ToHashSet(),true,1);
var oversizedExtras = new uint[]{10,11}.Select(id=>newShoe with {ItemId=id,Assessment=new(OutfitStorageState.QualityConflict,"")}).ToArray();
Check(QualityNormalizationPlan.First(oversizedExtras,[oversizedStored],_=>new("oversized","set",[oversizedDefinition]))==null,
    "Normalization cannot take out or lower HQ gear when the final native request would exceed nine sources");
Check(QualityNormalizationPolicy.CanLowerOutside(false,false,false), "Quality lowering is allowed only after closing the dresser and leaving its event");
Check(!QualityNormalizationPolicy.CanLowerOutside(true,false,false), "A visible dresser prevents quality lowering");
Check(!QualityNormalizationPolicy.CanLowerOutside(false,false,true), "A closed dresser still blocks lowering until the player is no longer occupied");
Check(!QualityNormalizationPolicy.CanLowerOutside(false,true,false), "Opening a cabinet never permits quality lowering");
Check(QualityNormalizationPolicy.SameDresser(123,456,789,123,456,789,2), "Reopening resolves the captured dresser in the same territory and range");
Check(!QualityNormalizationPolicy.SameDresser(123,456,789,124,456,789,2), "An identical nearby dresser cannot replace the original object");
Check(!QualityNormalizationPolicy.SameDresser(123,456,789,123,457,789,2), "A recycled object ID with different base data is rejected");
Check(!QualityNormalizationPolicy.SameDresser(123,456,789,123,456,790,2), "Changing territory invalidates the original dresser");
Check(!QualityNormalizationPolicy.SameDresser(123,456,789,123,456,789,7), "Walking away stops reopening instead of moving the player");
Check(!QualityNormalizationPolicy.SameDresser(123,456,789,123,456,789,float.NaN), "Unknown dresser distance cannot authorize an interaction");
Check(!QualityNormalizationPolicy.SameDresser(0,456,789,0,456,789,2), "A missing dresser ID cannot authorize an interaction");
foreach(var step in Enum.GetValues<QualityNormalizationPhase>())
{
    var expectedClose = step is QualityNormalizationPhase.WaitSelectionClosed or QualityNormalizationPhase.WaitDresserClosed;
    var expectedOpen = step == QualityNormalizationPhase.WaitDresserOpen;
    Check(QualityNormalizationPolicy.AcceptsContextTransition(step,false,false)==expectedClose
        && QualityNormalizationPolicy.AcceptsContextTransition(step,true,false)==expectedOpen
        && !QualityNormalizationPolicy.AcceptsContextTransition(step,true,true)
        && !QualityNormalizationPolicy.AcceptsContextTransition(step,false,true),
        $"Only explicitly awaited dresser transitions keep the quality job running: {step}");
}
Check(!QualityNormalizationPolicy.AllowsClosedDresser(QualityNormalizationPhase.Store)
    && !QualityNormalizationPolicy.AllowsClosedDresser(QualityNormalizationPhase.Restore),
    "Taking out and storing outfits require an open dresser");
Check(QualityNormalizationPolicy.AllowsClosedDresser(QualityNormalizationPhase.Lower)
    && QualityNormalizationPolicy.AllowsClosedDresser(QualityNormalizationPhase.LowerConfirm)
    && QualityNormalizationPolicy.AllowsClosedDresser(QualityNormalizationPhase.LowerWait)
    && QualityNormalizationPolicy.AllowsClosedDresser(QualityNormalizationPhase.WaitDresserOpen),
    "Closing the dresser keeps the verified lowering and reopening stages alive");
var silentSet = new OutfitDefinition(60000,"静默测试套装",[0,0,101,102,103,0,0,0,0,0,0]);
OutfitInfo? SilentFind(uint id) => new("测试装备","静默测试套装",[silentSet]);
var silentHead = new CompletionItemKey(101,0,1,false);
var silentBody = new CompletionItemKey(102,3201,2,false);
var silentHands = new CompletionItemKey(103,3202,3,false);
var silentSaved = new StoredOutfit(60000,new HashSet<uint>{103},false,27);
var silentPlan = ExistingOutfitCompletionPlan.First([silentBody,silentHead,silentHands],[silentSaved],SilentFind);
Check(silentPlan?.Stored.BoxIndex==27 && silentPlan.RawId==60000, "Silent completion uses an existing dresser slot, not the outfit row ID");
Check(silentPlan!.Sources.SequenceEqual([silentHead,silentBody]), "Missing pieces from one outfit are packed into a single request in sheet order");
Check(!silentPlan.Sources.Contains(silentHands), "Silent completion never resubmits an already saved piece");
Check(ExistingOutfitCompletionPlan.First([silentHead with {HighQuality=true}],[silentSaved],SilentFind)==null,
    "Opposite-quality gear cannot enter a silent existing-outfit request");
var silentHq = ExistingOutfitCompletionPlan.First([silentHead with {HighQuality=true}],
    [silentSaved with {HighQuality=true}],SilentFind);
Check(silentHq?.RawId==1_060_000 && silentHq.Sources.Single().HighQuality,
    "Silent HQ completion preserves quality and exact dresser slot identity");
Check(ExistingOutfitCompletionPlan.First([silentHead],[],SilentFind)==null,
    "Silent completion cannot create an outfit when no matching saved outfit exists");
Check(ExistingOutfitCompletionPlan.First([silentHead],[silentSaved with {BoxIndex=-1}],SilentFind)==null
    && ExistingOutfitCompletionPlan.First([silentHead],[silentSaved with {BoxIndex=800}],SilentFind)==null,
    "Unknown or out-of-bounds dresser slots are rejected");
Check(ExistingOutfitCompletionPlan.First([silentHead],[silentSaved with {Pieces=new HashSet<uint>{101,102,103}}],SilentFind)==null,
    "A complete outfit cannot produce a new request");
Check(ExistingOutfitCompletionPlan.First([silentHead,silentHead with {Slot=4}],[silentSaved],SilentFind)!.Sources.Count==1,
    "Only one physical copy of each missing piece is used in one batch");
Check(ExistingOutfitCompletionPlan.First([silentHead,silentBody with {Container=0,Slot=1}],[silentSaved],SilentFind)!.Sources.Count==1,
    "One physical slot cannot be selected as two different items");
Check(ExistingOutfitCompletionPlan.First([silentHead with {Container=9999}],[silentSaved],SilentFind)==null,
    "An invalid source cannot produce a silent request");
Check(ExistingOutfitCompletionPlan.First([new(999,0,1,false)],[silentSaved],SilentFind)==null,
    "Unmapped equipment cannot enter a silent batch");
Check(ExistingOutfitCompletionPlan.First([silentHead],[silentSaved],_=>null)==null,
    "Missing catalog data does not guess a target outfit");
Check(ExistingOutfitCompletionPlan.First([silentHead],[silentSaved],_=>new("bad","bad",[silentSet with {Pieces=[101]}]))==null,
    "Malformed sheet columns cannot be submitted silently");
var alternativeSet = silentSet with {Id=60001,Name="另一套装"};
var alternativeSaved = silentSaved with {Id=60001,BoxIndex=28};
var sharedSilent = ExistingOutfitCompletionPlan.First([silentHead],[alternativeSaved],
    _=>new("shared","shared",[silentSet,alternativeSet]));
Check(sharedSilent?.Stored.BoxIndex==28 && sharedSilent.Definition.Id==60001,
    "Shared equipment resolves an actually stored compatible outfit");
var sourceSnapshot = new HashSet<CompletionItemKey>{silentHead};
var snapshotPlan = ExistingOutfitCompletionPlan.First(sourceSnapshot.ToArray(),[silentSaved],SilentFind);
Check(snapshotPlan!.Sources.SequenceEqual([silentHead]), "Equipment outside the clicked batch is never added to a request");
Check(!ExistingOutfitCompletionPlan.IsAcknowledged(silentPlan.Sources,_=>false,_=>false,true),
    "Removing inventory sources alone cannot prove storage success");
Check(!ExistingOutfitCompletionPlan.IsAcknowledged(silentPlan.Sources,_=>true,_=>true,true),
    "Unlocking an outfit without removing the submitted sources cannot prove success");
Check(!ExistingOutfitCompletionPlan.IsAcknowledged(silentPlan.Sources,_=>true,key=>key==silentBody,true),
    "A partially applied multi-piece batch stays pending");
Check(ExistingOutfitCompletionPlan.IsAcknowledged(silentPlan.Sources,_=>true,_=>false,true),
    "Both saved unlocks and removed sources acknowledge the batch before advancing");
Check(!ExistingOutfitCompletionPlan.IsAcknowledged(silentPlan.Sources,_=>true,_=>false,false),
    "Unloaded inventories are not interpreted as removed sources");
Check(!ExistingOutfitCompletionPlan.IsAcknowledged([],_=>true,_=>false,true),
    "An empty batch cannot be acknowledged as success");
var deliveryLow = new ExpertDeliveryCandidate(new(100,0,1,false),"低品级装备",180,1035,"物品栏 1");
var deliveryHigh = deliveryLow with { Key=new(101,3201,2,false),Name="高品级装备",ItemLevel=600,SealReward=1870 };
var deliveryMid = deliveryLow with { Key=new(102,0,3,false),Name="中品级装备",ItemLevel=500,SealReward=1726 };
var deliveryPlan = ExpertDeliveryPreviewPlan.Build([deliveryHigh,deliveryMid,deliveryLow],600,71385,90000);
Check(deliveryPlan.Items.SequenceEqual([deliveryLow,deliveryMid]), "Delivery demo sorts by ascending item level and excludes the exact stopping level");
Check(deliveryPlan.SealsAfter==71385+1035+1726 && deliveryPlan.StopReason.Contains("600"), "Delivery demo reports projected seals and the item-level stopping reason");
Check(ExpertDeliveryPreviewPlan.Build([deliveryLow with {Protected=true},deliveryMid],600,0,90000).Items.SequenceEqual([deliveryMid]),
    "Protected gear is excluded even if it has the lowest item level");
Check(ExpertDeliveryPreviewPlan.Build([deliveryLow],180,0,90000).Items.Count==0,
    "The item at the specified item level is never included");
Check(ExpertDeliveryPreviewPlan.Build([deliveryLow],181,0,90000).Items.Count==1,
    "Gear immediately below the stopping level is included");
Check(ExpertDeliveryPreviewPlan.Build([deliveryLow],600,90000,90000).Items.Count==0,
    "Full seals stop before any hypothetical delivery");
var nearFull = ExpertDeliveryPreviewPlan.Build([deliveryLow,deliveryMid],600,88000,90000);
Check(nearFull.Items.SequenceEqual([deliveryLow]) && nearFull.StopReason.Contains("上限"),
    "The preview stops before the next reward would overflow seals");
Check(ExpertDeliveryPreviewPlan.Build([deliveryLow],600,88965,90000).SealsAfter==90000,
    "A reward that exactly fills the seal capacity is included and then stops");
Check(ExpertDeliveryPreviewPlan.Build([deliveryLow with {Available=false},deliveryMid],600,0,90000).Items.Count==0,
    "An unavailable lowest item stops instead of skipping to later gear");
Check(ExpertDeliveryPreviewPlan.Build([deliveryLow with {SealReward=0}],600,0,90000).Items.Count==0,
    "Unknown seal rewards cannot enter a proposed delivery");
Check(ExpertDeliveryPreviewPlan.Build([deliveryLow with {ItemLevel=0}],600,0,90000).Items.Count==0,
    "Unknown item levels cannot enter a proposed delivery");
var duplicateDelivery=ExpertDeliveryPreviewPlan.Build([deliveryLow,deliveryLow],600,0,90000);
Check(duplicateDelivery.Items.Count==1 && duplicateDelivery.StopReason.Contains("异常"),
    "A duplicate physical inventory source stops the preview plan");
Check(ExpertDeliveryPreviewPlan.Build([],600,0,90000).Items.Count==0, "An empty delivery list remains empty");
Check(ExpertDeliveryPreviewPlan.TryParseSeals("71,385/90,000",out var heldSeals,out var maxSeals)
    && heldSeals==71385 && maxSeals==90000, "The observed CN native seal balance parses correctly");
Check(!ExpertDeliveryPreviewPlan.TryParseSeals("正在加载",out _,out _)
    && !ExpertDeliveryPreviewPlan.TryParseSeals("90001/90000",out _,out _)
    && !ExpertDeliveryPreviewPlan.TryParseSeals("0/0",out _,out _), "Unavailable or invalid seal balances cannot authorize a preview plan");
bool RejectDelivery(int stopLevel,long held,long max)
{
    try { ExpertDeliveryPreviewPlan.Build([deliveryLow],stopLevel,held,max); return false; }
    catch(InvalidOperationException) { return true; }
}
Check(RejectDelivery(0,0,90000) && RejectDelivery(10000,0,90000), "Out-of-range stopping levels are rejected");
Check(RejectDelivery(600,-1,90000) && RejectDelivery(600,90001,90000), "Invalid currency snapshots are rejected");
Check(ExpertDeliverySubmitPolicy.CanConfirm(deliveryLow,deliveryLow,660,0,90000,true,true,1035),
    "Expert delivery confirms only an owned matching item and exact reward below the stopping level");
Check(!ExpertDeliverySubmitPolicy.CanConfirm(deliveryLow,deliveryLow,180,0,90000,true,true,1035),
    "The exact stopping item level cannot be submitted");
Check(!ExpertDeliverySubmitPolicy.CanConfirm(deliveryLow,deliveryLow,660,89000,90000,true,true,1035),
    "A current seal-cap overflow blocks final confirmation");
Check(!ExpertDeliverySubmitPolicy.CanConfirm(deliveryLow,deliveryLow,660,90000,90000,true,true,1035),
    "Full seals block final confirmation");
Check(!ExpertDeliverySubmitPolicy.CanConfirm(deliveryLow,deliveryLow with {Protected=true},660,0,90000,true,true,1035),
    "Registering an item to a gearset after preview blocks final confirmation");
Check(!ExpertDeliverySubmitPolicy.CanConfirm(deliveryLow,deliveryLow with {Key=deliveryLow.Key with {Slot=2}},660,0,90000,true,true,1035),
    "Moving an item after preview blocks final confirmation");
Check(!ExpertDeliverySubmitPolicy.CanConfirm(deliveryLow,deliveryLow with {Key=deliveryLow.Key with {HighQuality=true}},660,0,90000,true,true,1035),
    "Changing quality after preview blocks final confirmation");
Check(!ExpertDeliverySubmitPolicy.CanConfirm(deliveryLow,deliveryLow,660,0,90000,false,true,1035)
    && !ExpertDeliverySubmitPolicy.CanConfirm(deliveryLow,deliveryLow,660,0,90000,true,false,1035)
    && !ExpertDeliverySubmitPolicy.CanConfirm(deliveryLow,deliveryLow,660,0,90000,true,true,1064),
    "A foreign reward window, different item name, or different reward cannot be confirmed");
Check(ExpertDeliverySubmitPolicy.IsHqPrompt(true,true,4,"确定要交易优质道具吗？"),
    "The observed owned CN HQ prompt is accepted for an HQ source");
Check(!ExpertDeliverySubmitPolicy.IsHqPrompt(false,true,4,"确定要交易优质道具吗？")
    && !ExpertDeliverySubmitPolicy.IsHqPrompt(true,false,4,"确定要交易优质道具吗？")
    && !ExpertDeliverySubmitPolicy.IsHqPrompt(true,true,5,"确定要交易优质道具吗？")
    && !ExpertDeliverySubmitPolicy.IsHqPrompt(true,true,4,"确定要丢弃优质道具吗？"),
    "NQ sources, foreign owners, wrong events, and unrelated HQ prompts never receive Yes");
Check(!ExpertDeliverySubmitPolicy.Acknowledged(true,false,false,1000,1000,1035),
    "Disappearing inventory without a seal gain cannot acknowledge expert delivery");
Check(!ExpertDeliverySubmitPolicy.Acknowledged(true,true,false,1000,2035,1035),
    "A seal gain without removal of the source cannot acknowledge expert delivery");
Check(!ExpertDeliverySubmitPolicy.Acknowledged(false,false,false,1000,2035,1035)
    && !ExpertDeliverySubmitPolicy.Acknowledged(true,false,true,1000,2035,1035),
    "Unloaded inventory or an open native dialog blocks advancing to the next item");
Check(ExpertDeliverySubmitPolicy.Acknowledged(true,false,false,1000,2035,1035),
    "Source removal and the expected seal gain acknowledge one completed expert delivery");
Check(ExpertDeliverySubmitPolicy.NameKey("甲\uE03C")=="甲" && ExpertDeliverySubmitPolicy.NameKey("甲")!="甲乙",
    "HQ icons are ignored for exact name identity without accepting partial names");
var nativeMaskRows = new ExpertDeliveryNativeRow[]{new(0,"瘟疫医生面具",1064),new(6,"其他装备",1700)};
Check(ExpertDeliverySubmitPolicy.FindNativeRow(nativeMaskRows,"瘟疫医生面具",1064,55)==0,
    "Observed native renderer text resolves Plague Doctor's Mask to row zero without AtkValue names");
Check(ExpertDeliverySubmitPolicy.FindNativeRow([new(6,"瘟疫医生面具",1064)],"瘟疫医生面具",1064,55)==6,
    "Native selection uses the recycled renderer's real item index rather than its array slot");
Check(ExpertDeliverySubmitPolicy.FindNativeRow([new(0,"瘟疫医生面具",1064),new(0,"瘟疫医生面具",1064)],"瘟疫医生面具",1064,55)==0,
    "Multiple renderer references to the same actual row do not become duplicate gear");
Check(ExpertDeliverySubmitPolicy.FindNativeRow([new(0,"瘟疫医生面具",1064),new(1,"瘟疫医生面具",1064)],"瘟疫医生面具",1064,55)==-2,
    "Distinct identical native rows remain ambiguous and cannot be guessed");
Check(ExpertDeliverySubmitPolicy.FindNativeRow(nativeMaskRows,"瘟疫医生面具",1035,55)==-1,
    "Matching item text with a different displayed reward cannot be selected");
Check(ExpertDeliverySubmitPolicy.FindNativeRow([new(0,"瘟疫医生面具",1064,false)],"瘟疫医生面具",1064,55)==-1,
    "Disabled native rows cannot be selected");
Check(ExpertDeliverySubmitPolicy.FindNativeRow([new(-1,"瘟疫医生面具",1064),new(55,"瘟疫医生面具",1064)],"瘟疫医生面具",1064,55)==-1,
    "Recycled rows outside current native list bounds cannot be selected");
Check(ExpertDeliverySubmitPolicy.FindNativeRow([new(0,"瘟疫医生面具复制品",1064)],"瘟疫医生面具",1064,55)==-1,
    "Only exact item names select a native row, never partial names");
var deliveryRowsGate = new ExpertDeliveryRowGate();
Check(!deliveryRowsGate.Observe("old:0=红莲细剑;1=罗刹大斧;2=实用主义",1000),
    "The first native row snapshot cannot immediately dispatch a delivery event");
Check(!deliveryRowsGate.Observe("old:0=红莲细剑;1=罗刹大斧;2=实用主义",1100),
    "Two adjacent frames do not bypass native row settling time");
Check(!deliveryRowsGate.Observe("new:0=罗刹大斧;1=实用主义",1150),
    "A native list rebuild restarts the stability gate after a successful delivery");
Check(!deliveryRowsGate.Observe("new:0=罗刹大斧;1=实用主义",1300),
    "A newly shifted mapping is not used before it has settled");
Check(deliveryRowsGate.Observe("new:0=罗刹大斧;1=实用主义",1370),
    "Only a stable refreshed mapping permits selecting the next target");
deliveryRowsGate.Reset();
Check(!deliveryRowsGate.Observe("new:0=罗刹大斧;1=实用主义",2000),
    "Reopening the list or scrolling requires a new stable snapshot");
Check(!ExpertDeliverySubmitPolicy.CanConfirm(deliveryLow,deliveryLow,660,0,90000,true,false,1638),
    "A different reward-page item never receives final confirmation even after an owned HQ prompt");
var traceInfo = new List<string>();
var traceWarnings = new List<string>();
long traceNow = 100;
var deliveryTrace = new ExpertDeliveryTrace(traceInfo.Add,traceWarnings.Add,() => traceNow);
deliveryTrace.Begin(); deliveryTrace.Emit("Start","count=2"); deliveryTrace.Emit("SelectRow","row=1");
Check(traceInfo.Count==2 && traceInfo[0].Contains("seq=1") && traceInfo[1].Contains("seq=2")
    && traceInfo[0].Split(" seq=")[0]==traceInfo[1].Split(" seq=")[0],
    "A batch correlates lifecycle and selection records with a run ID and ordered sequence");
deliveryTrace.Wait("pending"); var traceCount=traceInfo.Count;
for (var tick=0;tick<100;tick++) deliveryTrace.Wait("pending changed "+tick);
Check(traceInfo.Count==traceCount,"Changing per-frame waits cannot bypass diagnostic throttling");
traceNow=2099; deliveryTrace.Wait("pending");
Check(traceInfo.Count==traceCount,"Wait diagnostics remain throttled before two seconds");
traceNow=2100; deliveryTrace.Wait("pending");
Check(traceInfo.Count==traceCount+1,"A persistent wait reports again after two seconds");
deliveryTrace.ResetStep(); deliveryTrace.Wait("new phase");
Check(traceInfo.Count==traceCount+2,"A new phase immediately reports its first wait");
deliveryTrace.Dialog("HQ accepted"); var dialogCount=traceInfo.Count;
deliveryTrace.Dialog("HQ accepted");
Check(traceInfo.Count==dialogCount,"An unchanged native confirmation is not logged every frame");
deliveryTrace.Dialog("HQ rejected",true);
Check(traceWarnings.Count==1 && traceWarnings[0].Contains("HQ rejected"),"Rejected confirmation details always reach warning logs");
Check(ExpertDeliveryTrace.Clean("row1\nrow2\r\t")=="row1\\nrow2\\r\\t",
    "Native text cannot inject extra log lines");
Check(ExpertDeliveryTrace.Clean(new string('x',1000),10).Length<30,
    "Unexpectedly large native text remains bounded");
deliveryTrace.Begin(); deliveryTrace.Emit("Start","new batch");
Check(traceInfo[^1].Contains("seq=1") && traceInfo[^1].Split(" seq=")[0]!=traceInfo[0].Split(" seq=")[0],
    "A restarted batch gets a fresh run ID and sequence");
if (args.Length == 0) return;
using var game = new GameData(args[0], new LuminaOptions { DefaultExcelLanguage = Lumina.Data.Language.ChineseSimplified });
var nativePrompts = game.Excel.GetSheet<Addon>(Lumina.Data.Language.ChineseSimplified)
    .Select(row => (row.RowId, Text: row.Text.ExtractText()))
    .Where(row => row.Text.Contains("缺少此装备的现有套装")).ToArray();
foreach (var prompt in nativePrompts)
    Console.WriteLine($"CN MERGE PROMPT: {prompt.RowId} => {prompt.Text.Replace("\n", "[newline]")}");
Check(nativePrompts.Any(row => row.RowId == 15663 && OutfitCompletionPolicy.IsMergePrompt(row.Text)),
    "Live CN Addon row 15663 matches the existing-outfit merge prompt");
var items = game.Excel.GetSheet<Item>(Lumina.Data.Language.ChineseSimplified);
var lookup = game.Excel.GetSheet<MirageStoreSetItemLookup>(Lumina.Data.Language.ChineseSimplified);
var names = new[] { "帕戈尔赞治愈胸衣", "战女神审言胸甲", "改良型斯卡艾瓦咏咒长衣" };
foreach (var name in names)
{
    var found = items.FirstOrDefault(row => row.Name.ExtractText() == name);
    if (found.RowId == 0)
    {
        Console.WriteLine($"SAMPLE NOT FOUND: {name}");
        continue;
    }
    var sets = lookup.TryGetRow(found.RowId, out var mapping)
        ? mapping.Item.Where(set => set.RowId != 0 && set.IsValid).Select(set => set.Value.Name.ExtractText()).ToArray()
        : [];
    Console.WriteLine($"CN SAMPLE: {name} ({found.RowId}) => {(sets.Length == 0 ? "no outfit marker" : string.Join(" / ", sets))}");
}
Check(lookup.Any(row => row.Item.Any(set => set.RowId != 0 && set.IsValid)), "Local CN outfit catalog loads and resolves set names");

var liveCatalog = new OutfitCatalog(lookup, items);
var marked = items.First(row => row.Name.ExtractText() == "帕戈尔赞治愈胸衣");
var ordinary = items.First(row => row.Name.ExtractText() == "改良型斯卡艾瓦咏咒长衣");
Check(liveCatalog.Get(marked.RowId)?.SetNames == "帕戈尔赞治愈套装", "Actual plugin catalog resolves screenshot marked item");
Check(liveCatalog.Get(ordinary.RowId) is null, "Actual plugin catalog excludes screenshot unmarked item");
var scan = OutfitListBuilder.Build([new(marked.RowId, false, "兵装库·身体", 1, 30000),
    new(ordinary.RowId, false, "物品栏 4", 20, 30000)], liveCatalog.Get);
Check(scan.Count == 1 && scan[0].ItemId == marked.RowId, "CN catalog integration selects only marked equipment");

Check(liveCatalog.Get(marked.RowId)?.Part == "身体" && liveCatalog.Get(marked.RowId)?.IconId > 0, "Local CN item resolves equipment part and real icon" );

var cabinetRows = game.Excel.GetSheet<Lumina.Excel.Sheets.Cabinet>().Where(row => row.Item.RowId != 0).ToArray();
Check(cabinetRows.Length > 0, "CN Cabinet catalog contains real collection entries");
Check(cabinetRows.Any(row => row.RowId == 0 && row.Item.IsValid), "CN collection row zero must not be treated as an absent entry");
Check(cabinetRows.All(row => row.Item.IsValid), "Cabinet item references are valid");
Check(cabinetRows.Any(row => row.RowId != row.Item.RowId), "Cabinet storage uses collection row ID, not equipment ID");
Check(cabinetRows.Any(row => row.Item.Value.Icon > 0 && OutfitCatalog.GetPart(row.Item.Value) != "其他"),
    "CN cabinet equipment resolves native icons and equipment parts");

var liveSleep = items.First(row => row.Name.ExtractText() == "朴素寝室拖鞋");
Check(liveCatalog.Get(liveSleep.RowId)?.Sets?.Any(set => set.Id == 45518 && set.Pieces[5] == 40406 && set.Pieces[6] == 40407) == true,
    "Actual CN sleepwear mapping preserves the pants and shoes restore-bit positions");
Check(items.GetRow(QualityNormalizationPolicy.GlamourPrismId).Name.ExtractText() == "幻象棱晶", "Quality normalization prism ID matches the local CN resource");

var infantryFeet = items.First(row=>row.Name.ExtractText()=="步兵长靴");
var infantryDefinition = liveCatalog.Get(infantryFeet.RowId)!.Sets!.First(set=>set.Name=="步兵套装");
Check(OutfitSubmissionLayout.Pack(infantryDefinition.Pieces,[new(infantryFeet.RowId,3205,0,false)],false).Single().ItemId==infantryFeet.RowId,
    "Actual CN infantry boots produce one first-position submission despite preceding empty outfit slots");
