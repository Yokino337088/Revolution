using UnityEngine;

namespace Revolution.RevAsset
{
    /// <summary>随 Player 发布的 RevAsset 启动配置。编辑器的分包规则单独存于 ProjectSettings。</summary>
    public sealed class RevAssetSettings : ScriptableObject
    {
        /// <summary>RevAsset 启动配置的 Resources 地址。</summary>
        public const string ResourcePath = "Revolution/RevAsset/Settings";

        /// <summary>更新、缓存和构建目录使用的内容包名。</summary>
        public string Package = "main";

        /// <summary>应用兼容代号。资源协议或脚本发生不兼容变化时提高版本。</summary>
        public string Compatibility = "1";

        /// <summary>编辑器 Play 使用分包规则直接在 AssetDatabase 取资源。</summary>
        public bool EditorSimulation = true;

        /// <summary>首选 HTTPS 发布目录。留空时使用已安装版本或内置首包。</summary>
        public string ChannelUrl = "";

        /// <summary>首屏出现后后台检查签名发布，下载完成后等待安全交接。</summary>
        public bool CheckOnStart;

        /// <summary>发布公钥，格式为 Base64 模数:Base64 指数。</summary>
        public string PublicKey = "";
    }
}
