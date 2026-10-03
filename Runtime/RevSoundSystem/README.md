# 音效系统（RevSoundSystem）—— 3 分钟上手

> **一行代码播放音效**：`RevSound.Play("ui_click");`
> 10 个 `.cs` / 1640 行（注释 453 + 净代码 907 + 空行 280）。**你要读的只有 1 个文件**：`Facade\RevSound.cs`（296 行）。

## 一、2D 还是 3D？（先看这一张表）

| | 2D 音效 | 3D 音效 |
|---|---|---|
| 写法 | `RevSound.Play("ui_click")` | `RevSound.PlayOn("cast", enemy.gameObject)`<br>`RevSound.PlayAt("boom", hitPoint)` |
| 要不要给位置 | **不用给**（给了也没意义） | **必须给**：挂在哪个物体上 / 哪个世界坐标点 |
| 距离衰减 | 没有，永远听得清 | 有（默认 1~50 米，`RevSound.Set3DRange` 可调） |
| 典型用途 | 界面音、提示音、语音、BGM、播报 | 技能、打击、脚步、爆炸、环境声、角色身上的循环音 |

- **`PlayOn`（挂在 GameObject 上）**：播放器会**直接挂到你给的那个物体下面** → 它动声音就跟着动（位置由父子关系维护，**零每帧开销**）；物体被销毁（或隐藏）时声音自动收掉，业务不用判空、不用手动停。
- **`PlayAt`（挂在世界坐标点）**：播放器不跟随任何物体，位置就是这个点 —— 爆炸、落地、脚步点播这类用一次。
- **2D 直接播就行**：不需要指定位置，也不会因为距离变小声；它永远待在框架根节点下。

> 对应参考实现的"3D 音效的职责是给后端正确的 emitter（`AkGameObj` 挂在目标身上），是否 3D 由配置决定"：
> 这里同样 —— 框架只负责**把播放器挂对地方**，衰减交给 Unity 的 AudioSource。

## 二、3 分钟跑起来

```csharp
// ① 把音频文件放进游戏资源根目录（不用写任何配置代码）
//    Assets/GameRes/Audio/Sfx/ui_click.wav   ← RevSound.Play("ui_click")
//    Assets/GameRes/Audio/Bgm/login.wav      ← RevSound.PlayBgm("login")

// ② 播放（这一行就是全部）
RevSound.Play("ui_click");                        // 2D：界面音、提示音、播报
RevSound.PlayOn("skill_cast", hero.gameObject);   // 3D：挂到英雄身上（跟着它，物毁音停）
RevSound.PlayOn("run_loop", transform, loop: true);// 3D：挂在角色上的循环音
RevSound.PlayAt("explosion", hitPoint);           // 3D：挂在世界坐标点（不跟随）
RevSound.PlayBgm("login", fadeSeconds: 1f);       // BGM（自动替换上一首，可淡入淡出）

// ③ 要停就拿着句柄
RevSoundHandle h = RevSound.Play("vo_hello");
h.Stop();
```

**零配置**：不需要摆场景物体、不需要挂脚本、不需要写 `Update` —— 第一次播放时框架会自动创建一个隐藏宿主每帧驱动它。

### 音频放哪：在打包工具窗口里选（代码里不写死）

| | 位置（默认值） | 运行时读的常量 |
|---|---|---|
| 音效 | `<资源根目录>/Audio/Sfx/<名字>` | `RevSoundPath.Sfx` |
| BGM | `<资源根目录>/Audio/Bgm/<名字>` | `RevSoundPath.Bgm` |

**怎么改**：菜单 `Revolution.Tools/资源/RevAB 打包工具` → 「打包」页签 → 资源目录 → **音效目录**
（把文件夹拖进槽里 / 点「选择…」/ 点「默认」；目录必须在「资源根目录」之内）。

```text
ABBuildConfig.asset           ← 你在打包窗口里选的（团队共享，进版本管理）
      ↓ ABSoundPathGenerator 生成
RevSoundPath.cs               ← 生成到 Runtime 程序集内部（RevSoundSystem\Generated\）
      ↓ 音效系统读常量
零运行期 IO；目录写错编译期就报错
```

#### 子目录怎么用（音效名 = 相对根目录的路径，支持任意层）

音效名本身就是"**相对音效根目录的资源路径**"，所以子目录**不用配置、随便建**：

