using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEngine;

namespace Revolution.RevAsset.Tests
{
    /// <summary>验证目录、签名和提交的关键边界。原生制品与生命周期由独立的 Unity 流程验收。</summary>
    public sealed class RevAssetCatalogTests
    {
        /// <summary>断言实际闭包去重并保证深层依赖在前，标签选择继承相同的依赖。</summary>
        [Test]
        public void DependencyOrderAndTags()
        {
            var data = Catalog(Bundle("main", "right", "left"), Bundle("left", "base"), Bundle("right", "base"), Bundle("base"));
            data.bundles[0].tags = new[] { "intro" };
            var catalog = Parse(data);
            CollectionAssert.AreEqual(new[] { "base", "right", "left" }, catalog.GetDependencies("main"));
            Assert.That(catalog.SelectBundles(new[] { "intro" }).Count(), Is.EqualTo(4));
            Assert.That(catalog.Map.TryGetLocation("Panels/Home", out string bundle, out string asset), Is.True);
            Assert.That(bundle, Is.EqualTo("main"));
            Assert.That(asset, Is.EqualTo("Assets/Home.prefab"));
        }

        /// <summary>错误依赖和非法地址在安装之前失败。</summary>
        [Test]
        public void RefusesInvalidGraphAndPaths()
        {
            Assert.Throws<InvalidDataException>(() => Parse(Catalog(Bundle("main", "unknown"))));
            Assert.Throws<InvalidDataException>(() => Parse(Catalog(Bundle("main", "other"), Bundle("other", "main"))));
            var data = Catalog(Bundle("main"));
            data.assets[0].key = "Panels/../Home";
            Assert.Throws<InvalidDataException>(() => Parse(data));
            data.assets[0].key = "Res/Home";
            Assert.Throws<InvalidDataException>(() => Parse(data));
        }

        /// <summary>真实 RSA 签名绑定目录和发布序号，篡改任一份签发内容都无法使用。</summary>
        [Test]
        public void ChannelSignatureAndCatalogDigestAreRequired()
        {
            var settings = ScriptableObject.CreateInstance<RevAssetSettings>();
            try
            {
                using (var key = new RSACryptoServiceProvider(2048) { PersistKeyInCsp = false })
                {
                    RSAParameters publicKey = key.ExportParameters(false);
                    settings.PublicKey = Convert.ToBase64String(publicKey.Modulus) + ":" + Convert.ToBase64String(publicKey.Exponent);
                    byte[] bytes = Encode(Catalog(Bundle("main")));
                    RevAssetRelease release = Release(bytes);
                    release.Sign(key);
                    RevAssetRelease.Read(Encode(release), settings, "StandaloneWindows64").ReadCatalog(bytes);
                    release.revision++;
                    Assert.Throws<InvalidDataException>(() => RevAssetRelease.Read(Encode(release), settings, "StandaloneWindows64"));
                    bytes[bytes.Length / 2] ^= 1;
                    Assert.Throws<InvalidDataException>(() => release.ReadCatalog(bytes));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        /// <summary>生成具有固定身份的最小目录。</summary>
        private static RevAssetCatalogData Catalog(params RevAssetBundleData[] bundles)
        {
            return new RevAssetCatalogData
            {
                package = "main", compatibility = "1", platform = "StandaloneWindows64", version = "1", revision = 1,
                bundles = bundles,
                assets = new[] { new RevAssetAddressData { key = "Panels/Home", bundle = "main", asset = "Assets/Home.prefab" } }
            };
        }

        /// <summary>提供测试使用的合法包元数据。</summary>
        private static RevAssetBundleData Bundle(string name, params string[] dependencies)
        {
            return new RevAssetBundleData
            {
                name = name, hash = new string('a', 64), unityHash = new string('b', 32), size = 16,
                dependencies = dependencies, tags = Array.Empty<string>()
            };
        }

        /// <summary>生成与目录配套的未签发行记录。</summary>
        private static RevAssetRelease Release(byte[] bytes)
        {
            return new RevAssetRelease
            {
                package = "main", compatibility = "1", platform = "StandaloneWindows64", version = "1", revision = 1,
                catalog = RevAssetFiles.HashBytes(bytes), length = bytes.Length
            };
        }

        /// <summary>用运行时相同配置读取测试目录。</summary>
        private static RevAssetCatalog Parse(RevAssetCatalogData data)
        {
            return RevAssetCatalog.Parse(Encode(data), "main", "StandaloneWindows64", "1");
        }

        /// <summary>编码 Unity 的实际序列化结果。</summary>
        private static byte[] Encode(object value) => RevAssetFiles.Utf8.GetBytes(JsonUtility.ToJson(value));
    }
}
