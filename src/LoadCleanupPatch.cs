using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Timberborn.BlockSystem;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace Jove.VisualTerrainBlock
{
    /// <summary>
    /// 载入存档时的地形物理校验会删掉所有"不连通到地面支撑"的方块，
    /// 我们的悬空地形块天生如此，所以必须在删除点拦住。
    ///
    /// 实测（Timberborn 1.1.2.8）删除就发生在这一条链里：
    ///   TerrainPhysicsPostLoader.ValidateAll() → Validate() → RemoveBlockObjects()
    ///   RemoveBlockObjects 对每个 BlockObject 判断：不在白名单 _validBlockObjects 里 →
    ///   AddIssue(LoadingIssue.BlockObjectLoadingIssue) + Delete()，
    ///   并打日志 "Loaded BlockObject X at ... is not supported by terrain physics. Deleting it."
    ///
    /// 做法：
    ///   1) prefix 掉 TerrainPhysicsPostLoader.Validate()，直接当"校验通过"返回。
    ///      （白名单是 GetInitialCandidates 从地图底部逐列 BFS 填出来的，悬空方块进不去，
    ///        所以逐个登记白名单这条路走不通；直接跳过整段清理更可靠。）
    ///   2) 顺带在 TerrainAndBlockObjectsToDeleteFinder.FindAll 的产出集合里按方块名摘掉我们的方块，
    ///      防止其它路径复用这份删除列表。
    ///
    /// 识别一律用 BlockObject.Name（游戏侧属性，形如 "VisualTerrainBlock(Clone)"），
    /// 不依赖本程序集里的自定义类型——mod 程序集可能被游戏加载多份，类型身份对不上。
    /// </summary>
    public class LoadCleanupPatch : ILoadableSingleton
    {
        private const string HarmonyId = "jove.visualterrainblock.loadcleanup";
        private const string TemplateName = "VisualTerrainBlock";

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
                PatchValidate();
                PatchDeleteFinder();
            }
            catch (Exception exception)
            {
                Debug.LogError("[VisualTerrainBlock] 安装补丁失败: " + exception);
            }
        }

        private static void PatchValidate()
        {
            Type loaderType = Type.GetType("Timberborn.TerrainPhysics.TerrainPhysicsPostLoader, Timberborn.TerrainPhysics");
            MethodInfo target = FindMethod(loaderType, "Validate", 0);
            if (target == null)
            {
                Debug.LogError("[VisualTerrainBlock] 找不到 TerrainPhysicsPostLoader.Validate，补丁未安装");
                return;
            }

            if (AlreadyPatched(target))
            {
                return;
            }

            MethodInfo prefix = typeof(LoadCleanupPatch).GetMethod(nameof(SkipValidate),
                BindingFlags.Static | BindingFlags.NonPublic);
            new Harmony(HarmonyId).Patch(target, prefix: new HarmonyMethod(prefix));
            Debug.Log("[VisualTerrainBlock] 已跳过 TerrainPhysicsPostLoader.Validate（载入期不再清理无支撑结构）");
        }

        private static void PatchDeleteFinder()
        {
            Type finderType = Type.GetType("Timberborn.TerrainPhysics.TerrainAndBlockObjectsToDeleteFinder, Timberborn.TerrainPhysics");
            if (finderType == null)
            {
                Debug.LogWarning("[VisualTerrainBlock] 未找到 TerrainAndBlockObjectsToDeleteFinder");
                return;
            }

            // FindAll 有两个重载（首参分别是 IEnumerable<Vector3Int> 和 IEnumerable<BlockObject>），两个都打
            List<MethodInfo> targets = finderType
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(method =>
                {
                    if (method.Name != "FindAll")
                    {
                        return false;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    return parameters.Length == 3 && parameters[2].ParameterType == typeof(HashSet<BlockObject>);
                })
                .ToList();

            MethodInfo postfix = typeof(LoadCleanupPatch).GetMethod(nameof(StripOurBlocks),
                BindingFlags.Static | BindingFlags.NonPublic);
            foreach (MethodInfo target in targets.Where(target => !AlreadyPatched(target)))
            {
                new Harmony(HarmonyId).Patch(target, postfix: new HarmonyMethod(postfix));
                Debug.Log("[VisualTerrainBlock] 已给 FindAll 打补丁: "
                          + string.Join(", ", target.GetParameters().Select(parameter => parameter.ParameterType.Name)));
            }
        }

        private static MethodInfo FindMethod(Type type, string name, int parameterCount)
        {
            return type?
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(method => method.Name == name && method.GetParameters().Length == parameterCount);
        }

        private static bool AlreadyPatched(MethodInfo target)
        {
            Patches existing = Harmony.GetPatchInfo(target);
            return existing != null && existing.Owners.Contains(HarmonyId);
        }

        /// <summary>让载入期的"无支撑结构清理"整段不生效。</summary>
        private static bool SkipValidate(ref bool __result)
        {
            __result = true;
            return false;
        }

        private static void StripOurBlocks(object terrainToDelete, object terrainSet, HashSet<BlockObject> blockObjectsToDelete)
        {
            try
            {
                if (blockObjectsToDelete == null || blockObjectsToDelete.Count == 0)
                {
                    return;
                }

                int removed = blockObjectsToDelete.RemoveWhere(IsOurBlock);
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

        private static bool IsOurBlock(BlockObject blockObject)
        {
            if (blockObject == null)
            {
                return false;
            }

            string name;
            try
            {
                name = blockObject.Name;
            }
            catch (Exception)
            {
                return false;
            }

            return !string.IsNullOrEmpty(name) && name.StartsWith(TemplateName, StringComparison.Ordinal);
        }
    }
}
