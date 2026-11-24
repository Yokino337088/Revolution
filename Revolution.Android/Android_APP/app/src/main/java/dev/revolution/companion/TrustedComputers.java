package dev.revolution.companion;

import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/** 仅在手机用户核对电脑后保存授权，私有设置内只写由 Android Keystore 加密的凭据。 */
final class TrustedComputers
{
    // 当前应用专用的 AES 密钥别名。
    private static final String ALIAS = "revolution-companion-computers-v1";
    // 每条记录以电脑证书的完整 SHA-256 为键，IP 或端口变化仍可找到同一台电脑。
    private static final String STORE = "trusted-computers";
    // AES-GCM 同时保护令牌内容和所绑定的证书指纹。
    private static final String ALGORITHM = "AES/GCM/NoPadding";

    /** 读取仅本 App 可访问、安装包已配置为不备份的授权记录。 */
    private static SharedPreferences entries(Context context)
    {
        return context.getSharedPreferences(STORE, Context.MODE_PRIVATE);
    }

    /** 使用系统密钥库的不可导出密钥，首次保存时创建，随后保持同一份。 */
    private static synchronized SecretKey deviceKey() throws Exception
    {
        KeyStore store = KeyStore.getInstance("AndroidKeyStore");
        store.load(null);
        SecretKey existing = (SecretKey)store.getKey(ALIAS, null);
        if (existing != null)
        {
            return existing;
        }
        KeyGenerator algorithm = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
        algorithm.init(new KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
            .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
            .setKeySize(256).build());
        return algorithm.generateKey();
    }

    /** 是否存在可清除的电脑授权，供手机界面决定是否展示入口。 */
    static boolean hasAny(Context context)
    {
        return !entries(context).getAll().isEmpty();
    }

    /** 只为确切匹配的电脑证书取令牌，失效记录清除后回到首次授权。 */
    static String read(Context context, String fingerprint)
    {
        String record = entries(context).getString(fingerprint, null);
        if (record == null)
        {
            return null;
        }
        try
        {
            String[] values = record.split("\\.", 2);
            byte[] iv = Base64.decode(values[0], Base64.NO_WRAP);
            Cipher cipher = Cipher.getInstance(ALGORITHM);
            cipher.init(Cipher.DECRYPT_MODE, deviceKey(), new GCMParameterSpec(128, iv));
            cipher.updateAAD(fingerprint.getBytes(StandardCharsets.US_ASCII));
            String token = new String(cipher.doFinal(Base64.decode(values[1], Base64.NO_WRAP)), StandardCharsets.US_ASCII);
            if (token.matches("[a-f0-9]{64}"))
            {
                return token;
            }
        }
        catch (Exception unreadable)
        {
            // 更换密钥或还原设备后，旧密文不能用作任何形式的认证。
        }
        forget(context, fingerprint);
        return null;
    }

    /** 第一次授权成功后落盘，提交完成才能提示下次可以直接连接。 */
    static boolean remember(Context context, String fingerprint, String token)
    {
        try
        {
            Cipher cipher = Cipher.getInstance(ALGORITHM);
            cipher.init(Cipher.ENCRYPT_MODE, deviceKey());
            cipher.updateAAD(fingerprint.getBytes(StandardCharsets.US_ASCII));
            String record = Base64.encodeToString(cipher.getIV(), Base64.NO_WRAP) + "." +
                Base64.encodeToString(cipher.doFinal(token.getBytes(StandardCharsets.US_ASCII)), Base64.NO_WRAP);
            return entries(context).edit().putString(fingerprint, record).commit();
        }
        catch (Exception unavailable)
        {
            return false;
        }
    }

    /** 电脑明确拒绝已保存凭据时，仅清除这台电脑的旧记录。 */
    static void forget(Context context, String fingerprint)
    {
        entries(context).edit().remove(fingerprint).apply();
    }

    /** 从手机侧忘记所有电脑，下次连接需要重新核对指纹。 */
    static void clear(Context context)
    {
        entries(context).edit().clear().apply();
    }
}
