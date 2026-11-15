using System;
using Timberborn.BaseComponentSystem;
using Timberborn.BlockSystem;
using Timberborn.BlueprintSystem;
using Timberborn.EntitySystem;
using Timberborn.TerrainSystem;
using UnityEngine;

// ComponentSpec 是 C# record，netstandard2.1 下需要这个垫片才能编译 record
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}

namespace Jove.VisualTerrainBlock
{
    /// <summary>
    /// 悬空地形块的标记 + 转换组件。
    ///
    /// 玩法保留"工地 → 海狸施工 → 建成"：施工阶段就是普通建筑工地。
    /// 建成的那一刻（`BlockObject.IsFinished` 变 true）把这个方块实体删掉，
    /// 并在同一格生成真地形（`SetTerrain(coords, 1)`，参数照抄原版
    /// `GroundRaisingService.OnEntityDeleted`）。
    ///
    /// 这样：
    ///   1) 建造过程保留（要工人、要材料）
    ///   2) 建成后是真地形 → 可挖、挡水、能种树、放置时按格对齐
    ///   3) 施工中的工地可以用拆除工具取消（见 DeletionPatch）
    /// </summary>
    public class VisualTerrainBlockMarker : BaseComponent, IPostInitializableEntity, IUpdatableComponent
    {
        private readonly ITerrainService _terrainService;
        private readonly EntityService _entityService;

        private bool _converted;

        public VisualTerrainBlockMarker(ITerrainService terrainService, EntityService entityService)
        {
            _terrainService = terrainService;
            _entityService = entityService;
        }

        public void PostInitializeEntity()
        {
            TryConvert();
        }

        public void Update()
        {
            // 建成的那一帧转成地形；转完后实体被删掉，不会再进入这里
            if (!_converted)
            {
                TryConvert();
            }
        }

        private void TryConvert()
        {
            if (_converted)
            {
                return;
            }

            try
            {
                BlockObject blockObject = GetComponent<BlockObject>();
                if (blockObject == null || !blockObject.IsFinished)
                {
                    return;
                }

                _converted = true;
                Vector3Int coordinates = blockObject.Coordinates;
                _terrainService.SetTerrain(coordinates, 1);
                _entityService.Delete(this);
            }
            catch (Exception exception)
            {
                _converted = true;
                Debug.LogError("[VisualTerrainBlock] 转换成真地形失败: " + exception);
            }
        }
    }

    public record VisualTerrainBlockMarkerSpec : ComponentSpec
    {
    }
}
