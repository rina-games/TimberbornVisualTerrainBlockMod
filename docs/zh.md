# 悬空地形块 — 设计说明

[English](en.md)

版本 0.5.0。Mod Id：`Jove.VisualTerrainBlock`。最低游戏版本：1.1.0.0。

## 当前行为

玩家在地形改造工具组里放置「悬空地形块」。它是一个普通工地：消耗 4 个泥土、需要 1000 科学点、由海狸施工。建成那一帧，`FinishConversionService` 把这一格写成真地形（`SetTerrain(coords, 1)`），然后删掉方块实体。

建成前可以悬空、可以贴着崖壁拉长。建成之后就是原版地形，挖掘、挡水、种树和再往上建造都走游戏自己的规则。失去支撑时也会按原版塌落，本 mod 不拦截这条逻辑。

## 原版为什么拉不长

原版地形块的 `MatterBelow` 已经是 `Any`，数据上每个地形体素只是一个 bool。限制来自玩法规则：

| 规则 | 位置 | 玩家看到的现象 |
|---|---|---|
| 必须等后方的地形块完工 | `TerraformingDirectionalBlocker` | 「此工程正等待后方的工程完成。」 |
| 必须自下而上 | 同上 | 「此工程正等待下方的工程完工。」 |
| 放置预览的物理支撑校验 | `TerrainPhysicsService.ValidateBlockObjectPreview` | 往空气里拉时预览变红 |
| 读档时清理无支撑结构 | `TerrainPhysicsPostLoader.Validate` | 读档后悬空物消失 |

本建筑不加 `GroundRaiserSpec`，因此不会带上 `TerraformingDirectionalBlocker`。放置预览只对名字以 `VisualTerrainBlock` 开头的方块放行。

## 实现

| 文件 | 作用 |
|---|---|
| `Buildings/Landscaping/VisualTerrainBlock/VisualTerrainBlock.blueprint.json` | 建筑蓝图。外观复用原版地形块模型 |
| `src/FinishConversionService.cs` | 订阅 `EnteredFinishedStateEvent`，建成后铺地形并删除实体；读档时补转已建成但还没转换的方块 |
| `src/PlacementPatch.cs` | 悬空预览可以通过；未完工的工地不会被物理系统顺手删掉 |
| `src/LoadCleanupPatch.cs` | 读档时跳过整段无支撑清理，避免悬空地形被删掉；拆除时保留玩家选中的工地 |
| `src/DeletionPatch.cs` | 开发者模式的物体删除工具可以选中本建筑 |
| `src/ExplosionPatch.cs` | 炸药可以拆除还没建成的悬空工地 |

`[OnEvent]` 不会自动生效，`FinishConversionService.Load` 里必须调用 `EventBus.Register`。删除实体之前要先 `DisableDeconstruction()`，否则拆除系统会把工地库存里的泥土退成地上的材料堆。

## 蓝图位置

建筑蓝图放在 mod 根目录的 `Buildings/` 下。`Blueprints/` 只放 `TemplateCollections`。放错位置时，游戏启动会抛出 `Blueprint not found at path: Buildings/...`。

## 有意保留的原版规则

- `CanTerrainBeAdded` 不打全局补丁。原版需要支撑才能完工的建筑仍按原版判断。
- 建成后的地形被炸药或挖掘拆掉支撑时，后续失去支撑的地形会塌落。
