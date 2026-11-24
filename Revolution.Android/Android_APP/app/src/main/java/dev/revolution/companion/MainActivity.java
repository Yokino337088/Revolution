package dev.revolution.companion;

import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
import android.content.ActivityNotFoundException;
import android.content.ClipData;
import android.content.Intent;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.view.View;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.EditText;
import android.widget.RadioButton;
import android.widget.TextView;
import java.util.List;
import java.util.Locale;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/** 手机操作入口；包选择和测试授权来自可见界面，游戏进入前台后由服务持有连接。 */
public final class MainActivity extends Activity
{
    // 系统文档选择器的回执编号。
    private static final int FILE = 10;
    // 系统安装器的回执编号。
    private static final int INSTALL = 11;
    // 返回未知来源设置后再次核对实际授权。
    private static final int SETTINGS = 12;
    // 只在界面可见时读取服务状态。
    private final Handler main = new Handler(Looper.getMainLooper());
    // 包清单和大 APK 的读取放在后台，避免阻塞输入。
    private final ExecutorService files = Executors.newSingleThreadExecutor();
    // 仅将用户选择的包名写入偏好设置，凭据不落盘。
    private String selected = "";
    // 显示当前已选包及其系统应用名称。
    private TextView game;
    // 连接方式的操作指引，可复制到电脑。
    private TextView instructions;
    // 会话实时状态和最近的结束原因。
    private TextView status;
    // 只在用户确认电脑前显示证书指纹。
    private TextView pairing;
    // 在准备连接与启动游戏两个用户操作之间切换。
    private Button start;
    // 主动结束本轮连接。
    private Button stop;
    // 局域网使用电脑地址，USB 通过 ADB reverse 使用回环地址。
    private RadioButton lan;
    // 电脑服务端在当前局域网内的地址，允许指定非默认端口。
    private EditText desktop;
    // 电脑签发的短期配对码，输入后清空，不写入保存状态。
    private EditText code;
    // 业务存档隔离由所选游戏实现并在计划中核对。
    private CheckBox isolate;
    // 文档选择器临时授予的 APK 地址，安装结束或取消后释放引用。
    private Uri pendingUri;
    // 从 APK 内读取的包名，用来核实安装回执。
    private String pendingPackage;
    // APK 声明的版本号，用于判断安装器是否实际交付新版本。
    private long pendingVersion;
    // 当前正在读取文件或加载应用清单，避免重入。
    private boolean loading;
    // 仅在状态或设置变化时刷新视图和无障碍播报。
    private String rendered = "";

    /** 绑定原生控件并恢复非敏感选择，旋转屏幕后重用仍在运行的测试服务。 */
    @Override public void onCreate(Bundle state)
    {
        super.onCreate(state);
        setContentView(R.layout.main);
        game = findViewById(R.id.game);
        instructions = findViewById(R.id.instructions);
        status = findViewById(R.id.status);
        pairing = findViewById(R.id.pair);
        start = findViewById(R.id.start);
        stop = findViewById(R.id.stop);
        lan = findViewById(R.id.lan);
        desktop = findViewById(R.id.address);
        desktop.setText(getPreferences(0).getString("host", ""));
        code = findViewById(R.id.code);
        isolate = findViewById(R.id.isolate);
        selected = getPreferences(0).getString("package", "");
        boolean wifi = getPreferences(0).getBoolean("lan", true);
        ((RadioButton)findViewById(wifi ? R.id.lan : R.id.usb)).setChecked(true);
        isolate.setChecked(getPreferences(0).getBoolean("isolate", true));
        if (state != null)
        {
            pendingUri = state.getParcelable("uri");
            pendingPackage = state.getString("apkPackage");
            pendingVersion = state.getLong("apkVersion");
        }
        // 启动游戏必须来自当前可见 Activity 的用户点击。
        findViewById(R.id.pick).setOnClickListener(v -> chooseInstalled());
        findViewById(R.id.file).setOnClickListener(v -> chooseFile());
        start.setOnClickListener(v -> startOrLaunch());
        stop.setOnClickListener(v -> {
            BridgeService.lastMessage = "已断开连接";
            stopService(new Intent(this, BridgeService.class));
        });
        lan.setOnCheckedChangeListener((button, checked) -> {
            getPreferences(0).edit().putBoolean("lan", checked).apply();
            render();
        });
        isolate.setOnCheckedChangeListener((button, checked) ->
            getPreferences(0).edit().putBoolean("isolate", checked).apply());
        showSelection();
        render();
    }

