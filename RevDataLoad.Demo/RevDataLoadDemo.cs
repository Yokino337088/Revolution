// ============================================================
// RevDataLoadDemo.cs —— 配置表（数据装载）开箱示例
//
// 位置：Assets\Revolution.Demo\RevDataLoad.Demo\
//
// 【它解决什么】
//   配置表 = 一种资源：导表工具生成"结构体 + 容器"，数据是纯文本 TXT，
//   运行时按逻辑路径 Data/<表名> 从资源系统读，零反射、零装箱。
//
// 【怎么用】打开配套场景 RevDataLoadDemo.unity → 点 Play → 左侧按钮逐个点。
//
// 【本示例演示什么】
//   ① 生成的容器长什么样：RevDemoHeroTable 继承 RevDataTable<int, RevDemoHero>，
//      只实现 GetKey / ParseRow 两个方法（查询能力全部继承自基类）；
//   ② 文本装载：LoadText 按行解析（# 注释行跳过、制表符分列、单行出错不中断整表、主键重复跳过）；
//   ③ 查询：FindByKey（主键 O(1)）/ GetByIndex（按行遍历）/ Count / All；
//   ④ 真实项目的装载入口：DataTableManager.LoadAsync（走资源系统 —— 本示例因为不依赖工程配置，
//      用内嵌文本直接演示 LoadText；真实项目里这段文本由资源系统从 Data/<表名> 拿到）。
//
// 【与导表工具的关系】
//   下面 ①② 的代码与数据 txt 通常由导表工具生成 —— 这里手写一份等价物，
//   让你不开导表工具也能看懂"生成物到底长什么样"。
// ============================================================
using System.Collections.Generic;
using UnityEngine;

namespace Revolution.Demo.DataLoad
{
    // ---------- ① 数据结构：一行数据长什么样（导表工具生成；struct 值类型零 GC） ----------
    [System.Serializable]
    public struct RevDemoHero
    {
        public int Id;              // 英雄ID（主键）
        public string Name;         // 英雄名
        public int Quality;         // 品质(1-5)
        public float Attack;        // 攻击力
        public bool IsRanged;       // 是否远程
    }

    // ---------- ① 容器：一张表怎么组织与查询（导表工具生成；查询能力继承自基类） ----------
    public sealed class RevDemoHeroTable : Revolution.RevDataTable<int, RevDemoHero>
    {
        public RevDemoHeroTable() : base("RevDemoHero") { }          // 表名 = 数据文件名（默认 Data/RevDemoHero）

        /// <summary>主键怎么取（Excel 的第一个字段）</summary>
        protected override int GetKey(in RevDemoHero data) => data.Id;

        /// <summary>一行文本怎么解析成数据（生成器按字段类型写死直接赋值 —— 零反射、零装箱）</summary>
        protected override bool ParseRow(string[] cells, out RevDemoHero data)
        {
            data = default;

            if (cells.Length < 5) return false;                       // 列数不够 = 数据与结构不匹配，按坏行处理

            data.Id       = Revolution.RevDataFieldParser.ToInt(cells[0]);    // 容错：解析失败取默认值
            data.Name     = Revolution.RevDataFieldParser.ToStr(cells[1]);
            data.Quality  = Revolution.RevDataFieldParser.ToInt(cells[2]);
            data.Attack   = Revolution.RevDataFieldParser.ToFloat(cells[3]);
            data.IsRanged = Revolution.RevDataFieldParser.ToBool(cells[4]);
            return true;
        }
    }

    // ==================== 演示入口 ====================

    public sealed class RevDataLoadDemo : MonoBehaviour
    {
        /// <summary>一段数据文本（真实项目里它躺在 Data/RevDemoHero.txt，由资源系统按逻辑路径读出来）。</summary>
        private const string DemoText =
            "#RevDemoHero\n" +
            "#ID\tName\tQuality\tAttack\tIsRanged\n" +
            "#英雄ID\t英雄名\t品质(1-5)\t攻击力\t是否远程\n" +
            "1001\t亚瑟\t5\t120.5\tfalse\n" +
            "1002\t妲己\t3\t95\ttrue\n" +
            "1003\t后羿\t4\t160.8\ttrue\n" +
            "\n" +                                                   // 空行跳过
            "#1004\t坏行示例\t不是数字\t0\tfalse\n" +                  // ← 整行是注释，不会解析
            "1002\t重复主键\t1\t10\tfalse\n" +                        // ← 主键重复：跳过（保留先出现的）
            "1005\t鲁班七号\t2\t88.8\ttrue\n";

