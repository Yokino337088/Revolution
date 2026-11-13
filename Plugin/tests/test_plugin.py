"""验证离线 Git 门禁与插件实际使用的 MCP 协议入口。"""

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
FRAMEWORK = ROOT / "revolution"
CHECK = FRAMEWORK / "policy/check.py"
GUID = "abcd0123456789ef" * 2


class PolicyTests(unittest.TestCase):
    """使用一次性 Git 仓库，复现工作区、索引与提交内容不一致的情况。"""

    def setUp(self):
        """创建合规资产和一个已有 C# 诊断，供增量规则比较。"""
        directory = tempfile.TemporaryDirectory(prefix="rev-policy-test-")
        self.addCleanup(directory.cleanup)
        self.repo = Path(directory.name)
        self.git("init", "-q")
        self.git("config", "user.name", "RevPolicy Test")
        self.git("config", "user.email", "rev-policy@example.invalid")
        self.put(".codex/rev-policy.json", json.dumps({
            "version": 1, "unityProject": "Unity", "assetExcludes": ["Revolution"],
            "assetLocations": {".mat": {"directory": "GameRes/Material", "moduleSubdirectory": True}},
            "frameworkSnapshot": {"directory": "Revolution", "anchor": "PACKAGE_VERSION.txt"},
            "code": {"directories": ["GameScripts"], "runtimeDirectories": ["GameScripts"],
                     "panelDirectories": ["GameScripts/Panels"], "internalTypes": ["RevABLoader"]},
        }))
        for index, folder in enumerate(("GameRes", "GameRes/Material", "GameRes/Material/Board", "Scenes",
                                        "GameScripts", "GameScripts/Panels"), 1):
            self.put(f"Unity/Assets/{folder}.meta", f"guid: {index:032x}\n")
        self.material = "Unity/Assets/GameRes/Material/Board/棋盘.mat"
        self.put(self.material, "baseline material\n")
        self.put(self.material + ".meta", f"guid: {GUID}\n")
        self.code = "Unity/Assets/GameScripts/Panels/Duel.cs"
        self.old_code = "class Duel\n{\n    void Start()\n    {\n        if (true) return;\n    }\n}\n"
        self.put(self.code, self.old_code)
        self.put(self.code + ".meta", f"guid: {'1' * 32}\n")
        self.put("Unity/Assets/Revolution/Runtime.cs", "source")
        self.put("Unity/Assets/Revolution/PACKAGE_VERSION.txt", f"commit = {'a' * 40}\n")
        self.git("add", ".")
        self.git("commit", "-qm", "fixture")

    def git(self, *args):
        """Fixtures use their own index and branch."""
        return subprocess.check_output(["git", "-C", str(self.repo), *args], encoding="utf-8")

    def put(self, relative, data):
        """Write an asset or config under this fixture."""
        path = self.repo / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(data, encoding="utf-8")

    def check(self, expected, *args):
        """Assert both the exit status and the human-readable result."""
        result = subprocess.run([sys.executable, str(CHECK), "--repo", str(self.repo), *args],
                                capture_output=True, encoding="utf-8", timeout=90)
        self.assertEqual(result.returncode, expected, result.stdout + result.stderr)
        return result.stdout

    def test_staged_bad_material_ignores_unstaged_fix(self):
        """A proposed commit cannot borrow its missing meta from the working tree."""
        new = "Unity/Assets/Scenes/错放材质.mat"
        self.put(new, "new material\n")
        self.git("add", new)
        self.put(new + ".meta", f"guid: {'2' * 32}\n")
        staged = self.check(1, "--staged")
        self.assertIn("REV100", staged)
        self.assertIn("REV101", staged)
        self.assertNotIn("REV101", self.check(1))
        self.git("commit", "-qm", "bad material")
        self.assertIn("REV101", self.check(1, "--base", "HEAD^"))

    def test_untracked_duplicate_and_stable_moves(self):
        """Compare GUIDs against the destination snapshot and keep a valid move legal."""
        duplicate = "Unity/Assets/GameRes/Material/Board/duplicate.mat"
        self.put(duplicate, "new material\n")
        self.put(duplicate + ".meta", f"guid: {GUID}\n")
        self.assertIn("REV103", self.check(1))
        (self.repo / duplicate).unlink()
        (self.repo / (duplicate + ".meta")).unlink()
        self.git("mv", self.material, duplicate)
        self.git("mv", self.material + ".meta", duplicate + ".meta")
        self.check(0, "--staged")
        self.put(duplicate + ".meta", f"guid: {'3' * 32}\n")
        self.git("add", duplicate + ".meta")
        self.assertIn("REV102", self.check(1, "--staged"))

    def test_framework_anchor_must_change_with_snapshot(self):
        """Re-recording the same commit must not silence an edited framework snapshot."""
        self.put("Unity/Assets/Revolution/Runtime.cs", "changed source")
        self.assertIn("REV110", self.check(1))
        self.put("Unity/Assets/Revolution/PACKAGE_VERSION.txt", f"# local note\ncommit = {'a' * 40}\n")
        self.assertIn("REV110", self.check(1))
        self.put("Unity/Assets/Revolution/PACKAGE_VERSION.txt", f"commit = {'b' * 40}\n")
        self.check(0)

    def test_roslyn_flags_new_bypasses_and_ignores_strings(self):
        """The existing single-line if stays grandfathered until it changes."""
        new = self.old_code.replace("        if (true)", '        string example = "if (x) return; Application.dataPath";\n        if (true)')
        self.put(self.code, new)
        self.check(0)
        new = "using TaskAlias = System.Threading.Tasks.Task;\n" + new.replace(
            "        if (true)", "        if (false) { Destroy(gameObject); }\n"
            "#if UNITY_ANDROID\n        var path = UnityEngine.Application.streamingAssetsPath;\n#endif\n"
            "        var loader = new Revolution.RevABLoader();\n"
            "        RevInitialize.Initialize();\n"
            "        UnityEngine.SceneManagement.SceneManager.LoadScene(\"SomeScene\");\n"
            "        if (true)")
        self.put(self.code, new)
        output = self.check(1)
        for number in range(200, 207):
            self.assertIn(f"REV{number}", output)


