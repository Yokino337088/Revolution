package dev.revolution.companion;

import android.app.AlertDialog;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.ApplicationInfo;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.content.pm.ResolveInfo;
import android.net.Uri;
import android.os.Build;
import android.widget.ArrayAdapter;
import android.widget.CheckBox;
import android.widget.LinearLayout;
import android.widget.ListView;
import android.widget.SearchView;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;

/** 使用系统公开的启动入口和文件选择器，最终框架身份由游戏的运行时回执确认。 */
final class GamePicker
{
    // 仅记录通过游戏握手的包及其安装更新时间，升级后的包须重新验证。
    private static final String VERIFIED = "verified-games";

    /** 列表保留包名供启动，显示名仅用于辨认同名游戏。 */
    static final class Entry
    {
        // 系统报告的已安装包标识。
        final String id;
        // 桌面应用名称。
        final String label;
        // 游戏类别或 Unity 元数据只用于排在前面。
        final boolean game;
        // 当前安装版本已经在本机通过过 Revolution 运行时身份核验。
        final boolean verified;

        /** 保存一次查询快照，启动前还会重新解析入口。 */
        Entry(String id, String label, boolean game, boolean verified)
        {
            this.id = id;
            this.label = label;
            this.game = game;
            this.verified = verified;
        }

        /** 让同名应用和自定义 Unity Activity 也能按包名搜索。 */
        @Override public String toString()
        {
            return label + (verified ? "  ·  已验证 Revolution" : "") + "\n" + id;
        }
    }

    /** 握手成功后记录安装时间，以便列表下次优先展示同一版本。 */
    static void remember(Context context, String id)
    {
        try
        {
            long updated = context.getPackageManager().getPackageInfo(id, 0).lastUpdateTime;
            context.getSharedPreferences(VERIFIED, 0).edit().putLong(id, updated).apply();
        }
        catch (PackageManager.NameNotFoundException removed)
        {
            // 在回执和查询之间卸载的包不获得已验证标记。
        }
    }

    /** 枚举能从桌面启动的应用，优先展示系统游戏分类和 Unity 入口。 */
    static List<Entry> installed(Context context)
    {
        PackageManager pm = context.getPackageManager();
        SharedPreferences verifiedGames = context.getSharedPreferences(VERIFIED, 0);
        Intent intent = new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER);
        Set<String> seen = new HashSet<>();
        List<Entry> entries = new ArrayList<>();
        for (ResolveInfo item : pm.queryIntentActivities(intent, PackageManager.GET_META_DATA))
        {
            String id = item.activityInfo.packageName;
            if (id.equals(context.getPackageName()) || !seen.add(id))
            {
                continue;
            }
            ApplicationInfo app = item.activityInfo.applicationInfo;
            boolean candidate = app.category == ApplicationInfo.CATEGORY_GAME ||
                (app.flags & ApplicationInfo.FLAG_IS_GAME) != 0 ||
                item.activityInfo.name.startsWith("com.unity3d.") ||
                (item.activityInfo.metaData != null && item.activityInfo.metaData.getBoolean("unityplayer.UnityActivity"));
            boolean verified = false;
            if (verifiedGames.contains(id))
            {
                try
                {
                    verified = verifiedGames.getLong(id, -1) == pm.getPackageInfo(id, 0).lastUpdateTime;
                }
                catch (PackageManager.NameNotFoundException removed)
                {
                    continue;
                }
            }
            entries.add(new Entry(id, item.loadLabel(pm).toString(), candidate, verified));
        }
        entries.sort(Comparator.comparing((Entry e) -> !e.verified).thenComparing(e -> !e.game).thenComparing(e -> e.label)
            .thenComparing(e -> e.id));
        return entries;
    }

    /** 在原生弹窗中按名称或包名过滤，选中后回到当前会话设置。 */
    static void show(MainActivity activity, List<Entry> choices)
    {
        LinearLayout panel = new LinearLayout(activity);
        panel.setOrientation(LinearLayout.VERTICAL);
        SearchView search = new SearchView(activity);
        search.setIconifiedByDefault(false);
        search.setQueryHint("搜索游戏名称或包名");
        CheckBox all = new CheckBox(activity);
        all.setText(R.string.show_other_apps);
        all.setChecked(choices.stream().noneMatch(entry -> entry.game || entry.verified));
        ListView list = new ListView(activity);
        panel.addView(search);
        panel.addView(all);
        panel.addView(list);
        ArrayAdapter<Entry> adapter = new ArrayAdapter<>(activity, android.R.layout.simple_list_item_1,
            new ArrayList<>());
        list.setAdapter(adapter);
        Runnable filter = () -> {
            String text = search.getQuery().toString().trim().toLowerCase(Locale.ROOT);
            adapter.clear();
            for (Entry entry : choices)
            {
                if ((all.isChecked() || entry.verified || entry.game) &&
                    entry.toString().toLowerCase(Locale.ROOT).contains(text))
                {
                    adapter.add(entry);
                }
            }
        };
        filter.run();
        all.setOnCheckedChangeListener((view, checked) -> filter.run());
        AlertDialog dialog = new AlertDialog.Builder(activity).setTitle("选择测试游戏")
            .setView(panel).setNegativeButton("取消", null).create();
        search.setOnQueryTextListener(new SearchView.OnQueryTextListener()
        {
            /** 键盘搜索键收起输入焦点，保留当前筛选。 */
            @Override public boolean onQueryTextSubmit(String query)
            {
                search.clearFocus();
                return true;
            }

            /** 匹配完整显示名和包名中的任意片段，同时保留候选排序。 */
            @Override public boolean onQueryTextChange(String query)
            {
                filter.run();
                return true;
            }
        });
        // 使用筛选后列表的位置，避免同名或筛选后启动错误应用。
        list.setOnItemClickListener((parent, view, position, index) -> {
            activity.selectGame(adapter.getItem(position).id);
            dialog.dismiss();
        });
        dialog.show();
    }

    /** 通过 SAF 授权的流读取元信息，系统解析器需要文件路径，临时副本在所有出口删除。 */
    static PackageInfo archive(Context context, Uri uri) throws IOException
    {
        File temp = File.createTempFile("selected-", ".apk", context.getCacheDir());
        try
        {
            try (InputStream in = context.getContentResolver().openInputStream(uri);
                 FileOutputStream out = new FileOutputStream(temp))
            {
                if (in == null)
                {
                    throw new IOException("文件提供方未返回 APK 内容");
                }
                byte[] block = new byte[65536];
                int count;
                while ((count = in.read(block)) != -1)
                {
                    out.write(block, 0, count);
                }
            }
            PackageInfo info = context.getPackageManager().getPackageArchiveInfo(temp.getPath(), 0);
            if (info == null)
            {
                throw new IOException("无法识别该文件，请选择可单独安装的 APK");
            }
            return info;
        }
        finally
        {
            temp.delete();
        }
    }

    /** 比较设备上真实的版本号，兼容旧系统的 32 位字段。 */
    static long version(PackageInfo info)
    {
        return Build.VERSION.SDK_INT >= 28 ? info.getLongVersionCode() : info.versionCode;
    }
}
