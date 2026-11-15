using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Timberborn.BlockSystem;
using Timberborn.EntitySystem;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace Jove.VisualTerrainBlock
{
    /// <summary>
    /// 载入存档时的地形物理校验会删掉所有"不连通到地面支撑"的方块，
    /// 我们的悬空地形块天生如此，所以必须在删除点把它们摘出来。
    ///
    /// 实测（1.1.2.8）真正执行删除的是这一条链：
    ///   TerrainPhysicsUpdater.PostLoad()
    ///     → TerrainAndBlockObjectsToDeleteFinder.FindAll(terrain, terrainSet, blockObjectsToDelete)
    ///     → 逐个删除集合里的方块，日志 "Loaded BlockObject ... is not supported by terrain physics. Deleting it."
    ///
    /// 另有一条更早的路径 TerrainPhysicsPostLoader.RemoveBlockObjects()（会报 LoadingIssue 弹窗），两处都处理。
    /// 都只摘掉我们自己的方块，其它对象的校验保持原样。
    /// </summary>
    public class LoadCleanupPatch : ILoadableSingleton
    {
        private const string HarmonyId = "jove.visualterrainblock.loadcleanup";

        private static readonly HashSet<BlockObject> OurBlocks = new HashSet<BlockObject>();

        private static FieldInfo _validBlockObjectsField;
        private static FieldInfo _entityRegistryField;
        private static EntityRegistry _entityRegistry;
        private static bool _patched;

        public LoadCleanupPatch(EntityRegistry entityRegistry)
        {
            _entityRegistry = entityRegistry;
        }

        public void Load()
        {
            if (_patched)
            {
                return;
            }

            _patched = true;
            try
            {
                PatchPostLoaderRemove();
                PatchDeleteFinder();
            }
            catch (Exception exception)
            {
                Debug.LogError("[VisualTerrainBlock] 安装补丁失败: " + exception);
            }
        }

        private static void PatchPostLoaderRemove()
        {
            Type loaderType = Type.GetType("Timberborn.TerrainPhysics.TerrainPhysicsPostLoader, Timberborn.TerrainPhysics");
            MethodInfo target = loaderType?.GetMethod("RemoveBlockObjects",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (target == null)
            {
                Debug.LogWarning("[VisualTerrainBlock] 未找到 TerrainPhysicsPostLoader.RemoveBlockObjects，跳过该路径");
                return;
            }

            if (AlreadyPatched(target))
            {
                return;
            }

            _validBlockObjectsField = loaderType.GetField("_validBlockObjects", BindingFlags.Instance | BindingFlags.NonPublic);
            _entityRegistryField = loaderType.GetField("_entityRegistry", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_validBlockObjectsField == null || _entityRegistryField == null)
            {
                Debug.LogWarning("[VisualTerrainBlock] TerrainPhysicsPostLoader 内部字段名变了，跳过该路径");
                return;
            }

            MethodInfo prefix = typeof(LoadCleanupPatch).GetMethod(nameof(ProtectInPostLoader),
                BindingFlags.Static | BindingFlags.NonPublic);
            new Harmony(HarmonyId).Patch(target, prefix: new HarmonyMethod(prefix));
            Debug.Log("[VisualTerrainBlock] 已给 TerrainPhysicsPostLoader.RemoveBlockObjects 打补丁");
        }

        private static void PatchDeleteFinder()
        {
            Type finderType = Type.GetType("Timberborn.TerrainPhysics.TerrainAndBlockObjectsToDeleteFinder, Timberborn.TerrainPhysics");
            MethodInfo target = finderType?.GetMethod("FindAll",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (target == null)
            {
                Debug.LogWarning("[VisualTerrainBlock] 未找到 TerrainAndBlockObjectsToDeleteFinder.FindAll，跳过该路径");
                return;
            }

            if (AlreadyPatched(target))
            {
                return;
            }

            MethodInfo postfix = typeof(LoadCleanupPatch).GetMethod(nameof(StripOurBlocksFromDeleteSet),
                BindingFlags.Static | BindingFlags.NonPublic);
            new Harmony(HarmonyId).Patch(target, postfix: new HarmonyMethod(postfix));
            Debug.Log("[VisualTerrainBlock] 已给 TerrainAndBlockObjectsToDeleteFinder.FindAll 打补丁");
        }

        private static bool AlreadyPatched(MethodInfo target)
        {
            Patches existing = Harmony.GetPatchInfo(target);
            return existing != null && existing.Owners.Contains(HarmonyId);
        }

        /// <summary>旧路径：RemoveBlockObjects 执行前把我们的方块补登记进白名单。</summary>
        private static bool ProtectInPostLoader(object __instance)
        {
            try
            {
                HashSet<BlockObject> validObjects = _validBlockObjectsField.GetValue(__instance) as HashSet<BlockObject>;
                EntityRegistry registry = _entityRegistryField.GetValue(__instance) as EntityRegistry;
                if (validObjects == null || registry == null)
                {
                    return true;
                }

                int protectedCount = 0;
                foreach (BlockObject blockObject in CollectOurBlocks(registry))
                {
                    if (validObjects.Add(blockObject))
                    {
                        protectedCount++;
                    }
                }

                if (protectedCount > 0)
                {
                    Debug.Log("[VisualTerrainBlock] 载入校验中保留了 " + protectedCount + " 个悬空地形块");
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("[VisualTerrainBlock] 保护悬空地形块时出错: " + exception);
            }

            // 继续执行原方法，其它对象的校验不受影响
            return true;
        }

        /// <summary>主路径：从待删除集合里把我们的方块摘掉。</summary>
        private static void StripOurBlocksFromDeleteSet(IEnumerable<Vector3Int> terrainToDelete,
            HashSet<Vector3Int> terrainSet, HashSet<BlockObject> blockObjectsToDelete)
        {
            try
            {
                if (blockObjectsToDelete == null || blockObjectsToDelete.Count == 0 || _entityRegistry == null)
                {
                    return;
                }

                HashSet<BlockObject> ours = new HashSet<BlockObject>(CollectOurBlocks(_entityRegistry));
                if (ours.Count == 0)
                {
                    return;
                }

                int removed = blockObjectsToDelete.RemoveWhere(ours.Contains);
                if (removed > 0)
                {
                    Debug.Log("[VisualTerrainBlock] 载入删除列表中摘出了 " + removed + " 个悬空地形块");
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("[VisualTerrainBlock] 清理删除列表时出错: " + exception);
            }
        }

        private static IEnumerable<BlockObject> CollectOurBlocks(EntityRegistry registry)
        {
            OurBlocks.Clear();
            foreach (EntityComponent entity in registry.Entities)
            {
                bool isOurs = false;
                foreach (object component in entity.RegisteredComponents)
                {
                    if (component is VisualTerrainBlockMarker)
                    {
                        isOurs = true;
                        break;
                    }
                }

                if (!isOurs)
                {
                    continue;
                }

                foreach (object component in entity.RegisteredComponents)
                {
                    if (component is BlockObject blockObject)
                    {
                        OurBlocks.Add(blockObject);
                        break;
                    }
                }
            }

            return OurBlocks;
        }
    }
}
