using System.Security.Cryptography;
using Revolution;
using Revolution.HotUpdate;

/// <summary>链接实际框架源码，检查更新清单和资源映射的纯数据契约。</summary>
static class Program
{
    static int _passed;
    /// <summary>运行既有清单回归和新增的发布签名及压缩映射边界。</summary>
    static void Main()
    {
        RevHotManifest manifest = NewManifest();
        Expect(manifest.ValidateForActivation("PC", "official") == null, "完整清单通过");
        Expect(manifest.ValidateForActivation("Android", "official") != null, "跨平台拒绝");
        Expect(manifest.ValidateForActivation("PC", "other") != null, "跨渠道拒绝");
        Expect(manifest.ValidateForActivation("PC", "official", "prod") != null, "跨环境拒绝");
        Expect(manifest.ValidateForActivation("PC", "official", "") == null, "同环境通过");

        manifest.Bundles[0].Dependencies.Add("missing");
        Expect(manifest.ValidateForActivation("PC", "official") != null, "缺依赖拒绝");
        manifest.Bundles[0].Dependencies.Clear();
        manifest.Bundles[0].Dependencies.Add("hero");
        Expect(manifest.ValidateForActivation("PC", "official") != null, "自依赖拒绝");
        manifest.Bundles[0].Dependencies.Clear();
        manifest.Bundles.Add(NewBundle("armor", 'c'));
        manifest.Bundles[0].Dependencies.Add("armor");
        manifest.Bundles[1].Dependencies.Add("hero");
        Expect(manifest.ValidateForActivation("PC", "official") != null, "依赖环拒绝");
        manifest.Bundles[1].Dependencies.Clear();
        Expect(manifest.ValidateForActivation("PC", "official") == null, "依赖闭包通过");
        manifest.Bundles.RemoveAt(1);
        manifest.Bundles[0].Dependencies.Clear();

        manifest.Bundles[0].Name = "../hero";
        Expect(manifest.ValidateForActivation("PC", "official") != null, "路径穿越拒绝");
        manifest.Bundles[0].Name = "hero";
        manifest.Bundles.Add(NewBundle("HERO", 'd'));
        Expect(manifest.ValidateForActivation("PC", "official") != null, "大小写重复名拒绝");
        manifest.Bundles.RemoveAt(1);
        manifest.Bundles[0].Sha256 = "";
        Expect(manifest.ValidateForActivation("PC", "official") != null, "空哈希拒绝");
        manifest.Bundles[0].Sha256 = new string('b', 64);
        manifest.HashAlgo = "md5";
        Expect(manifest.ValidateForActivation("PC", "official") != null, "不支持的校验算法拒绝");
        manifest.HashAlgo = "sha256";
        manifest.ResVersion = "../escape";
        Expect(manifest.ValidateForActivation("PC", "official") != null, "版本号路径穿越拒绝");
        manifest.ResVersion = "1.0.0.1";

        string text = manifest.Serialize();
        RevHotManifest parsed = RevHotManifest.Parse(text, out string error);
        Expect(parsed != null && error == null && parsed.ValidateForActivation("PC", "official") == null, "序列化往返验证");
        Expect(RevHotManifest.Parse(text.Replace("@bundleCount|1", "@bundleCount|2"), out _) == null, "截断清单拒绝");
        manifest.Platform = "WebGL";
        Expect(manifest.ValidateForActivation("WebGL", "official") != null, "WebGL 缺 Hash128 拒绝");
        manifest.Bundles[0].UnityHash = new string('f', 32);
        manifest.Bundles[0].UnityCrc = "1234";
        Expect(manifest.ValidateForActivation("WebGL", "official") == null, "WebGL 有效 Hash128/CRC 通过");
        CheckResMap();
        CheckSignature(text);
        Console.WriteLine($"RevHotUpdate / ResMap offline: {_passed} assertions passed");
    }

