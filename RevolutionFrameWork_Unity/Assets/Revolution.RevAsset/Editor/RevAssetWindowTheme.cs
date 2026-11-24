using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Revolution.RevAsset.Editor
{
    /// <summary>RevAsset 工作台的配色和样式。主题资源只在窗口绘制期间存活。</summary>
    internal sealed class RevAssetWindowTheme : IDisposable
    {
        /// <summary>当前 Unity 编辑器的明暗皮肤，切换后由窗口重新创建样式。</summary>
        internal readonly bool IsDark;

        /// <summary>窗体留白的底色。</summary>
        internal readonly Color Background;

        /// <summary>规则列表和结果表中用于区分相邻行的色块。</summary>
        internal readonly Color AlternateRow;

        /// <summary>顶部标题区的底板。</summary>
        internal readonly GUIStyle Header;

        /// <summary>表单区块的底板与统一间距。</summary>
        internal readonly GUIStyle Card;

        /// <summary>窗口名称的排版。</summary>
        internal readonly GUIStyle Title;

        /// <summary>顶部深色底板内的说明文字。</summary>
        internal readonly GUIStyle HeaderNote;

        /// <summary>区块标题的排版。</summary>
        internal readonly GUIStyle Heading;

        /// <summary>无须吸引注意的补充信息，可随窗口缩放换行。</summary>
        internal readonly GUIStyle Note;

        /// <summary>表格行内容，长路径由原生标签裁剪并可用提示查看。</summary>
        internal readonly GUIStyle Cell;

        /// <summary>静态后端状态。</summary>
        internal readonly GUIStyle Badge;

        /// <summary>导航与表单的次级操作。</summary>
        internal readonly GUIStyle QuietButton;

        /// <summary>标题内的帮助入口。</summary>
        internal readonly GUIStyle HeaderButton;

        /// <summary>当前页签。</summary>
        internal readonly GUIStyle ActiveTab;

        /// <summary>用于预览、构建与继续的主操作。</summary>
        internal readonly GUIStyle PrimaryButton;

        /// <summary>由此主题创建的临时纹理，关闭窗口时逐个销毁。</summary>
        private readonly List<Texture2D> _textures = new List<Texture2D>();

        /// <summary>建立明暗两套与 Unity 输入框兼容的外观，必须从 OnGUI 调用。</summary>
        internal RevAssetWindowTheme(bool dark)
        {
            IsDark = dark;
            Background = dark ? Rgb(32, 36, 45) : Rgb(237, 241, 246);
            AlternateRow = dark ? Rgb(47, 52, 63) : Rgb(238, 242, 247);
            Color text = dark ? Rgb(233, 238, 246) : Rgb(28, 39, 54);
            Color muted = dark ? Rgb(173, 186, 205) : Rgb(66, 81, 102);
            Color quiet = dark ? Rgb(51, 59, 73) : Rgb(221, 227, 236);
            Color quietHover = dark ? Rgb(59, 70, 89) : Rgb(206, 218, 232);

            Header = Panel(Rgb(28, 42, 67), 18);
            Card = Panel(dark ? Rgb(41, 47, 58) : Color.white, 16);
            Title = Text(Rgb(248, 251, 255), 26, true);
            HeaderNote = Text(Rgb(181, 201, 227), 12);
            Heading = Text(text, 16, true);
            Note = Text(muted, 12);
            Note.wordWrap = true;
            Cell = Text(text, 12);
            Badge = Button(Rgb(48, 65, 92), Rgb(48, 65, 92), Rgb(224, 237, 255), 26);
            HeaderButton = Button(Rgb(42, 57, 84), Rgb(52, 70, 101), Rgb(233, 241, 254), 26);
            QuietButton = Button(quiet, quietHover, text, 29);
            ActiveTab = Button(dark ? Rgb(61, 79, 114) : Rgb(207, 225, 250),
                dark ? Rgb(73, 95, 139) : Rgb(193, 215, 247), dark ? Rgb(235, 244, 255) : Rgb(21, 58, 112), 29);
            PrimaryButton = Button(Rgb(41, 89, 168), Rgb(51, 105, 189), Color.white, 32);
        }

        /// <summary>创建有固定圆角与内边距的底板，布局组件负责拉伸中间部分。</summary>
        private GUIStyle Panel(Color fill, int padding)
        {
            return new GUIStyle
            {
                normal = { background = Surface(fill) },
                padding = new RectOffset(padding, padding, padding, padding),
                border = new RectOffset(7, 7, 7, 7),
                stretchWidth = true
            };
        }

        /// <summary>让普通按钮、当前页签和主操作保留独立的指针与按下颜色。</summary>
        private GUIStyle Button(Color fill, Color hover, Color text, int height)
        {
            var style = Text(text, 12, true);
            style.alignment = TextAnchor.MiddleCenter;
            style.fixedHeight = height;
            style.padding = new RectOffset(14, 14, 0, 0);
            style.border = new RectOffset(7, 7, 7, 7);
            style.normal.background = Surface(fill);
            style.hover = new GUIStyleState { background = Surface(hover), textColor = text };
            style.active = new GUIStyleState { background = Surface(Color.Lerp(hover, Color.black, .13f)), textColor = text };
            style.focused = style.hover;
            return style;
        }

        /// <summary>统一字号、颜色和裁剪规则。原生输入框继续沿用 Unity 的皮肤。</summary>
        private static GUIStyle Text(Color color, int size, bool bold = false)
        {
            return new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = color },
                fontSize = size,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                clipping = TextClipping.Clip
            };
        }

        /// <summary>生成一张可九宫格拉伸的圆角底纹。所有像素只在主题切换时计算一次。</summary>
        private Texture2D Surface(Color fill)
        {
            const int size = 18;
            const float radius = 6f;
            var image = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0f, Mathf.Abs(x + .5f - size * .5f) - (size * .5f - radius));
                    float dy = Mathf.Max(0f, Mathf.Abs(y + .5f - size * .5f) - (size * .5f - radius));
                    Color pixel = fill;
                    pixel.a *= Mathf.Clamp01(radius + .5f - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * size + x] = pixel;
                }
            }
            image.SetPixels32(pixels);
            image.Apply(false, true);
            _textures.Add(image);
            return image;
        }

        /// <summary>按编辑器颜色表读出不透明色。</summary>
        private static Color Rgb(byte r, byte g, byte b) => new Color32(r, g, b, 255);

        /// <summary>释放当前窗体创建的临时图形资源。</summary>
        public void Dispose()
        {
            foreach (Texture2D texture in _textures)
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
            _textures.Clear();
        }
    }
}