| 音频放在 | 播放写法 |
|---|---|
| `<资源根目录>/Audio/Sfx/ui_click.wav` | `RevSound.Play("ui_click")` |
| `<资源根目录>/Audio/Sfx/UI/ui_click.wav` | `RevSound.Play("UI/ui_click")` |
| `<资源根目录>/Audio/Sfx/Voice/Hero_1001/vo_hello.wav` | `RevSound.Play("Voice/Hero_1001/vo_hello", kind: RevSoundKind.Voice)` |
| `<资源根目录>/Audio/Bgm/Lobby/login.wav` | `RevSound.PlayBgm("Lobby/login")` |

- 子目录名直接进 **同帧去重 / 上限淘汰 / 句柄** 的判定：`"UI/click"` 与 `"Battle/click"` 是两条不同的声音；
- 手写习惯会被自动规范化：`Play("UI\\click")`（Windows 反斜杠）、`Play("/UI//click/")`（多余斜杠）都能正确加载
  （规范化实现在资源系统的 `RevResPathUtil.NormalizeResName` —— 纯 C#、可工程外单测，其它模块也能直接复用）；
- ❌ **名字里不要再写根目录**：`Play("Audio/Sfx/UI/click")` 会拼成 `Audio/Sfx/Audio/Sfx/UI/click`（加载失败 → `Failed(LoadFailed)`）—— 名字永远相对"音效根目录"；
- 打包窗口里「音效目录」下面会**列出当前已有的子目录**（含多级），照着抄即可；
- （可选）这些子目录在 `RevResPath` 里也各有一条常量（如 `Audio_Sfx_UI`），前提是它们位于资源根目录之下。

- 窗口里改完**自动重新生成** `RevSoundPath.cs`；编辑器加载时也会自动对齐一次（新克隆的仓库不会缺这个文件）；
- **为什么生成到 Runtime 里、而不是和 `RevResPath` 一起放 Generation**：`Revolution.Runtime.asmdef` 的 `references` 是空的，而 Generation 反过来引用了 Runtime —— 音效系统在 Runtime 里，反向引用会**成环**，所以这份常量只能生成在 Runtime 内部；
- **业务侧也能直接用**（它在 Runtime 程序集里，任何业务程序集都读得到）：
  `RevResManager.LoadAsync<AudioClip>(RevSoundPath.Sfx, "ui_click", clip => { ... })`；
- 这两个目录**故意不暴露成 `RevSound.Root` 之类的运行期字段**：目录是项目级事实，不该在运行时被随手改 —— 要换目录就去窗口改，改动自带编译期保护。

## 三、"我要做 X" 对照表

| 我想做的 | 写法 |
|---|---|
| 放一个界面音（2D） | `RevSound.Play("ui_click", kind: RevSoundKind.Ui)` |
| 技能音挂在释放者身上（3D） | `RevSound.PlayOn("skill_cast", hero.gameObject)` |
| 脚步 / 引擎这类循环音（3D，跟随） | `RevSound.PlayOn("run_loop", transform, loop: true)` |
| 爆炸 / 落地（3D，固定点） | `RevSound.PlayAt("explosion", position)` |
| 播语音/台词（2D） | `RevSound.Play("vo_hello", kind: RevSoundKind.Voice)` |
| 播 BGM / 换 BGM | `RevSound.PlayBgm("login", fadeSeconds: 1f)` |
| 播歌单（播放完自动下一首） | `RevSound.PlayBgmList("login_1", "login_2")` |
| 停一条 / 停全部 | `h.Stop()` / `RevSound.StopAll(fadeSeconds: 0.3f)` |
| 一块播、一块停（切界面/一局） | `using (var s = RevSound.OpenScope()) { s.PlayOn("cast", hero.gameObject); }` |
| 调音量（总/分类） | `RevSound.MasterVolume = 0.8f; RevSound.SetVolume(RevSoundKind.Bgm, 0.6f);` |
| 静音 / 总开关 | `RevSound.Mute = true;` / `RevSound.Enabled = false;` |
| 提前加载（点了就有声） | `RevSound.Preload("ui_click", "ui_close");` |
| 让代码只写"逻辑名"（表驱动） | `RevSound.Register("ui_click", "UI/Button/click", kind: RevSoundKind.Ui, volume: 0.8f);` |
| 预加载表里所有音效 | `RevSound.PreloadAll();` |
| 卸载 | `RevSound.Unload("ui_click")` / `RevSound.UnloadAll()` |
| 限制同时播放数 | `RevSound.MaxVoices = 24;`（超出淘汰最旧的一次性音效） |
| 同帧同名只播一次（防连点） | 默认开；关掉：`RevSound.FrameDedupe = false;` |
| 战斗中不播某些音 | `RevSound.Policy = new MyBattleSoundPolicy();`（实现 `IRevSoundPolicy`） |
| 知道"哪些音没播出去/为什么" | 订阅 `RevSound.Failed`（原因枚举见 `RevSoundErrorReason`） |
| 知道"播完了"（接台词、BGM 轮播） | 订阅 `RevSound.VoiceFinished` |
| 自己驱动（自定义宿主） | 每帧调 `RevSound.Tick(Time.deltaTime)`（之后自动宿主让位） |

