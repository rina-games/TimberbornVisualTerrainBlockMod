using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Timberborn.BlockSystem;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace Jove.VisualTerrainBlock
{
    /// <summary>
    /// 拆除工具选不中我们的方块/工地。
    ///
    /// 原因：`EntityBlockObjectDeletionTool.IsBlockObjectValid(blockObject)` 的实现就是
    /// `!IsStackedDeletionBlocked(blockObject)`，而 `IsStackedDeletionBlocked` 会问这个方块身上的
    /// `IBlockObjectDeletionBlocker`（`_deletionBlockers`）—— 我们的悬空地形块被判定为"被挡住"，
    /// 于是选不中、也不会弹二次确认框。
    ///
    /// 这里对 `IsStackedDeletionBlocked` 打 prefix：对象名以 VisualTerrainBlock 开头时直接返回 false
    /// （= 没被挡住），让拆除工具能像普通建筑一样选中并删除。
    /// </summary>
    public class DeletionPatch : ILoadableSingleton
    {
        private const string HarmonyId = "jove.visualterrainblock.deletion";
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
                Type toolType = Type.GetType(
                    "Timberborn.BlockObjectTools.EntityBlockObjectDeletionTool, Timberborn.BlockObjectTools");
                if (toolType == null)
                {
                    Debug.LogWarning("[VisualTerrainBlock] 找不到 EntityBlockObjectDeletionTool，跳过拆除补丁");
                    return;
                }

                MethodInfo target = toolType
                    .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .FirstOrDefault(method =>
                    {
                        if (method.Name != "IsStackedDeletionBlocked")
                        {
                            return false;
                        }

                        ParameterInfo[] parameters = method.GetParameters();
                        return parameters.Length == 1 && parameters[0].ParameterType == typeof(BlockObject);
                    });
                if (target == null)
                {
                    Debug.LogWarning("[VisualTerrainBlock] 找不到 IsStackedDeletionBlocked(BlockObject)，跳过拆除补丁");
                    return;
                }

                Patches existing = Harmony.GetPatchInfo(target);
                if (existing != null && existing.Owners.Contains(HarmonyId))
                {
                    return;
                }

                MethodInfo prefix = typeof(DeletionPatch).GetMethod(nameof(AllowDeletion),
                    BindingFlags.Static | BindingFlags.NonPublic);
                new Harmony(HarmonyId).Patch(target, prefix: new HarmonyMethod(prefix));
                Debug.Log("[VisualTerrainBlock] 已给 EntityBlockObjectDeletionTool.IsStackedDeletionBlocked 打补丁（拆除工具能选中悬空地形块）");
            }
            catch (Exception exception)
            {
                Debug.LogError("[VisualTerrainBlock] 拆除补丁安装失败: " + exception);
            }
        }

        private static bool AllowDeletion(BlockObject __0, ref bool __result)
        {
            if (!IsOurBlock(__0))
            {
                return true;
            }

            __result = false;
            return false;
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
