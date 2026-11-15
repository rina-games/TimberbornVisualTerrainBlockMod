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
    /// 原版炸药（Dynamite）爆炸只做三件事：降一格地形、删掉自己脚下的道路方块、炸死范围内的海狸。
    /// 它不会删除建筑类方块，所以放在悬空地形块上引爆后，块还在。
    ///
    /// 这里给 Dynamite.Detonate 加一个 postfix：爆炸时把爆点周围（切比雪夫距离 ≤ 1）
    /// 名字以 VisualTerrainBlock 开头的方块一起删掉，让"放炸药 = 拆掉悬空块"符合直觉。
    ///
    /// 识别用方块名而不是自定义组件类型：mod 程序集可能被游戏加载多份，类型身份对不上。
    /// </summary>
    public class ExplosionPatch : ILoadableSingleton
    {
        private const string HarmonyId = "jove.visualterrainblock.explosion";
        private const string TemplateName = "VisualTerrainBlock";

        private static IBlockService _blockService;
        private static EntityService _entityService;
        private static FieldInfo _blockObjectField;
        private static bool _patched;

        public ExplosionPatch(IBlockService blockService, EntityService entityService)
        {
            _blockService = blockService;
            _entityService = entityService;
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
                PatchDetonate();
            }
            catch (Exception exception)
            {
                Debug.LogError("[VisualTerrainBlock] 炸药补丁安装失败: " + exception);
            }
        }

        private static void PatchDetonate()
        {
            Type dynamiteType = Type.GetType("Timberborn.Explosions.Dynamite, Timberborn.Explosions");
            if (dynamiteType == null)
            {
                Debug.LogWarning("[VisualTerrainBlock] 找不到 Dynamite 类型，跳过炸药补丁");
                return;
            }

            _blockObjectField = dynamiteType.GetField("_blockObject", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_blockObjectField == null)
            {
                Debug.LogWarning("[VisualTerrainBlock] Dynamite._blockObject 字段不存在，跳过炸药补丁");
                return;
            }

            MethodInfo target = dynamiteType
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(method => method.Name == "Detonate" && method.GetParameters().Length == 0);
            if (target == null)
            {
                Debug.LogWarning("[VisualTerrainBlock] 找不到 Dynamite.Detonate，跳过炸药补丁");
                return;
            }

            Patches existing = Harmony.GetPatchInfo(target);
            if (existing != null && existing.Owners.Contains(HarmonyId))
            {
                return;
            }

            MethodInfo postfix = typeof(ExplosionPatch).GetMethod(nameof(DestroyNearbyBlocks),
                BindingFlags.Static | BindingFlags.NonPublic);
            new Harmony(HarmonyId).Patch(target, postfix: new HarmonyMethod(postfix));
            Debug.Log("[VisualTerrainBlock] 已给 Dynamite.Detonate 打补丁（爆炸会炸掉周围的悬空地形块）");
        }

        private static void DestroyNearbyBlocks(object __instance)
        {
            try
            {
                BlockObject dynamite = _blockObjectField.GetValue(__instance) as BlockObject;
                if (dynamite == null)
                {
                    return;
                }

                Vector3Int center = dynamite.Coordinates;
                HashSet<BlockObject> toDelete = new HashSet<BlockObject>();
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            Vector3Int coordinates = new Vector3Int(center.x + dx, center.y + dy, center.z + dz);
                            foreach (BlockObject candidate in _blockService.GetObjectsAt(coordinates))
                            {
                                if (IsOurBlock(candidate))
                                {
                                    toDelete.Add(candidate);
                                }
                            }
                        }
                    }
                }

                foreach (BlockObject blockObject in toDelete)
                {
                    _entityService.Delete(blockObject);
                }

                if (toDelete.Count > 0)
                {
                    Debug.Log("[VisualTerrainBlock] 炸药炸掉了 " + toDelete.Count + " 个悬空地形块");
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("[VisualTerrainBlock] 爆炸处理悬空地形块时出错: " + exception);
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
