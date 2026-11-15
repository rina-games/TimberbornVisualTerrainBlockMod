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
    /// 玩法：方块是"放下即成"的（蓝图 PlaceFinished = true），没有施工阶段——
    /// 因为悬空位置工人多半到不了，工地会永远卡着，而且工地用拆除工具也选不中。
    /// 实体初始化完成后，这里直接把这一格变成真地形，再把方块实体删掉：
    ///   1) 真地形 → 可挖、挡水、能种树、放置时按格对齐（走地形拾取分支）
    ///   2) 没有残留方块 → 不存在"拆不掉"的对象
    ///
    /// 转换参数照抄原版 GroundRaisingService.OnEntityDeleted：SetTerrain(coords, 1)。
    /// </summary>
    public class VisualTerrainBlockMarker : BaseComponent, IPostInitializableEntity
    {
        private readonly ITerrainService _terrainService;
        private readonly EntityService _entityService;

        public VisualTerrainBlockMarker(ITerrainService terrainService, EntityService entityService)
        {
            _terrainService = terrainService;
            _entityService = entityService;
        }

        public void PostInitializeEntity()
        {
            try
            {
                BlockObject blockObject = GetComponent<BlockObject>();
                if (blockObject == null || !blockObject.IsFinished)
                {
                    return;
                }

                Vector3Int coordinates = blockObject.Coordinates;
                _terrainService.SetTerrain(coordinates, 1);
                _entityService.Delete(this);
            }
            catch (Exception exception)
            {
                Debug.LogError("[VisualTerrainBlock] 转换成真地形失败: " + exception);
            }
        }
    }

    public record VisualTerrainBlockMarkerSpec : ComponentSpec
    {
    }
}