    /** 安装器或系统设置跨屏幕后仍能核对最初选择的文件。 */
    @Override public void onSaveInstanceState(Bundle state)
    {
        super.onSaveInstanceState(state);
        state.putParcelable("uri", pendingUri);
        state.putString("apkPackage", pendingPackage);
        state.putLong("apkVersion", pendingVersion);
    }

    /** 返回测试台后读取当前服务阶段。 */
    @Override public void onStart()
    {
        super.onStart();
        main.post(tick);
    }

    /** 游戏处于前台时暂停界面轮询，网络连接继续由服务维护。 */
    @Override public void onStop()
    {
        main.removeCallbacks(tick);
        super.onStop();
    }

    /** Activity 退出时释放文件工作线程，晚到的回执会检查界面寿命。 */
    @Override public void onDestroy()
    {
        files.shutdownNow();
        super.onDestroy();
    }

    // 界面刷新时钟，只在可见期间注册。
    private final Runnable tick = new Runnable()
    {
        /** 服务变化时更新一次可见状态。 */
        @Override public void run()
        {
            render();
            main.postDelayed(this, 400);
        }
    };

    /** 展示选择文件或启动失败的具体原因。 */
    private void message(String reason)
    {
        if (!isFinishing() && !isDestroyed())
        {
            new AlertDialog.Builder(this).setMessage(reason).setPositiveButton("知道了", null).show();
        }
    }

    /** 选择已安装包后保存身份并刷新版本显示。 */
    void selectGame(String packageId)
    {
        selected = packageId;
        getPreferences(0).edit().putString("package", selected).apply();
        showSelection();
        rendered = "";
        render();
    }

    /** 查安装管理器获取当前版本，目标卸载时要求重新选择。 */
    private void showSelection()
    {
        try
        {
            PackageInfo info = getPackageManager().getPackageInfo(selected, 0);
            game.setText(getString(R.string.selected_game, info.applicationInfo.loadLabel(getPackageManager()), info.versionName, selected));
        }
        catch (PackageManager.NameNotFoundException missing)
        {
            selected = "";
            game.setText(R.string.select_hint);
        }
    }

    /** 先在后台读取包，再交给可见的原生列表选择。 */
    private void chooseInstalled()
    {
        loading = true;
        render();
        files.execute(() -> {
            List<GamePicker.Entry> entries = GamePicker.installed(this);
            runOnUiThread(() -> {
                loading = false;
                if (!isDestroyed() && !isFinishing())
                {
                    render();
                    GamePicker.show(this, entries);
                }
            });
        });
    }

    /** 向系统请求单个 APK 的临时读取权限。 */
    private void chooseFile()
    {
        Intent open = new Intent(Intent.ACTION_OPEN_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE)
            .setType("application/vnd.android.package-archive");
        try
        {
            startActivityForResult(open, FILE);
        }
        catch (ActivityNotFoundException missing)
        {
            message("设备缺少系统文件选择器");
        }
    }