## 四、四类声音的默认行为

| 分类 | 同帧去重 | 默认循环 | 适用 |
|---|---|---|---|
| `RevSoundKind.Ui` | ✅ | ✗ | 按钮、弹窗、页签 |
| `RevSoundKind.Sfx` | ✅ | ✗ | 技能、打击、脚步 |
| `RevSoundKind.Voice` | ✗（连续两句同台词是合理的） | ✗ | 角色语音、播报 |
| `RevSoundKind.Bgm` | ✗ | ✅ | 背景音乐 |

> 2D / 3D **不由分类决定**，由你调哪个方法决定（`Play` = 2D，`PlayOn` / `PlayAt` = 3D）—— 没有"某个分类悄悄变 3D"的隐藏魔法。

#### 音效表（catalog）：让代码只写"逻辑名"

磁盘目录怎么整理、美术怎么改名，调用点都不想跟着改 —— 那就把"逻辑名 → 真实路径"登记到表里：

```csharp
// ① 登记一次（建议在初始化/加载界面时；同名重复登记 = 覆盖）
RevSound.Register("ui_click",   "UI/Button/click",  kind: RevSoundKind.Ui, volume: 0.8f);
RevSound.Register("vo_hello",   "Voice/Hero_1001/vo_hello", kind: RevSoundKind.Voice);
RevSound.Register("run_loop",   "Sfx/Run/loop",     loop: true);
RevSound.Register("login_bgm",  "Lobby/login",      kind: RevSoundKind.Bgm);

// ② 之后代码只认逻辑名
RevSound.Play("ui_click");                 // → 加载 <资源根目录>/Audio/Sfx/UI/Button/click
RevSound.PlayOn("run_loop", transform);    // → 加载 Sfx/Run/loop，并且自动循环
RevSound.PlayBgm("login_bgm", fadeSeconds: 1f);
RevSound.PreloadAll();                     // 表里所有音效一次性预加载（各按自己的分类选根目录）
```

| 规则 | 说明 |
|---|---|
| 解析优先级 | 表里的值只补齐**调用点没写**的参数；调用点显式传的永远优先（`Play("ui_click", volume: 0.3f)` → 用 0.3） |
| 没登记的名字 | 原样当路径用（= 以前的写法，子目录照样能用）—— **音效表是可选增强，不是必须的开关** |
| 同名重复登记 | 覆盖（后登记的生效）；名字/路径为空返回 `false`，不改动表 |
| 同帧去重按什么判 | 按**真实路径 + 分类**：两个逻辑名指向同一条音效时也算重复 |
| `LoadFailed` 报什么 | 报的是**表里的真实路径**（因为它就是加载失败的那个路径）；其它失败报的是逻辑名 |
| 其它 API | `RevSound.Unregister(name)` / `IsRegistered(name)` / `ClearCatalog()` |

## 五、目录（你只需要读第一个）

| 文件 | 行数 | 说明 |
|---|---|---|
| `Facade\RevSound.cs` | 296 | ★ **唯一入口**：`Play`(2D) / `PlayOn`、`PlayAt`(3D) / BGM / 音量 / 开关 / 预加载 / **音效表** / 两个事件 |
| `Core\RevSoundKind.cs` | 69 | 四类声音 + 默认行为表（纯 C#） |
| `Core\RevSoundHandle.cs` | 58 | 句柄：`Stop()`、"过期句柄不误伤新声音"（纯 C#） |
| `Support\RevSoundScope.cs` | 83 | `using` 一块：退出时把这块播的声音全停 |
| `Implementation\RevSoundCore.cs` | 587 | 内核：加载→播放→挂点→每帧推进→回收（不用读） |
| `Core\RevSoundVoiceTable.cs` | 191 | 槽位表：代际 / 同帧去重 / 上限淘汰（纯 C#，可工程外单测） |
| `Implementation\RevSoundCatalog.cs` | 97 | **音效表**：逻辑名 → 真实路径 + 默认参数（纯 C#，可工程外单测） |
| `Implementation\RevSoundAssets.cs` | 182 | 资源层：clip 加载释放（接 `RevResourceSystem`）+ 播放器池（接 `RevObjectPool`） |
| `Generated\RevSoundPath.cs` | 31 | **工具生成，勿手改**：音效 / BGM 目录常量（在打包工具窗口里选） |
| `Support\RevSoundDriver.cs` | 46 | 隐藏宿主：每帧驱动（零配置，自动创建） |

