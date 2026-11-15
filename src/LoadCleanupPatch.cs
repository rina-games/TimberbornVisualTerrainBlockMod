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
    /// 载入存档时，游戏会做一次"地形物理支撑"校验：
    /// TerrainPhysicsPostLoader.ValidateAll → Validate → RemoveBlockObjects / RemoveTerrain，
    /// 把所有不连通到地面支撑的结构删掉（弹窗「对象位置无效，已被删除」）。
    ///
    /// 我们的悬空地形块天生就是无支撑的，必然被这一套清掉，所以这里补一刀：
    /// 在 RemoveBlockObjects 执行前，把所有带 VisualTerrainBlockMarker 的方块
    /// 塞进内部白名单 _validBlockObjects，让它们免于被删；其它对象的校验逻辑保持原样。
    /// </summary>
    public class LoadCleanupPatch : ILoadableSingleton
    {
        private const string HarmonyId = "jove.visualterrainblock.loadcleanup";

        private static FieldInfo _validBlockObjectsField;
        private static FieldInfo _entityRegistryField;
        private static bool _patched;

        public void Load()
        {
            if (_patched)
            {
                return;
            }

            _patched = true;
            try
            {
                Patch();
            }
            catch (Exception exception)
            {
                Debug.LogError("[VisualTerrainBlock] 安装补丁失败: " + exception);
            }
        }

        private static void Patch()
        {
            Type loaderType = Type.GetType("Timberborn.TerrainPhysics.TerrainPhysicsPostLoader, Timberborn.TerrainPhysics");
            if (loaderType == null)
            {
                Debug.LogError("[VisualTerrainBlock] 找不到 TerrainPhysicsPostLoader，补丁未安装");
                return;
            }

            MethodInfo target = loaderType.GetMethod("RemoveBlockObjects",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (target == null)
            {
                Debug.LogError("[VisualTerrainBlock] 找不到 TerrainPhysicsPostLoader.RemoveBlockObjects，补丁未安装");
                return;
            }

            Patches existing = Harmony.GetPatchInfo(target);
            if (existing != null && existing.Prefixes.Any(patch => patch.owner == HarmonyId))
            {
                Debug.Log("[VisualTerrainBlock] 补丁已存在，跳过");
                return;
            }

            _validBlockObjectsField = loaderType.GetField("_validBlockObjects",
                BindingFlags.Instance | BindingFlags.NonPublic);
            _entityRegistryField = loaderType.GetField("_entityRegistry",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (_validBlockObjectsField == null || _entityRegistryField == null)
            {
                Debug.LogError("[VisualTerrainBlock] 内部字段名变了，补丁未安装");
                return;
            }

            MethodInfo prefix = typeof(LoadCleanupPatch).GetMethod(nameof(ProtectOurBlocks),
                BindingFlags.Static | BindingFlags.NonPublic);
            new Harmony(HarmonyId).Patch(target, prefix: new HarmonyMethod(prefix));
            Debug.Log("[VisualTerrainBlock] 已给 TerrainPhysicsPostLoader.RemoveBlockObjects 打补丁");
        }

        private static bool ProtectOurBlocks(object __instance)
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
                foreach (EntityComponent entity in registry.Entities)
                {
                    bool isOurs = false;
                    BlockObject blockObject = null;
                    foreach (object component in entity.RegisteredComponents)
                    {
                        if (component is VisualTerrainBlockMarker)
                        {
                            isOurs = true;
                        }
                        else if (component is BlockObject candidate)
                        {
                            blockObject = candidate;
                        }
                    }

                    if (isOurs && blockObject != null && validObjects.Add(blockObject))
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
    }
}