    /** 接收文件选择、安装授权和系统安装结果；再次从系统核对实际包版本。 */
    @Override protected void onActivityResult(int code, int result, Intent data)
    {
        super.onActivityResult(code, result, data);
        if (code == FILE && result == RESULT_OK && data != null && data.getData() != null)
        {
            inspect(data.getData());
        }
        else if (code == SETTINGS && pendingUri != null)
        {
            if (getPackageManager().canRequestPackageInstalls())
            {
                install();
            }
            else
            {
                pendingUri = null;
                message("系统没有授予本次 APK 安装权限");
            }
        }
        else if (code == INSTALL)
        {
            boolean ready = result == RESULT_OK && installedVersion();
            pendingUri = null;
            if (ready)
            {
                selectGame(pendingPackage);
            }
            else
            {
                message("所选 APK 版本尚未安装");
            }
        }
    }

    /** 比较实际已装版本和启动入口，取消安装不能误选旧包。 */
    private boolean installedVersion()
    {
        if (pendingPackage == null)
        {
            return false;
        }
        try
        {
            return GamePicker.version(getPackageManager().getPackageInfo(pendingPackage, 0)) == pendingVersion &&
                getPackageManager().getLaunchIntentForPackage(pendingPackage) != null;
        }
        catch (PackageManager.NameNotFoundException missing)
        {
            return false;
        }
    }

    /** 从系统文件流识别包及版本，安装动作必须由用户经系统安装器确认。 */
    private void inspect(Uri uri)
    {
        loading = true;
        render();
        files.execute(() -> {
            try
            {
                PackageInfo info = GamePicker.archive(this, uri);
                runOnUiThread(() -> archiveReady(uri, info));
            }
            catch (Exception error)
            {
                runOnUiThread(() -> {
                    loading = false;
                    render();
                    message("读取 APK 失败：" + error.getMessage());
                });
            }
        });
    }

    /** 同包同版本可能是两次构建，允许选择已安装版本或将所选文件送交系统安装器。 */
    private void archiveReady(Uri uri, PackageInfo info)
    {
        loading = false;
        if (isDestroyed() || isFinishing())
        {
            return;
        }
        render();
        if (getPackageName().equals(info.packageName))
        {
            message("请选择要测试的游戏 APK");
            return;
        }
        pendingUri = uri;
        pendingPackage = info.packageName;
        pendingVersion = GamePicker.version(info);
        if (installedVersion())
        {
            new AlertDialog.Builder(this).setTitle("手机已安装相同版本号")
                .setMessage(pendingPackage + "\n版本 " + info.versionName + "\n同一版本号可以对应不同的测试构建。")
                .setNegativeButton("取消", (dialog, which) -> pendingUri = null)
                .setNeutralButton("安装所选 APK", (dialog, which) -> install())
                .setPositiveButton("使用已安装版本", (dialog, which) -> {
                    pendingUri = null;
                    selectGame(pendingPackage);
                }).show();
            return;
        }
        new AlertDialog.Builder(this).setTitle("安装所选游戏")
            .setMessage(info.packageName + "\n版本 " + info.versionName + "\n安装完成后将使用这个包的启动入口。")
            .setNegativeButton("取消", (dialog, which) -> pendingUri = null)
            .setPositiveButton("打开系统安装器", (dialog, which) -> install()).show();
    }

