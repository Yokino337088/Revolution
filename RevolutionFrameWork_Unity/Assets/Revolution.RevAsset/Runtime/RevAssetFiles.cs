using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Revolution.RevAsset
{
    /// <summary>统一发布目录、校验和同卷提交。原生包文件始终按内容摘要命名。</summary>
    internal static class RevAssetFiles
    {
        /// <summary>写入清单时使用无 BOM 的 UTF-8。</summary>
        internal static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        /// <summary>构建和运行时都使用同一组安全的路径段。</summary>
        internal static bool IsId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 96 || value[0] == '.' || value == "objects" || value == "catalogs")
            {
                return false;
            }
            foreach (char c in value)
            {
                if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-' || c == '_' || c == '.'))
                {
                    return false;
                }
            }
            return !value.EndsWith(".", StringComparison.Ordinal);
        }

        /// <summary>允许原生包以目录组织和点号变体。拒绝不安全的文件系统路径。</summary>
        internal static bool IsBundleName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 220 || name != name.ToLowerInvariant())
            {
                return false;
            }
            foreach (string segment in name.Split('/'))
            {
                if (!IsId(segment) || segment == "..")
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>判断字符串是否为标准小写 SHA-256。</summary>
        internal static bool IsHash(string value)
        {
            return IsHex(value, 64);
        }

        /// <summary>检查指定长度的小写十六进制值。</summary>
        internal static bool IsHex(string value, int length)
        {
            if (value == null || value.Length != length)
            {
                return false;
            }
            foreach (char c in value)
            {
                if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f'))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>地址由经过校验的摘要生成，频道文件没有自定义对象路径。</summary>
        internal static string ObjectName(string hash) => "objects/" + hash + ".bundle";

        /// <summary>目录文件也采用内容寻址，最新指针最后发布。</summary>
        internal static string CatalogName(string hash) => "catalogs/" + hash + ".json";

        /// <summary>原生首包在编辑器缓存中按内容包和平台分开，出包时只注入目标的文件。</summary>
        internal static string BuiltinEditorRoot(string package, string platform)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "RevAsset", "Builtin", package, platform));
        }

        /// <summary>以流式方式计算二进制文件摘要。内存与包大小无关。</summary>
        internal static string HashFile(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(stream));
            }
        }

        /// <summary>计算已在内存里的清单摘要。</summary>
        internal static string HashBytes(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(bytes));
            }
        }

        /// <summary>读取带上限的本地描述文件。</summary>
        internal static byte[] ReadBounded(string path, int limit)
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length == 0 || file.Length > limit)
            {
                throw new InvalidDataException("制品文件缺失或超限：" + path);
            }
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0 || bytes.Length > limit)
            {
                throw new InvalidDataException("制品文件读取期间发生变化：" + path);
            }
            return bytes;
        }

        /// <summary>只在内容和长度都正确时复用缓存中的包文件。</summary>
        internal static bool MatchesFile(string path, RevAssetBundleData bundle)
        {
            var file = new FileInfo(path);
            return file.Exists && file.Length == bundle.size && HashFile(path) == bundle.hash;
        }

        /// <summary>先写同目录的独立文件，最后以同卷原子替换提交。异常由调用者继续保留旧版本。</summary>
        internal static void WriteAtomic(string path, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = TemporarySibling(path);
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                PublishFile(temp, path);
            }
            finally
            {
                File.Delete(temp);
            }
        }

        /// <summary>短临时文件名与目标在同一卷，避免摘要和随机串叠加超出 Windows 路径长度。</summary>
        internal static string TemporarySibling(string path)
        {
            return Path.Combine(Path.GetDirectoryName(path), ".ra-" + Guid.NewGuid().ToString("N"));
        }

        /// <summary>把已校验的内容提交到正式文件名。发布前临时文件仍归下载任务管理。</summary>
        internal static void PublishFile(string source, string path)
        {
            if (File.Exists(path))
            {
                File.Replace(source, path, null);
            }
            else
            {
                File.Move(source, path);
            }
        }

        /// <summary>从 Unity 实际运行平台计算构建目标。编辑器读取本机包，与编辑器当前出包目标无关。</summary>
        internal static string PlayerPlatform()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                    return "StandaloneWindows64";
                case RuntimePlatform.WindowsPlayer:
                    return IntPtr.Size == 8 ? "StandaloneWindows64" : "StandaloneWindows";
                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.OSXPlayer:
                    return "StandaloneOSX";
                case RuntimePlatform.LinuxEditor:
                case RuntimePlatform.LinuxPlayer:
                    return "StandaloneLinux64";
                case RuntimePlatform.Android:
                    return "Android";
                case RuntimePlatform.IPhonePlayer:
                    return "iOS";
                case RuntimePlatform.WebGLPlayer:
                    return "WebGL";
                default:
                    return Application.platform.ToString();
            }
        }

        /// <summary>把摘要字节转为跨平台稳定的文件名。</summary>
        private static string ToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
