using System;
using System.Collections.Generic;
using UnityEditor;

namespace Revolution.RevAsset.Editor
{
    /// <summary>资源如何进入 Unity 的 AssetBundleBuild 输入。</summary>
    public enum RevAssetPacking
    {
        /// <summary>优先沿用 AssetImporter 的包名与变体，未标记的按所在目录合包。</summary>
        NativeOrFolder,
        /// <summary>同一规则匹配的资源合成自定义包。</summary>
        Together,
        /// <summary>资源按实际目录合包。</summary>
        ByFolder,
        /// <summary>每份资源独立成包。</summary>
        ByAsset,
        /// <summary>路径从公开地址排除。Unity 实际需要的依赖仍可进入包。</summary>
        Exclude
    }

    /// <summary>路径选择、地址、原生包粒度和部署标签的编辑器规则。</summary>
    [Serializable]
    public sealed class RevAssetBuildRule
    {
        /// <summary>Assets 下的文件或目录。覆盖时最具体路径胜出。</summary>
        public string Path = "Assets/GameRes";

        /// <summary>当前规则采用的分包方式。</summary>
        public RevAssetPacking Packing;

        /// <summary>合包方式的实际包名，可包含目录层级。</summary>
        public string Bundle = "";

        /// <summary>合包方式的原生 AB 变体后缀。</summary>
        public string Variant = "";

        /// <summary>文件规则的精确地址，目录规则的逻辑前缀。空值按工程资源根目录推导。</summary>
        public string Address = "";

        /// <summary>首包和预下载可使用这些标签。</summary>
        public string[] Tags = { "core" };
    }

    /// <summary>项目可版本化的 RevAsset 构建规则。私钥仅在当前发布任务传入。</summary>
    [FilePath("ProjectSettings/RevAssetBuild.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class RevAssetBuildProfile : ScriptableSingleton<RevAssetBuildProfile>
    {
        /// <summary>生成默认逻辑路径时去掉该资源根目录。</summary>
        public string AddressRoot = "Assets/GameRes";

        /// <summary>显式分包规则，默认扫描常用项目资源根目录。</summary>
        public List<RevAssetBuildRule> Rules = new List<RevAssetBuildRule> { new RevAssetBuildRule() };

        /// <summary>自动为多个包共同使用但未明确归属的资产建立共享依赖包。</summary>
        public bool ExtractSharedDependencies = true;

        /// <summary>原生 AB 压缩选项，默认 LZ4 分块读取。</summary>
        public bool Uncompressed;

        /// <summary>允许只含指定标签及其依赖的首包。留空带齐全部内容。</summary>
        public string[] BuiltinTags = Array.Empty<string>();

        /// <summary>保存工程级规则，供界面、RevCLI 和 CI 共同读取。</summary>
        public void SaveProfile()
        {
            Save(true);
        }
    }
}