## 六、与参考实现音效系统的对照

### 6.1 抄来的（精华）

| 参考实现哲学 | 本框架怎么落 |
|---|---|
| 异步是常态：播放立即返回、业务看不见异步窗口 | 资源没到 → 立即返回句柄，加载完自动开播（`Pending` 槽位）；业务代码完全同步写法 |
| 3D 音效 = 给后端正确的 emitter（`AkGameObj` 挂目标） | `PlayOn` 把播放器**直接挂到目标物体下**；物毁音停、位置零每帧开销 |
| 占位 ID + 播放现场缓存 | **只留一个槽位表 + 代际号**：Stop 就是校验代际后释放槽位，O(1)（参考实现是三张映射表 + 全表遍历） |
| 生命周期即作用域（14 个 Bank 域 + 一行批量回收） | `RevSoundScope`：`using` 一块，退出全停 |
| 筛选先于提交（最便宜的调用是不调用） | 策略 → 同帧去重 → 上限淘汰，三步都在"取播放器"之前 |
| 分类即语义 | `RevSoundKind` 四类 + 各自默认行为（**替掉**参考实现用事件名后缀 `_Hit_`/`_VO_` 匹配的魔法字符串） |
| 元数据驱动（代码不认识任何具体音效名，只认识 ID 和表） | 默认：音效名 = 相对根目录的路径（走资源系统的路径映射）；要"代码只写逻辑名"就用**音效表** `RevSound.Register`（`Implementation\RevSoundCatalog.cs`） |
| 策略可插拔、内核无知 | `IRevSoundPolicy`：内核不知道任何业务规则 |
| 性能内建：缓存先于计算、分帧、近似 | 播放器池化（`RevObjectPool`）+ 只遍历活跃槽位 + 稳态零分配（无 LINQ、无装箱） |
| 可观测性 | `Failed`（带原因枚举）/ `VoiceFinished` 两个事件 + `RevSound.Core.ToString()` |
| 循环音效的生命周期 | 循环音不参与上限淘汰 |

### 6.2 丢掉的（糟粕）

| 参考实现的问题 | 本框架的处理 |
|---|---|
| `CSoundManager` 3539 行上帝类 | 内核 587 行，且门面/内核分家 |
| 到处 Singleton（5 个都带 `GetInstance`） | 静态门面只是便捷；内核 `RevSoundCore` 是**普通实例**（可 new 多个） |
| 空 `catch { }` 吞异常、失败无日志 | 失败一律走 `RevSoundErrorReason` + `Failed` 事件（框架自身不打日志） |
| 接口爆炸（PostEvent 5 重载 + RTPC 10 重载） | 播放 5 个 + 停止 3 个；音量/开关 9 个；高级能力（策略/范围/自驱）各 1 个 |
| 默认参数陷阱（`forceUnload = true` 语义与直觉相反） | 所有默认值取"安全侧"（不卸载、不停播、不静音） |
| 公开 API 里留 `NotImplementedException` | 不写未实现的接口 |
| 静态临时容器（省分配但重入就串数据） | 不用共享临时容器 |
| 平台宏分支爆炸（`#if SGAME_LITE / IS_CE / …` 交织） | 零 `#if` 分支：平台差异由 `RevResourceSystem` 的后端策略吸收 |
| 业务要自己移除回调、自己判空 | 句柄自动失效；3D 目标销毁自动收声 |
| 配置与代码人工同步（"新增 Lim 包记得补数组"） | 无需要人工维护的数组 |
| 客户端混用同步/异步加载（同步命中"加载中"会拿到空内容） | **只走异步**（`RevResManager.LoadAsync`），首次晚一点出声换取零竞态 |

## 七、与你早期实现 `MusicMgr`（714 行）的对照