    /** 将所选 URI 的只读权限授予系统安装器，系统保留签名、替换和用户确认检查。 */
    private void install()
    {
        try
        {
            if (!getPackageManager().canRequestPackageInstalls())
            {
                startActivityForResult(new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,
                    Uri.parse("package:" + getPackageName())), SETTINGS);
                return;
            }
            Intent install = new Intent(Intent.ACTION_INSTALL_PACKAGE).setData(pendingUri)
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION).putExtra(Intent.EXTRA_RETURN_RESULT, true);
            install.setClipData(ClipData.newRawUri("APK", pendingUri));
            startActivityForResult(install, INSTALL);
        }
        catch (RuntimeException error)
        {
            pendingUri = null;
            message("无法打开系统安装器：" + error.getMessage());
        }
    }

    /** 点击依次发起出站连接、确认电脑指纹和启动游戏，凭据不进入偏好设置。 */
    private void startOrLaunch()
    {
        PhoneSession session = BridgeService.current;
        if (session != null)
        {
            if (session.phase == PhoneSession.Phase.CONFIRM)
            {
                session.confirm();
            }
            else
            {
                session.launch(this);
            }
            return;
        }
        String key = code.getText().toString();
        String host = lan.isChecked() ? desktop.getText().toString().trim() : "127.0.0.1:18767";
        if (!key.matches("[0-9]{8}") || host.isEmpty())
        {
            message("请填写电脑接线器显示的地址和八位配对码");
            return;
        }
        if (lan.isChecked())
        {
            getPreferences(0).edit().putString("host", host).apply();
        }
        if (getPackageManager().getLaunchIntentForPackage(selected) == null)
        {
            showSelection();
            message("游戏启动入口已失效，请重新选择");
            return;
        }
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) !=
            PackageManager.PERMISSION_GRANTED && !getPreferences(0).getBoolean("askedNotice", false))
        {
            getPreferences(0).edit().putBoolean("askedNotice", true).apply();
            requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, 13);
        }
        startForegroundService(new Intent(this, BridgeService.class).putExtra("package", selected)
            .putExtra("address", host).putExtra("code", key).putExtra("isolate", isolate.isChecked()));
        code.setText("");
        start.setEnabled(false);
    }

    /** 根据阶段显示下一步、配对材料和已验证的构建，离开配对状态立即隐藏短码。 */
    private void render()
    {
        PhoneSession session = BridgeService.current;
        PhoneSession.Phase phase = session == null ? PhoneSession.Phase.CLOSED : session.phase;
        boolean active = session != null;
        String key = phase + "/" + active + "/" + lan.isChecked() + "/" + loading + "/" + selected + "/" + BridgeService.lastMessage;
        if (key.equals(rendered))
        {
            return;
        }
        rendered = key;
        for (int id : new int[]{R.id.pick, R.id.file, R.id.isolate, R.id.usb, R.id.lan})
        {
            findViewById(id).setEnabled(!active && !loading);
        }
        desktop.setVisibility(!active && lan.isChecked() ? View.VISIBLE : View.GONE);
        code.setVisibility(active ? View.GONE : View.VISIBLE);
        instructions.setText(active ? session.address : getString(lan.isChecked() ? R.string.lan_hint : R.string.usb_hint));
        String detail = BridgeService.lastMessage;
        start.setText(R.string.pair_start);
        pairing.setVisibility(View.GONE);
        stop.setVisibility(active ? View.VISIBLE : View.GONE);
        start.setEnabled((!active && !selected.isEmpty() && !loading) || phase == PhoneSession.Phase.PAIRED ||
            phase == PhoneSession.Phase.CONFIRM);
        switch (phase)
        {
            case CONNECTING: detail = "正在连接电脑，读取证书…"; break;
            case CONFIRM:
                detail = "请与电脑窗口的指纹逐位核对。";
                pairing.setText(getString(R.string.pair_details, session.fingerprint.substring(0, 16).toUpperCase(Locale.ROOT)));
                pairing.setVisibility(View.VISIBLE);
                start.setText(R.string.verify_computer);
                break;
            case PAIRING: detail = "正在与电脑配对…"; break;
            case PAIRED:
                detail = "电脑已配对。若游戏已经打开，请先从游戏内退出，再开始测试。";
                start.setText(R.string.game_start);
                break;
            case STARTING: detail = "等待游戏冷启动及身份验证…"; break;
            case READY:
                detail = "Agent 已可接入\n" + session.packageId + "\nBuild " + session.identity.optString("buildId");
                break;
            default: break;
        }
        status.setText(loading ? "正在读取应用信息…" : detail);
        start.setVisibility(active && phase != PhoneSession.Phase.PAIRED && phase != PhoneSession.Phase.CONFIRM ?
            View.GONE : View.VISIBLE);
    }
}
