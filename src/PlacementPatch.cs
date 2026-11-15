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
    /// 放置阶段的物理支撑校验在游戏里是"全局"的，不挂在建筑蓝图上的
    /// TerrainPhysicsBlockObjectValidatorSpec 上：
    ///   放置工具（Application 程序集）→ ITerrainPhysicsService.ValidateBlockObjectPreview(BlockObject)
    /// 所以删掉蓝图里的 spec 只能绕过"落地时"的那层校验，光标预览仍会被拦：
    /// 叠在已有方块上（下方有东西）判定通过，往旁边伸进空气里判定失败 —— 表现为"只能往上建"。
    ///
    /// 这里对 ValidateBlockObjectPreview 打 prefix：预览对象是我们的悬空地形块时直接判通过。
    /// 顺带把 CanBeDestroyed 也护住，避免地形变化（炸药、挖掘）时物理系统顺手删掉周边的悬空块。
    /// </summary>
    public class PlacementPatch : ILoadableSingleton
    {
        private const string HarmonyId = "jove.visualterrainblock.placement";
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
                Type serviceType = Type.GetType("Timberborn.TerrainPhysics.TerrainPhysicsService, Timberborn.TerrainPhysics");
                if (serviceType == null)
                {
                    Debug.LogWarning("[VisualTerrainBlock] 找不到 TerrainPhysicsService，跳过放置补丁");
                    return;
                }

                PatchMethod(serviceType, "ValidateBlockObjectPreview", nameof(AllowPreview));
                PatchMethod(serviceType, "CanBeDestroyed", nameof(ProtectFromPhysicsDeletion));
            }
            catch (Exception exception)
            {
                Debug.LogError("[VisualTerrainBlock] 放置补丁安装失败: " + exception);
            }
        }

        private static void PatchMethod(Type type, string methodName, string prefixName)
        {
            MethodInfo target = type
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(method =>
                {
                    if (method.Name != methodName)
                    {
                        return false;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    return parameters.Length == 1 && parameters[0].ParameterType == typeof(BlockObject);
                });
            if (target == null)
            {
                Debug.LogWarning("[VisualTerrainBlock] 找不到 " + type.Name + "." + methodName + "(BlockObject)，跳过");
                return;
            }

            Patches existing = Harmony.GetPatchInfo(target);
            if (existing != null && existing.Owners.Contains(HarmonyId))
            {
                return;
            }

            MethodInfo prefix = typeof(PlacementPatch).GetMethod(prefixName, BindingFlags.Static | BindingFlags.NonPublic);
            new Harmony(HarmonyId).Patch(target, prefix: new HarmonyMethod(prefix));
            Debug.Log("[VisualTerrainBlock] 已给 " + type.Name + "." + methodName + " 打补丁");
        }

        /// <summary>光标预览：我们的方块不需要物理支撑。</summary>
        private static bool AllowPreview(BlockObject __0, ref bool __result)
        {
            if (!IsOurBlock(__0))
            {
                return true;
            }

            __result = true;
            return false;
        }

        /// <summary>物理清理：不要因为"没支撑"删掉我们的方块。</summary>
        private static bool ProtectFromPhysicsDeletion(BlockObject __0, ref bool __result)
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
