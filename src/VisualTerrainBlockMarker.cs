using Timberborn.BaseComponentSystem;
using Timberborn.BlueprintSystem;

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
    /// 纯标记组件：让载入时的地形物理校验能认出"这是我们这个悬空地形块"。
    /// 组件本身没有逻辑，唯一作用是给实体挂一个可判定的类型。
    /// </summary>
    public class VisualTerrainBlockMarker : BaseComponent
    {
    }

    public record VisualTerrainBlockMarkerSpec : ComponentSpec
    {
    }
}