    /// <summary>覆盖产物损坏、路径歧义、UTF-8 和旧文本编码边界。</summary>
    static void CheckResMap()
    {
        const string text = "# 示例\nUI/设置|ui/shared|设置\nUI/房间|ui/shared|房间\n";
        byte[] packed = RevResMapCodec.Pack(text);
        Expect(RevResMapCodec.Unpack(packed) == text, "完整解压保留所有列及 Unicode");
        Expect(packed.SequenceEqual(RevResMapCodec.Pack("\uFEFF" + text.Replace("\n", "\r\n"))), "BOM 和换行由构建统一");
        Expect(RevResMapCodec.ParseText(text)["UI/设置"] == "ui/shared|设置", "旧文本也走严格查询");
        // 类型化索引需要保留原表结果、大小写和已选定的版本，归并字符串仅服务当前快照。
        RevResMap locations = RevResMap.Parse(text, bundle => bundle == "ui/shared");
        Expect(locations.Count == 2 && locations.TryGetLocation("UI/设置", out string bundleName, out string assetName) &&
            bundleName == "ui/shared" && assetName == "设置", "直接取包名和资源名");
        Expect(locations.TryGetLocation("UI/设置", out var firstBundle, out _) &&
            locations.TryGetLocation("UI/房间", out var secondBundle, out _) && ReferenceEquals(firstBundle, secondBundle), "相同包名只留一份");
        Expect(!locations.TryGetLocation("ui/设置", out var missBundle, out var missAsset) &&
            missBundle == null && missAsset == null, "未命中保持逻辑路径原有大小写");
        RevResMap next = RevResMap.Parse("UI/设置|ui/new.hd|设置别名");
        Expect(next.TryGetLocation("UI/设置", out var nextBundle, out var nextAsset) &&
            nextBundle == "ui/new.hd" && nextAsset == "设置别名" &&
            locations.TryGetLocation("UI/设置", out var oldBundle, out _) && oldBundle == "ui/shared", "新旧快照独立保留各自的地址");
        IgnoreBadMap(() => RevResMap.Parse(text, _ => false), "类型化热更拒绝候选清单外的包");
        IgnoreBadMap(() => RevResMap.Parse(text + "UI/设置|ui/other|A\n"), "类型化索引也拒绝重复逻辑键");
        IgnoreBadMap(() => RevResMapCodec.Unpack(packed[..^1]), "截断 gzip 尾部拒绝");
        IgnoreBadMap(() => RevResMapCodec.Unpack(packed.Concat(new byte[1]).ToArray()), "追加正文拒绝");
        byte[] modified = (byte[])packed.Clone();
        modified[^1] ^= 1;
        IgnoreBadMap(() => RevResMapCodec.Unpack(modified), "同长度修改摘要不匹配");
        byte[] inflated = (byte[])packed.Clone();
        inflated[11] = 127;
        IgnoreBadMap(() => RevResMapCodec.Unpack(inflated), "超限声明拒绝");
        byte[] smaller = (byte[])packed.Clone();
        smaller[8]--;
        IgnoreBadMap(() => RevResMapCodec.Unpack(smaller), "声明长度偏小拒绝");
        IgnoreBadMap(() => RevResMapCodec.Pack(text + "UI/设置|ui/new|设置\n"), "重复逻辑键拒绝");
        IgnoreBadMap(() => RevResMapCodec.ParseText("UI/A|../bad|A"), "包路径跳转拒绝");
        IgnoreBadMap(() => RevResMapCodec.ParseText("UI/A|https%3A/pvp|A"), "编码路径歧义拒绝");
        IgnoreBadMap(() => RevResMapCodec.ParseText("UI/A|ok|A|extra"), "多余列拒绝");
        IgnoreBadMap(() => RevResMapCodec.DecodeText(new byte[] { 0xE9, 0x83 }), "截断 UTF-8 拒绝");
    }

    /// <summary>验证已签清单、改动内容、缺失签名和提供错误公钥四种结果。</summary>
    static void CheckSignature(string text)
    {
        using var signer = RSA.Create(2048);
        using var other = RSA.Create(2048);
        string publicKey = RevHotManifestSignature.ExportPublicKey(signer);
        string signed = RevHotManifestSignature.Sign(text, signer);
        Expect(RevHotManifestSignature.Verify(signed, publicKey) == null, "真实 RSA 发布清单验签通过");
        Expect(RevHotManifestSignature.Verify("\uFEFF" + signed.Replace("\n", "\r\n"), publicKey) == null, "传输换行规范化后仍可验签");
        Expect(RevHotManifestSignature.Verify(signed.Replace("@channel|official", "@channel|other"), publicKey) != null,
            "改渠道破坏签名");
        Expect(RevHotManifestSignature.Verify(text, publicKey) != null, "配置公钥时旧清单不能冒充带签版本");
        Expect(RevHotManifestSignature.Verify(signed, RevHotManifestSignature.ExportPublicKey(other)) != null, "其他发布者签名拒绝");
        Expect(RevHotManifestSignature.Verify(signed + "@channel|other\n", publicKey) != null, "禁止在签名后拼接额外字段");
        Expect(RevHotManifest.Parse(signed, out _) != null, "旧版清单解析器可忽略新增签名记录");
    }

    /// <summary>要求底层明确拒绝损坏的映射数据。</summary>
    static void IgnoreBadMap(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            Expect(true, message);
            return;
        }
        Expect(false, message);
    }

    /// <summary>创建可用于清单校验的最小包记录。</summary>
    static RevHotBundleInfo NewBundle(string name, char hash)
        => new() { Name = name, Sha256 = new string(hash, 64), Size = 10 };

    static RevHotManifest NewManifest()
    {
        var manifest = new RevHotManifest { AppVersion = "1.0.0", ResVersion = "1.0.0.1", Platform = "PC", Channel = "official" };
        manifest.ResMap = new RevHotFileInfo { Name = "ResMap.txt", Sha256 = new string('a', 64), Size = 13 };
        manifest.Bundles.Add(NewBundle("hero", 'b'));
        return manifest;
    }

    static void Expect(bool result, string message)
    {
        if (!result) throw new Exception("断言失败：" + message);
        _passed++;
    }
}
