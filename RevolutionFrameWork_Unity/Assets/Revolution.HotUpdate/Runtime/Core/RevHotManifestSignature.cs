using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Revolution.HotUpdate
{
    /// <summary>热更清单的发布签名。签名发生于发布机，客户端只配置经过信任分发的 RSA 公钥。</summary>
    public static class RevHotManifestSignature
    {
        /// <summary>签名记录必须是末行。旧清单读取器可以忽略此字段。</summary>
        private const string Marker = "\n@signature|rsa-sha256|";

        /// <summary>签名消息使用无 BOM 的 UTF-8。文本中的字段和换行顺序随完整清单一起验证。</summary>
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        /// <summary>由持有私钥的发布流程调用。返回可直接发布的带签名清单，私钥不进入游戏包。</summary>
        public static string Sign(string text, RSA signingKey)
        {
            string payload = Normalize(text);
            if (signingKey == null || signingKey.KeySize < 2048)
            {
                throw new ArgumentException("热更签名需要至少 2048 位 RSA 发布密钥", nameof(signingKey));
            }
            if (payload.Length == 0 || ("\n" + payload).Contains(Marker))
            {
                throw new InvalidDataException("热更签名需要非空且未签过的清单");
            }

            byte[] signature = signingKey.SignData(Utf8.GetBytes(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return payload + Marker + Convert.ToBase64String(signature) + "\n";
        }

        /// <summary>导出可写进客户端配置的公钥，格式为 Base64 模数与指数，用冒号分隔。</summary>
        public static string ExportPublicKey(RSA signingKey)
        {
            RSAParameters key = signingKey.ExportParameters(false);
            return Convert.ToBase64String(key.Modulus) + ":" + Convert.ToBase64String(key.Exponent);
        }

        /// <summary>使用固定公钥验证最后一行签名前的完整清单。验证失败或平台不支持时返回错误。</summary>
        public static string Verify(string text, string publicKey)
        {
            try
            {
                string payload = Normalize(text);
                int index = payload.IndexOf(Marker, StringComparison.Ordinal);
                int signatureStart = index + Marker.Length;
                if (index <= 0 || signatureStart >= payload.Length || payload.IndexOf('\n', signatureStart) >= 0)
                {
                    return "缺少唯一的末行清单签名";
                }

                string[] parts = (publicKey ?? string.Empty).Split(':');
                if (parts.Length != 2)
                {
                    return "客户端热更公钥格式无效";
                }

                var parameters = new RSAParameters
                {
                    Modulus = Convert.FromBase64String(parts[0]),
                    Exponent = Convert.FromBase64String(parts[1])
                };
                if (parameters.Modulus.Length < 256 || parameters.Exponent.Length == 0)
                {
                    return "客户端热更公钥长度无效";
                }

                byte[] signature = Convert.FromBase64String(payload.Substring(signatureStart));
                byte[] message = Utf8.GetBytes(payload.Substring(0, index));
                using (RSA rsa = RSA.Create())
                {
                    rsa.ImportParameters(parameters);
                    return rsa.VerifyData(message, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
                        ? null : "热更清单签名不匹配";
                }
            }
            catch (Exception exception) when (exception is ArgumentException || exception is FormatException ||
                exception is CryptographicException || exception is NotSupportedException)
            {
                return "无法验证热更清单签名：" + exception.Message;
            }
        }

        /// <summary>统一 BOM 与换行，让 HTTP 文本和文件读取获得相同签名消息。</summary>
        private static string Normalize(string text)
        {
            text = text ?? string.Empty;
            if (text.Length != 0 && text[0] == '\uFEFF')
            {
                text = text.Substring(1);
            }
            return text.Replace("\r\n", "\n").TrimEnd('\n');
        }
    }
}
