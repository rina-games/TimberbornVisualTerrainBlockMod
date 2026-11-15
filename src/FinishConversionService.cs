using System;
using System.Collections.Generic;
using Timberborn.BlockSystem;
using Timberborn.EntitySystem;
using Timberborn.SingletonSystem;
using Timberborn.TerrainSystem;
using UnityEngine;

namespace Jove.VisualTerrainBlock
{
    /// <summary>
    /// 把建成的悬空地形块转成真地形。
    ///
    /// 触发点用的是游戏事件（`EnteredFinishedStateEvent`）而不是组件生命周期：
    /// 组件里的 `Update/PostInitializeEntity` 在实测中没被调到，转换一直没发生，
    /// 所以方块一直是"方块对象"——挖掘工具只能挖地形，自然"选中了没反应"。
    ///
    /// 单例订阅事件是确定会生效的；另外读档时再扫一遍，把存档里"已建成但没转地形"的方块补转掉。
    /// 转换参数照抄原版 GroundRaisingService.OnEntityDeleted：SetTerrain(coords, 1)。
    /// </summary>
    public class FinishConversionService : ILoadableSingleton, IPostLoadableSingleton
    {
        private const string TemplateName = "VisualTerrainBlock";

        private readonly ITerrainService _terrainService;
        private readonly EntityService _entityService;
        private readonly EntityRegistry _entityRegistry;

        private readonly List<BlockObject> _pending = new List<BlockObject>();

        public FinishConversionService(ITerrainService terrainService, EntityService entityService,
            EntityRegistry entityRegistry)
        {
            _terrainService = terrainService;
            _entityService = entityService;
            _entityRegistry = entityRegistry;
        }

        public void Load()
        {
            Debug.Log("[VisualTerrainBlock] 转换服务已就绪（建成即变真地形）");
        }

        [OnEvent]
        public void OnEnteredFinishedState(EnteredFinishedStateEvent @event)
        {
            if (@event == null || !IsOurBlock(@event.BlockObject))
            {
                return;
            }

            Convert(@event.BlockObject);
        }

        public void PostLoad()
        {
            _pending.Clear();
            foreach (EntityComponent entity in _entityRegistry.Entities)
            {
                foreach (object component in entity.RegisteredComponents)
                {
                    if (component is BlockObject blockObject && blockObject.IsFinished && IsOurBlock(blockObject))
                    {
                        _pending.Add(blockObject);
                    }
                }
            }

            foreach (BlockObject blockObject in _pending)
            {
                Convert(blockObject);
            }

            if (_pending.Count > 0)
            {
                Debug.Log("[VisualTerrainBlock] 读档补转了 " + _pending.Count + " 个已建成的悬空地形块");
            }

            _pending.Clear();
        }

        private void Convert(BlockObject blockObject)
        {
            try
            {
                Vector3Int coordinates = blockObject.Coordinates;
                _terrainService.SetTerrain(coordinates, 1);
                _entityService.Delete(blockObject);
            }
            catch (Exception exception)
            {
                Debug.LogError("[VisualTerrainBlock] 转换成真地形失败: " + exception);
            }
        }

        private static bool IsOurBlock(BlockObject blockObject)
        {
            if (blockObject == null)
            {
                return false;
            }

            try
            {
                string name = blockObject.Name;
                return !string.IsNullOrEmpty(name) && name.StartsWith(TemplateName, StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