| 旧写法 | 新写法 | 旧实现的坑 |
|---|---|---|
| `MusicMgr.Instance.PlaySound("click")` | `RevSound.Play("ui_click")` | 单例 + 依赖 6 个其它管理器（`MonoMgr`/`SceneMgr`/`ABResMgr`/`GOPoolMgr`/`TimerMgr`/`LogSystem`） |
| 音频包名 `"sound"` 硬编码在**每一处调用**（`LoadResAsync<AudioClip>("sound", name, ...)`） | 路径只在资源层写一处；调用处只传名字 | 想换包/换目录要翻遍所有调用点 |
| **没有 3D 概念**（所有 AudioSource 都在 `SoundRoot` 下） | `RevSound.PlayOn("cast", hero.gameObject)` | 想挂在角色身上只能业务自己 `SetParent`，且没有"物毁音停" |
| `StopSound(AudioSource)` | `h.Stop()` | 业务要攥着 AudioSource；列表里找不到就静默 return（"停不掉"） |
| `ChangeSoundValue(v)` 遍历所有 AudioSource | `RevSound.SetVolume(RevSoundKind.Sfx, v)` | 只有 2 个全局音量（音效/BGM），没有主音量与分类 |
| `PlayBKMusic(ab, name)` | `RevSound.PlayBgm("name", fadeSeconds: 1f)` | BGM 切换是硬切；没有淡入淡出 |
| `PlayBKMusicList(...)` + 完成事件 | `RevSound.PlayBgmList("a", "b")` | 能力保留，去掉了 Timer 轮询与 `OnMusicPlaybackCompleted` 手工接线 |
| `PlaySoundSafe(...)`（场景切换延迟 100ms 重试） | 无需对应物 | 异步回调用 `isSceneChanging` 拦掉后**不释放 clip**（泄漏）；延迟靠定时器轮询 |
| `PlayOrPauseSound(bool)` | `RevSound.Enabled` / `Mute` | 用 bool 做全局暂停：两个模块同时暂停时，先恢复的那个会把别人的暂停也解掉（参考实现明确点名这是反模式） |
| `ClearSound()`（注释写了三遍"切场景前记得清"） | `using (var s = RevSound.OpenScope())` | 靠人记；忘了就"切页面还在响" |
| `duration` 定时停止：`(int)duration * 1000` | 自行 `h.Stop()`（或用作用域） | 小于 1 秒的 duration 会被截断成 0（旧代码的真 bug） |
| 三份几乎相同的播放代码（~250 行重复） | 内核一个入口 | 复制粘贴导致的"三份里有一份没 SetParent"这类不一致 |

## 八、最容易踩的 10 个坑

| # | 坑 | 说明 / 正确做法 |
|---|---|---|
| 1 | **3D 播放必须给"挂在哪"** | `PlayOn` 的目标为 null（物体已销毁）会触发 `Failed(NoTarget)` 并返回空句柄 —— 这是刻意的：偷偷降级成 2D 会让技能音突然"贴脸满音量"，更难查 |
| 2 | **挂在物体上的音效会随物体的可见性走** | 目标被隐藏（`activeInHierarchy == false`）→ 播放器随之停，框架按"播完"回收并触发 `VoiceFinished`；物体被销毁 → 播放器一起销毁，声音自动收 |
| 3 | **首次播放有一点延迟** | 本框架只用异步加载（避免同步加载的竞态与 WebGL 限制）。要"点了就有声"就 `RevSound.Preload(...)` |
| 4 | **`Unload` 之后正在播的会怎样** | 不会断 —— 播放器自己还持有 clip 引用；只是"不再常驻"，下次播放会重新加载 |
| 5 | **`Mute` 与 `Enabled` 不是一回事** | `Mute` = 音量按 0（声音继续走，取消静音立刻接上）；`Enabled = false` = 后续播放直接不发生 |
| 6 | **作用域不负责卸资源** | `RevSoundScope` 只停声音；资源由 `Preload / Unload / UnloadAll` 显式管理（两个作用域可能共用一个片段，谁也别替谁释放） |
| 7 | **`RevResGroup.Sound` 的归属只认第一次** | 音效片段第一次被谁加载就归哪一组（`RevResourceSystem` 的规定）；公共音效想要"永不参与分组卸载"就 `AddFlag(RevResInstanceFlag.Resident)` |
| 8 | **上限到了会丢音** | `MaxVoices`（默认 24）满了先淘汰最旧的一次性音效；如果全是循环音就会拒绝新播放并触发 `Failed(TooManyVoices)` —— 团战丢音请调大上限或加 `Policy` 提前裁剪 |

