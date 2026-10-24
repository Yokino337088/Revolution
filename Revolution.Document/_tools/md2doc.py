# -*- coding: utf-8 -*-
"""把《自动化工具》下的 Markdown 生成同款样式的 HTML。

为什么要有这个脚本：
    这两份文档的 HTML 与 MD 必须保持一致。手改 HTML 容易走样、也容易忘记同步；
    统一从 MD 生成，样式取自同目录最接近的现有文档（内联 CSS），改一次两边都对。

用法：
    python Revolution.Document/_tools/md2doc.py
    （在仓库根目录执行；也可以只改 DOCS 里的 target 生成单篇）
"""
import io
import os
import re

DOC_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "自动化工具")
HEAD_SOURCE = os.path.join(DOC_DIR, "RevMCP使用说明.html")

DOCS = [
    dict(md="Revolution自动化工具使用说明.md", html="Revolution自动化工具使用说明.html",
         kicker="REVOLUTION · AUTOMATION CLI",
         tagline="让 AI 驱动 Unity：你提需求，AI 执行命令、读结果、给结论",
         meta="更新：2026-10-24。适用：Python 3.10+ / Unity 2022.3，无需第三方库。"),
    dict(md="RevMCP使用说明.md", html="RevMCP使用说明.html",
         kicker="REVOLUTION · AI AUTOMATION",
         tagline="你说话，AI 动手：发现接口、执行、断言、截图与清理",
         meta="更新：2026-10-24。适用：Python 3.10+、Unity 2022.3；需在 AI 客户端配置一次 MCP。"),
]


def inline(text):
    text = text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
    codes = []

    def keep(m):
        codes.append(m.group(1))
        return "\x00%d\x00" % (len(codes) - 1)

    text = re.sub(r"`([^`]+)`", keep, text)
    text = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", r'<a href="\2">\1</a>', text)
    text = re.sub(r"\*\*([^*]+)\*\*", r"<strong>\1</strong>", text)
    for i, code in enumerate(codes):
        body = code.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
        text = text.replace("\x00%d\x00" % i, "<code>" + body + "</code>")
    return text


def render_table(rows):
    head = [c.strip() for c in rows[0].strip().strip("|").split("|")]
    out = ['<div class="table-wrap"><table>', "<thead>", "<tr>"]
    out += ["<th>%s</th>" % inline(c) for c in head]
    out += ["</tr>", "</thead>", "<tbody>"]
    for row in rows[2:]:
        cells = [c.strip() for c in row.strip().strip("|").split("|")]
        out.append("<tr>" + "".join("<td>%s</td>" % inline(c) for c in cells) + "</tr>")
    out += ["</tbody></table></div>"]
    return "\n".join(out)


def render_md(md_text):
    lines = md_text.splitlines()
    title, lead, toc, sections = "", [], [], []
    current = None
    i, n = 0, len(lines)

    while i < n:
        s = lines[i].strip()

        if s.startswith("# ") and not title:
            title = s[2:].strip()
            i += 1
            continue

        if s.startswith("## "):
            current = (s[3:].strip(), [])
            sections.append(current)
            toc.append(s[3:].strip())
            i += 1
            continue

        if s.startswith("### "):
            if current:
                current[1].append("<h3>%s</h3>" % inline(s[4:].strip()))
            i += 1
            continue

        if s.startswith("```"):
            i += 1
            buf = []
            while i < n and not lines[i].strip().startswith("```"):
                buf.append(lines[i])
                i += 1
            i += 1
            code = "\n".join(buf).replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
            if current:
                current[1].append('<pre><code class="language-text">%s</code></pre>' % code)
            continue

        if s.startswith("|") and i + 1 < n and set(lines[i + 1].strip()) <= set("|-: "):
            rows = []
            while i < n and lines[i].strip().startswith("|"):
                rows.append(lines[i])
                i += 1
            if current:
                current[1].append(render_table(rows))
            continue

        if s.startswith(">"):
            buf = []
            while i < n and lines[i].strip().startswith(">"):
                buf.append(lines[i].strip().lstrip(">").strip())
                i += 1
            block = '<div class="callout good"><p>%s</p></div>' % inline(" ".join(x for x in buf if x))
            (current[1] if current else lead).append(block)
            continue

        if s.startswith("- ") or s.startswith("* ") or re.match(r"^\d+\.\s", s):
            ordered = bool(re.match(r"^\d+\.\s", s))
            items = []
            while i < n:
                cur = lines[i].strip()
                if cur.startswith("- ") or cur.startswith("* "):
                    items.append(cur[2:].strip())
                elif re.match(r"^\d+\.\s", cur):
                    items.append(re.sub(r"^\d+\.\s", "", cur))
                else:
                    break
                i += 1
            tag = "ol" if ordered else "ul"
            html = "<%s>\n%s\n</%s>" % (tag, "\n".join("<li>%s</li>" % inline(x) for x in items), tag)
            if current:
                current[1].append(html)
            continue

        if s == "":
            i += 1
            continue

        buf = []
        while i < n and lines[i].strip() and not re.match(r"^(#{1,3} |```|\||>|[-*] |\d+\.\s)", lines[i].strip()):
            buf.append(lines[i])
            i += 1
        if buf and current:
            current[1].append("<p>" + inline(" ".join(x.strip() for x in buf)) + "</p>")

    return title, lead, toc, sections


def main():
    head = io.open(HEAD_SOURCE, encoding="utf-8").read()
    head = head[:head.index("</style>") + len("</style>")] + "</head><body>\n"

    for doc in DOCS:
        md = io.open(os.path.join(DOC_DIR, doc["md"]), encoding="utf-8").read()
        title, lead, toc, sections = render_md(md)
        parts = [head,
                 '<header class="hero"><div class="wrap"><span class="kicker">%s</span><h1>%s</h1><p>%s</p></div></header>'
                 % (doc["kicker"], inline(title), doc["tagline"]),
                 "<main>",
                 '<section class="toc"><h2>目录</h2><ol>%s</ol><p>%s</p>%s</section>'
                 % ("".join('<li><a href="#s%d">%s</a></li>' % (i + 1, inline(t)) for i, t in enumerate(toc)),
                    inline(doc["meta"]), ("\n" + "\n".join(lead)) if lead else "")]
        for i, (sec, blocks) in enumerate(sections, 1):
            parts.append('<section id="s%d"><h2>%s</h2>\n%s\n</section>' % (i, inline(sec), "\n".join(blocks)))
        parts.append("</main>")
        parts.append('<footer>Revolution · 2026-10-24<br><a href="../index.html">文档总入口</a> · '
                     '<a href="%s">Markdown 源文档</a></footer>\n</body></html>\n' % doc["md"])
        out = "\n".join(parts)
        io.open(os.path.join(DOC_DIR, doc["html"]), "w", encoding="utf-8", newline="\n").write(out)
        print("%-38s 章节 %d / 目录 %d / %.1f KB" % (doc["html"], len(sections), len(toc), len(out.encode()) / 1024))


if __name__ == "__main__":
    main()
