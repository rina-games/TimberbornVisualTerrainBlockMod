using System;
using System.Collections.Generic;
using Timberborn.BlockSystem;
using Timberborn.Common;
using Timberborn.DeconstructionSystem;
using Timberborn.EntitySystem;
using Timberborn.MapIndexSystem;
using Timberborn.SingletonSystem;
using Timberborn.TerrainSystem;
using UnityEngine;

namespace Jove.VisualTerrainBlock
{
    /// <summary>
    /// 把建成的悬空地形块转成真地形。
    ///
    /// 触发点：
    ///   1) `EnteredFinishedStateEvent`：建成那一帧立刻转
    ///   2) `IPostLoadableSingleton.PostLoad()`：读档时扫一遍，把"已建成但没转地形"的补转
    ///
    /// 枚举方式：`BlockObject` 不实现 `IRegisteredComponent`，所以 `EntityComponentRegistry.GetAll<T>()`
    /// 和 `EntityRegistry.Entities[].RegisteredComponents` 都拿不到它（这也是前几版一次都没转成的原因）。
    /// 这里改成按地图格遍历，用 `IBlockService.GetObjectsAt(coords)` 取每格的方块；50×50 的地图量级完全可以接受。
    ///
    /// 转换参数照抄原版 GroundRaisingService.OnEntityDeleted：SetTerrain(coords, 1)。
    /// </summary>
    public class FinishConversionService : ILoadableSingleton, IPostLoadableSingleton
    {
        private const string TemplateName = "VisualTerrainBlock";

        private readonly ITerrainService _terrainService;
        private readonly EntityService _entityService;
        private readonly IBlockService _blockService;
        private readonly MapIndexService _mapIndexService;
        private readonly EventBus _eventBus;

        private readonly List<BlockObject> _pending = new List<BlockObject>();
        private readonly HashSet<BlockObject> _seen = new HashSet<BlockObject>();
        private readonly HashSet<BlockObject> _converted = new HashSet<BlockObject>();

        public FinishConversionService(ITerrainService terrainService, EntityService entityService,
            IBlockService blockService, MapIndexService mapIndexService, EventBus eventBus)
        {
            _terrainService = terrainService;
            _entityService = entityService;
            _blockService = blockService;
            _mapIndexService = mapIndexService;
            _eventBus = eventBus;
        }

        public void Load()
        {
            // [OnEvent] 不会自动生效，必须注册。否则建成后实体留在原地，
            // 施工标记（三角形图标）和方块模型都不会被清掉。
            _eventBus.Register(this);
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
            try
            {
                _pending.Clear();
                _seen.Clear();

                Vector3Int size = _mapIndexService.TerrainSize;
                int scanned = 0;
                for (int x = 0; x < size.x; x++)
                {
                    for (int z = 0; z < size.z; z++)
                    {
                        for (int y = 0; y < size.y; y++)
                        {
                            scanned++;
                            var objects = _blockService.GetObjectsAt(new Vector3Int(x, y, z));
                            foreach (BlockObject blockObject in objects)
                            {
                                if (!IsOurBlock(blockObject) || !blockObject.IsFinished || !_seen.Add(blockObject))
                                {
                                    continue;
                                }

                                _pending.Add(blockObject);
                            }
                        }
                    }
                }

                foreach (BlockObject blockObject in _pending)
                {
                    Convert(blockObject);
                }

                Debug.Log("[VisualTerrainBlock] 读档扫描 " + scanned + " 格，找到我们的已建成方块 "
                          + _pending.Count + " 个 → 已转成地形");
                _pending.Clear();
                _seen.Clear();
            }
            catch (Exception exception)
            {
                Debug.LogError("[VisualTerrainBlock] 读档补转失败: " + exception);
            }
        }

        private void Convert(BlockObject blockObject)
        {
            if (!blockObject || !_converted.Add(blockObject))
            {
                return;
            }

            try
            {
                Vector3Int coordinates = blockObject.Coordinates;
                // 原版地形块在删除前会 DisableDeconstruction。拆除组件还开着时，
                // 删除会触发 BuildingDeconstructedEvent，把工地库存里的泥土按拆除比例退出来。
                blockObject.GetComponent<Deconstructible>()?.DisableDeconstruction();
                _terrainService.SetTerrain(coordinates, 1);
                _entityService.Delete(blockObject);
                Debug.Log("[VisualTerrainBlock] 建成并转成地形 " + coordinates);
            }
            catch (Exception exception)
            {
                _converted.Remove(blockObject);
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
