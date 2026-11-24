using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace Revolution.RevAsset
{
    /// <summary>可变的 CDN 频道指针。签名绑定整个清单及内容包、平台、兼容代号和序号。</summary>
    [Serializable]
    internal sealed class RevAssetRelease
    {
        /// <summary>防止为失控的入口响应分配大块内存。</summary>
        internal const int MaxBytes = 16 * 1024;

        /// <summary>发布指针的固定文件名。</summary>
        internal const string FileName = "channel.json";

        /// <summary>指针格式版本。</summary>
        public int format = 1;
        /// <summary>资源包名。</summary>
        public string package;
        /// <summary>目标平台。</summary>
        public string platform;
        /// <summary>应用兼容代号。</summary>
        public string compatibility;
        /// <summary>可读内容版本名。</summary>
        public string version;
        /// <summary>用于阻止旧频道误覆盖新安装的发布序号。</summary>
        public long revision;
        /// <summary>清单字节摘要。</summary>
        public string catalog;
        /// <summary>清单长度。</summary>
        public int length;
        /// <summary>对规范化字段的 RSA-SHA256 签名。</summary>
        public string signature;

        /// <summary>解析并核对身份，随后用 Player 内置公钥验证发布者。</summary>
        internal static RevAssetRelease Read(byte[] bytes, RevAssetSettings settings, string target)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxBytes)
            {
                throw new InvalidDataException("发布指针缺失或超过上限");
            }

            var info = JsonUtility.FromJson<RevAssetRelease>(RevResMapCodec.DecodeText(bytes));
            if (info == null || info.format != 1 || info.package != settings.Package || info.platform != target ||
                info.compatibility != settings.Compatibility || !RevAssetFiles.IsId(info.version) || info.revision < 1 ||
                !RevAssetFiles.IsHash(info.catalog) || info.length <= 0 || info.length > RevAssetCatalog.MaxBytes)
            {
                throw new InvalidDataException("发布指针的包、平台、兼容代号或目录属性无效");
            }

            string[] parts = (settings.PublicKey ?? string.Empty).Trim().Split(':');
            if (parts.Length != 2 || string.IsNullOrEmpty(info.signature))
            {
                throw new InvalidDataException("热更新需要受信公钥与发布签名");
            }
            var key = new RSAParameters
            {
                Modulus = Convert.FromBase64String(parts[0]),
                Exponent = Convert.FromBase64String(parts[1])
            };
            if (key.Modulus.Length < 256 || key.Exponent.Length == 0)
            {
                throw new InvalidDataException("热更新公钥需要至少 2048 位 RSA");
            }
            using (RSA rsa = RSA.Create())
            {
                rsa.ImportParameters(key);
                if (!rsa.VerifyData(info.Message(), Convert.FromBase64String(info.signature),
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                {
                    throw new InvalidDataException("发布指针签名验证失败");
                }
            }
            return info;
        }

        /// <summary>发布私钥只由构建机持有。裸清单供本机首包，远端安装必须验证签名。</summary>
        internal void Sign(RSA key)
        {
            if (key.ExportParameters(false).Modulus.Length < 256)
            {
                throw new InvalidDataException("签名密钥需要至少 2048 位 RSA");
            }
            signature = Convert.ToBase64String(key.SignData(Message(), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }

        /// <summary>核对传输内容再构造只读目录，并检查指针与目录的版本一致。</summary>
        internal RevAssetCatalog ReadCatalog(byte[] bytes)
        {
            if (bytes == null || bytes.Length != length || RevAssetFiles.HashBytes(bytes) != catalog)
            {
                throw new InvalidDataException("清单字节数或 SHA-256 不匹配");
            }
            RevAssetCatalog result = RevAssetCatalog.Parse(bytes, package, platform, compatibility);
            if (result.Version != version || result.Revision != revision)
            {
                throw new InvalidDataException("发布指针与清单版本不一致");
            }
            return result;
        }

        /// <summary>签名以固定字段顺序和分隔符编码。身份和内容摘要都进入签名。</summary>
        private byte[] Message()
        {
            return RevAssetFiles.Utf8.GetBytes(FormattableString.Invariant(
                $"Revolution.RevAsset/1\n{package}\n{platform}\n{compatibility}\n{version}\n{revision}\n{catalog}\n{length}"));
        }
    }
}
