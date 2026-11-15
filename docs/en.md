# Visual Terrain Block — Design notes

[中文](zh.md)

Version 0.5.0. Mod id: `Jove.VisualTerrainBlock`. Minimum game version: 1.1.0.0.

## Current behavior

The player places **Floating Terrain Block** from the Landscaping tool group. It is a normal construction site: 4 dirt, 1000 science, built by beavers. On the frame it finishes, `FinishConversionService` writes that cell as real terrain (`SetTerrain(coords, 1)`) and deletes the block entity.

Before it finishes, the site can hang in the air or run out from a cliff. After it finishes, it is vanilla terrain. Digging, water, trees, and further building all use the game's own rules. Terrain that loses its support still collapses; this mod does not intercept that.

## Why the vanilla block cannot be extended

The vanilla terrain block already uses `MatterBelow: Any`, and each terrain voxel is only a bool. The limits come from gameplay rules:

| Rule | Where | What the player sees |
|---|---|---|
| The block behind must finish first | `TerraformingDirectionalBlocker` | The site waits for the block behind it |
| Blocks must be built from the bottom up | same | The site waits for the block below it |
| Placement preview checks physical support | `TerrainPhysicsService.ValidateBlockObjectPreview` | The preview turns red in open air |
| Unsupported structures are removed on load | `TerrainPhysicsPostLoader.Validate` | Floating terrain disappears after loading a save |

This building does not include `GroundRaiserSpec`, so it never receives `TerraformingDirectionalBlocker`. The placement preview is allowed only for block objects whose name starts with `VisualTerrainBlock`.

## Implementation

| File | Role |
|---|---|
| `Buildings/Landscaping/VisualTerrainBlock/VisualTerrainBlock.blueprint.json` | Building blueprint. Reuses the vanilla terrain-block mesh |
| `src/FinishConversionService.cs` | Listens for `EnteredFinishedStateEvent`, then sets terrain and deletes the entity. On load, converts finished blocks that were not converted yet |
| `src/PlacementPatch.cs` | Lets the floating preview succeed, and keeps unfinished sites from being deleted by the physics system |
| `src/LoadCleanupPatch.cs` | Skips the unsupported-structure cleanup on load so floating terrain survives. Keeps player-selected sites in the deletion list |
| `src/DeletionPatch.cs` | Lets the developer-mode object deletion tool select this building |
| `src/ExplosionPatch.cs` | Lets dynamite remove an unfinished floating site |

`[OnEvent]` is not subscribed automatically. `FinishConversionService.Load` must call `EventBus.Register`. Before the entity is deleted, call `DisableDeconstruction()`. Otherwise the deconstruction system refunds the dirt in the site inventory as a goods pile.

## Blueprint location

The building blueprint lives under `Buildings/` at the mod root. `Blueprints/` is only for `TemplateCollections`. A blueprint in the wrong folder makes the game throw `Blueprint not found at path: Buildings/...` at startup.

## Vanilla rules kept on purpose

- `CanTerrainBeAdded` is not patched globally. Vanilla buildings that require support still use the original check.
- After this block has become terrain, dynamite or digging that removes its support still collapses the unsupported terrain beyond it.