| 9 | **想改音频目录怎么办** | 菜单 `Revolution.Tools/资源/RevAB 打包工具` → 「打包」页签 → 资源目录 → 音效目录（选完自动重生成 `RevSoundPath.cs`）。**不要**自己在 `RevSound.cs` 里加路径字段：目录是项目级事实，改这里会绕开工具与编译期保护 |

| 10 | **名字里又写了一遍根目录** | `Play("Audio/Sfx/UI/click")` 会拼成 `Audio/Sfx/Audio/Sfx/UI/click` → `Failed(LoadFailed)`。名字永远**相对音效根目录**：子目录写进去（`"UI/click"`），根目录不写（根目录由打包窗口配置） |

> 附 ①：登记音效表时别忘了**分类** —— 表里的 `kind` 决定"用哪个根目录"（`Bgm` → BGM 根目录）与是否同帧去重；登记背景音乐请写 `kind: RevSoundKind.Bgm`。
> 附 ②：挂在目标物体上的播放器随物体销毁后，对象池的"在用数量"统计可能偏大一点（纯统计口径，不影响功能 —— 池在取出时会自动跳过空壳，归还空壳会被丢弃）。

## 九、没声音怎么查（按顺序）

1. **订阅 `RevSound.Failed`**：`Disabled`（总开关关了）/ `PolicyRejected`（策略拦的）/ `Duplicated`（同帧重了）/ `TooManyVoices`（上限）/ `NoTarget`（3D 没给挂点）/ `LoadFailed`（资源没找到）。这一条能定位 90% 的问题。
2. **`LoadFailed` 时查路径**：窗口里配的那两个目录（常量值见 `Generated\RevSoundPath.cs`，默认 `Audio/Sfx/`、`Audio/Bgm/`）+ 名字 拼出来的目录对不对？编辑器下 `RevResourceSystem` 走 AssetDatabase，运行时走 AB 映射（`ResMap`）—— 音频文件有没有被打进包？
3. **看整体状态**：`RevSound.Core.ToString()` → `RevSoundCore(活跃 3/24，BGM=login)`。
4. **3D 音效听不到**：确认用的是 `PlayOn`（目标物体有效、在场景里）或 `PlayAt`（坐标对不对）；听距范围默认 1~50 米（`RevSound.Set3DRange`）；Unity 按 `minDistance/maxDistance` 衰减。
5. **2D 音效"听着很远/很近"**：不会 —— 2D 的 `spatialBlend = 0`，完全不受距离影响。若听起来有衰减，说明是走 3D 播的。
6. **确认它真的在推进**：如果你自己接管了驱动（调过 `RevSound.Tick`），要保证每帧都调 —— 调过一次之后自动宿主就永久让位了。

## 十、规模与验证

| 项 | 结果 |
|---|---|
| 规模 | 10 个 `.cs` / 1640 行（注释 453 + 净代码 907 + 空行 280）；小白必读 1 个文件 296 行 |
| 目录配置 | 在 `RevAB 打包工具` 窗口里选（不进代码）：`ABBuildConfig`（+2 字段）→ `ABSoundPathGenerator`（新增 136 行）→ `RevSoundPath.cs`（生成物 27 行） |
| 对比旧实现 | `MusicMgr.cs` 714 行、只有 2 个音量 + 无句柄 + **无 2D/3D 区分** + 无作用域；本框架做到 2D/3D 显式区分（挂物体/挂坐标）+ 4 分类 + 池化 + 异步自动补播 + 作用域 + 双事件 |
| 框架编译 | Debug **0 错 0 警**、Release 0 错 |
| 纯 C# 内核行为验证 | **26 / 26 通过**（句柄代际、槽位复用不误停、轮转分配、同帧去重、上限淘汰、重复释放幂等、分类默认表） |
| 路径 / 规范化 / 音效表验证 | **43 / 43 通过**（`RevResPathUtil`：一层与多级子目录拼接、"两段键 == 完整路径键"不变量、名字规范化 12 项；`RevSoundCatalog`：登记/覆盖/注销/解析优先级）—— 与内核合计 **69 / 69** |
| 名字规范化归属 | 已下沉到 `RevResPathUtil.NormalizeResName`（纯 C#、可单测；资源/UI/池子等模块都能复用） |
| 依赖 | `RevResourceSystem`（音频片段）+ `RevObjectPool`（播放器池，公开的 `RevPoolCore<AudioSource>`，**不需要预制体**） |