class PluginTests(unittest.TestCase):
    """Use the installed layout and the real RevMCP stdio worker."""

    def test_manifests_and_packaged_components(self):
        """Every marketplace entry and manifest reference resolves inside its package."""
        market = json.loads((ROOT.parent / ".agents/plugins/marketplace.json").read_text(encoding="utf-8"))
        for entry in market["plugins"]:
            root = ROOT.parent / entry["source"]["path"]
            manifest = json.loads((root / ".codex-plugin/plugin.json").read_text(encoding="utf-8"))
            self.assertEqual(entry["name"], manifest["name"])
            for name in ("skills", "hooks", "mcpServers"):
                if name in manifest:
                    self.assertTrue((root / manifest[name]).exists())
        self.assertEqual(len(list((FRAMEWORK / "skills").glob("*/SKILL.md"))), 5)

    def test_mcp_binds_explicitly_and_retains_binding_on_error(self):
        """A bad second bind cannot replace the already selected Unity project."""
        with tempfile.TemporaryDirectory(prefix="rev-plugin-test-") as directory:
            project = Path(directory) / "测试 Unity"
            (project / "Assets").mkdir(parents=True)
            (project / "ProjectSettings").mkdir()
            (project / "ProjectSettings/ProjectSettings.asset").touch()
            process = subprocess.Popen([sys.executable, "-B", str(FRAMEWORK / "server/serve.py")],
                                       stdin=subprocess.PIPE, stdout=subprocess.PIPE)
            try:
                def exchange(method, params=None, ident=1):
                    request = {"jsonrpc": "2.0", "method": method}
                    if params is not None:
                        request["params"] = params
                    if ident is not None:
                        request["id"] = ident
                    process.stdin.write(json.dumps(request).encode() + b"\n")
                    process.stdin.flush()
                    if ident is not None:
                        response = json.loads(process.stdout.readline())
                        self.assertEqual(response["id"], ident)
                        return response["result"]

                def call(name, args):
                    return exchange("tools/call", {"name": name, "arguments": args})

                exchange("initialize", {"protocolVersion": "2025-03-26"})
                exchange("notifications/initialized", ident=None)
                names = {tool["name"] for tool in exchange("tools/list")["tools"]}
                self.assertEqual(names, {"rev_bind", "rev_discover", "rev_call", "rev_run"})
                self.assertTrue(call("rev_discover", {})["isError"])
                self.assertEqual(call("rev_bind", {"project": str(project)})["structuredContent"]["project"], str(project.resolve()))
                self.assertTrue(call("rev_bind", {"project": "relative"})["isError"])
                result = call("rev_discover", {"target": "editor", "query": "editor.status"})["structuredContent"]
                self.assertTrue(result["unavailable"])
                self.assertTrue(Path(result["reportPath"]).is_file())
            finally:
                process.stdin.close()
                process.wait(timeout=15)
                process.stdout.close()

    def test_revgame_session_context_is_scoped(self):
        """Compaction in RevGame restores its key rules; another workspace stays quiet."""
        hook = ROOT / "revgame/hooks/session.py"
        with tempfile.TemporaryDirectory(prefix="rev-hook-test-") as directory:
            root = Path(directory)
            child = root / "src"
            child.mkdir()

            def invoke():
                event = {"cwd": str(child), "hook_event_name": "SessionStart", "source": "compact"}
                return subprocess.check_output([sys.executable, str(hook)], input=json.dumps(event), encoding="utf-8")

            self.assertEqual(invoke(), "")
            skill = root / ".codex/skills/revgame-development/SKILL.md"
            skill.parent.mkdir(parents=True)
            skill.touch()
            (root / ".codex/rev-policy.json").touch()
            context = json.loads(invoke())["hookSpecificOutput"]["additionalContext"]
            self.assertIn("tools/RevPolicy/check.py", context)

    def test_revolution_session_finds_nested_project(self):
        """The repository root resolves the same Unity project as its policy."""
        hook = FRAMEWORK / "hooks/session.py"
        with tempfile.TemporaryDirectory(prefix="rev-nested-hook-") as directory:
            root = Path(directory)
            project = root / "client/Unity"
            package = project / "Assets/Revolution/package.json"
            package.parent.mkdir(parents=True)
            package.write_text("{}", encoding="utf-8")
            policy = root / ".codex/rev-policy.json"
            policy.parent.mkdir()
            policy.write_text(json.dumps({"unityProject": "client/Unity"}), encoding="utf-8")
            for cwd in (root, project):
                event = {"cwd": str(cwd), "hook_event_name": "SessionStart", "source": "compact"}
                output = subprocess.check_output([sys.executable, str(hook)], input=json.dumps(event), encoding="utf-8")
                context = json.loads(output)["hookSpecificOutput"]["additionalContext"]
                self.assertIn("RevScope", context)


if __name__ == "__main__":
    unittest.main()