        private readonly List<string> _ui = new List<string>();
        private Vector2 _scroll;

        private readonly RevDemoHeroTable _table = new RevDemoHeroTable();

        private void Ui(string line)
        {
            _ui.Add(line);
            if (_ui.Count > 200) _ui.RemoveAt(0);
            _scroll.y = float.MaxValue;
        }

        // ==================== OnGUI ====================

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 450, Screen.height - 20));
            GUILayout.Label("<b>RevDataLoad 配置表演示</b>", TitleStyle());

            // ② 装载：LoadText 按行解析
            if (GUILayout.Button("① LoadText：装载内嵌的演示数据"))
            {
                int loaded = _table.LoadText(DemoText, out int errors);
                Ui($"装载完成：成功 {loaded} 行，坏行 {errors} 个（1 个主键重复被跳过；注释行与空行整行跳过）。");
                Ui("单行出错不中断整张表 —— 一张表的笔误不该让整个游戏起不来（导表工具会在生成时校验，这里是最后防线）。");
            }

            // ③ 查询：主键 O(1)
            if (GUILayout.Button("② FindByKey(1001)：按主键查一条"))
            {
                if (_table.FindByKey(1001, out RevDemoHero hero))
                    Ui($"查到 {hero.Id}：{hero.Name}　品质 {hero.Quality}　攻击 {hero.Attack}　远程 {hero.IsRanged}");
                else
                    Ui("没装载或没有这条（先点 ①）。");
            }

            // ③ 遍历：按下标走整张表（顺序 = 数据文件行顺序）
            if (GUILayout.Button("③ 遍历全部（GetByIndex / All）"))
            {
                foreach (RevDemoHero h in _table.All)
                    Ui($"  [{h.Id}] {h.Name}（{h.Quality}★）攻击 {h.Attack}");
                Ui($"共 {_table.Count} 行；Keys 集合={string.Join(",", _table.Keys)}（调试 / 校验用）。");
            }

            // ④ 真实项目的装载入口：DataTableManager（走资源系统）
            if (GUILayout.Button("④ 真实入口：DataTableManager（说明见日志）"))
            {
                var table = Revolution.RevDataTableManager.Get<RevDemoHeroTable>();
                Ui(table == null
                    ? "DataTableManager 里还没有这张表 —— 真实项目用 await RevDataTableManager.LoadAsync<RevDemoHeroTable>()；" +
                      "它把 Data/RevDemoHero.txt 从资源系统拿文本，再调本示例演示的 LoadText 装载。" +
                      "（本示例为不依赖工程配置，用内嵌文本直接演示装载核心。）"
                    : $"已加载（{_table.Count} 行）—— Unload<RevDemoHeroTable>() 可卸载并归还资源引用。");
            }

            // ⑤ 卸载：清空已装载数据（容器实例保留，可再次装载）
            if (GUILayout.Button("⑤ Clear：清空已装载数据"))
            {
                _table.Clear();
                Ui($"已清空（Count = {_table.Count}）。容器实例保留，可再次装载。");
            }

            GUILayout.Space(6);
            if (GUILayout.Button("清空演示日志")) _ui.Clear();

            GUILayout.Label("格式契约：一行一条、制表符分列、# 开头整行注释、四字符转义 —— 写端（导表工具）与读端（运行时）共用同一份实现。", HintStyle());
            GUILayout.FlexibleSpace();
            GUILayout.EndArea();

            // ---------- 右侧：演示日志 ----------
            GUILayout.BeginArea(new Rect(Screen.width - 480, 10, 470, Screen.height - 20));
            GUILayout.Label("<b>演示步骤日志</b>", TitleStyle());
            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (string line in _ui) GUILayout.Label(line);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ---------- 简单样式 ----------
        private static GUIStyle _title, _hint;
        private static GUIStyle TitleStyle() => _title ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15 };
        private static GUIStyle HintStyle() => _hint ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 11, wordWrap = true };
    }
}
