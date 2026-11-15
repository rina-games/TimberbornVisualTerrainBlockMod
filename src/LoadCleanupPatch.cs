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
    ///   2) 拆除/删除工具也走 TerrainAndBlockObjectsToDeleteFinder.FindAll：
    ///      玩家选中的方块已经在输入集合里，随后物理系统再往同一份集合里追加"连带该塌的"。
    ///      只能摘掉连带追加的悬空地形块。输入里原本就有的必须留下，
    ///      否则未完工的工地被选中后集合被清空，确认框不出现，表现就是"选中了没反应"。
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

            MethodInfo stripAll = typeof(LoadCleanupPatch).GetMethod(nameof(StripOurBlocks),
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo rememberSelected = typeof(LoadCleanupPatch).GetMethod(nameof(RememberSelected),
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo stripUnselected = typeof(LoadCleanupPatch).GetMethod(nameof(StripUnselected),
                BindingFlags.Static | BindingFlags.NonPublic);
            foreach (MethodInfo target in targets.Where(target => !AlreadyPatched(target)))
            {
                bool playerSelection = target.GetParameters()[0].ParameterType == typeof(IEnumerable<BlockObject>);
                if (playerSelection)
                {
                    new Harmony(HarmonyId).Patch(target,
                        prefix: new HarmonyMethod(rememberSelected),
                        postfix: new HarmonyMethod(stripUnselected));
                    Debug.Log("[VisualTerrainBlock] 已给 FindAll(BlockObject) 打补丁（保留玩家选中的悬空地形块，只摘连带删除）");
                }
                else
                {
                    new Harmony(HarmonyId).Patch(target, postfix: new HarmonyMethod(stripAll));
                    Debug.Log("[VisualTerrainBlock] 已给 FindAll 打补丁: "
                              + string.Join(", ", target.GetParameters().Select(parameter => parameter.ParameterType.Name)));
                }
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

        /// <summary>
        /// 让载入期的"无支撑结构清理"整段不生效。
        /// 注意返回值语义：ValidateAll 是 while (Validate()) 循环——返回 true 会无限循环（实测卡在加载界面），
        /// 所以这里必须返回 false（= "没有需要清理的东西了"）。
        /// </summary>
        private static bool SkipValidate(ref bool __result)
        {
            __result = false;
            return false;
        }

        /// <summary>
        /// 拆除工具把选中的方块直接放进 FindAll 的输入。先记下这些方块，postfix 里不要摘掉。
        /// </summary>
        private static void RememberSelected(IEnumerable<BlockObject> __0, out HashSet<BlockObject> __state)
        {
            __state = new HashSet<BlockObject>();
            if (__0 == null)
            {
                return;
            }

            foreach (BlockObject blockObject in __0)
            {
                if (IsOurBlock(blockObject))
                {
                    __state.Add(blockObject);
                }
            }
        }

        /// <summary>
        /// 玩家选中的悬空地形块留在删除列表里；物理系统事后追加的连带删除仍然摘掉。
        /// </summary>
        private static void StripUnselected(HashSet<BlockObject> __2, HashSet<BlockObject> __state)
        {
            try
            {
                if (__2 == null || __2.Count == 0)
                {
                    return;
                }

                __2.RemoveWhere(blockObject =>
                    IsOurBlock(blockObject) && (__state == null || !__state.Contains(blockObject)));
            }
            catch (Exception exception)
            {
                Debug.LogError("[VisualTerrainBlock] 清理删除列表时出错: " + exception);
            }
        }

        private static void StripOurBlocks(HashSet<BlockObject> __2)
        {
            try
            {
                HashSet<BlockObject> blockObjectsToDelete = __2;
                if (blockObjectsToDelete == null || blockObjectsToDelete.Count == 0)
                {
                    return;
                }

                blockObjectsToDelete.RemoveWhere(IsOurBlock);
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
