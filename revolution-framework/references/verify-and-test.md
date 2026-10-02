# 怎么证明"改对了"（验证与测试）

> 这个框架的哲学是"**没有'猜它能不能工作'，只有'跑给看'**"。改完代码不要停在"应该没问题"。

## 1. 五级验证（由快到慢，按改动范围选）

| 级别 | 适用 | 怎么做 |
|---|---|---|
| ① 编译 | 任何改动 | Unity 里编译 0 error；纯 C# 部分可以先用工程外桩工程编译一遍（见第 3 节） |
| ② 跑 demo 场景 | 业务/模块用法 | 打开对应 `<模块>Demo.unity` → Play → 点按钮，看右侧步骤日志；**改了模块行为就必须跑它的 demo** |
| ③ GM 面板 / 场景内自检 | 快速复现与手工验证 | `Revolution.Tools/GM 指令面板`（Ctrl+Shift+G）；或加一条临时 GM 命令直接跑 |
| ④ **工程外纯 C# 断言** | 改了框架**内核**（路径/槽位表/池/日志/计时/解析/规则） | 链接那些不引用 UnityEngine 的文件到普通 .NET 工程，`dotnet run -c Release` 跑断言（见下） |
| ⑤ 真机 + 破坏性测试 | 资源/UI/热更等"只有真机才暴露"的改动 | Android 真机跑首包落地 + 增量更新；热更按第 4 节的破坏性测试逐项过 |

## 2. 能进工程外断言的文件（框架已有清单）

`README.md`（仓库根）「工程外验证」一节列了这些**不引用 `UnityEngine`** 的文件，可直接链接进 .NET 控制台工程：

```text
RevResourceSystem/Core/RevResPathUtil.cs            路径拼接 + 缓存键（含"两段键 == 完整路径键"不变量）
RevTimer/Core/RevTimerTable.cs                       计时器槽位表（代际号 / 延迟复用 / 句柄校验）
RevTimer/Core/RevServerClock.cs                      服务器时间（一次校准 + realtime 外推）
RevSoundSystem/Core/RevSoundVoiceTable.cs            声音槽位表（代际号 / 同帧去重 / 上限淘汰）
RevObjectPool/Core/RevPoolCore.cs                    池引擎（重复归还拦截）
RevLog/Core/RevLogRing.cs                            日志环形缓冲（定长 / 零分配写入）
RevLog/Implementation/RevLogCore.cs                  日志内核（过滤 / 重复抑制 / 通道隔离）
RevPublicMono/Implementation/RevMonoCore.cs          监听列表内核（去重 / 上限 / 快照派发 / 异常隔离）
Editor/RevResourceSystem/ABTool/Snapshot/ABLayoutSnapshot.cs   AB 布局快照差异（改名 / 换包 / 增删识别）
```

框架已有的实测结果（改这些内核后应当复验并更新数字）：

| 模块 | 断言 | 实测 |
|---|---|---|
| RevTimer | 55 / 55 | 1000 个计时器每帧 **0.0139 ms**；10 万次创建 + 停止 **< 4 KB** |
| RevLog | 41 / 41 | 10 万条日志 **0 分配**；环形缓冲硬顶 2048 条 |
| RevPublicMono | 25 / 25 | 10 万次派发 **32 B** |
| ABTool 布局快照 | 24 / 24 | 改名 / 换包 / 增删识别 |

> 本仓库历史上用同样办法验过：事件系统（52 项行为断言 + 5 组种子 × 6000 步 fuzz，零结构违例）、
> GM 指令（112 项断言 + 20000 步随机注册/随机命令行的 fuzz）、热更清单与 URL 规则、ResMap 查表成本基准。
> **断言要覆盖"不变量"而不只是"正常路径"**：节点不重复、不双归还、计数不为负、状态归零、无残留标记。

## 3. 搭一个工程外断言工程（模板见 `templates/offline-harness/`）

```text
1. 新建 .NET 控制台工程（net8.0 即可），<EnableDefaultCompileItems>false</EnableDefaultCompileItems>
2. 用 <Compile Include="...绝对/相对路径.../" /> 把要验的真实源码链接进来（保持单一真相，不复制）
3. 需要 Unity 类型时再加一个"桩文件"（UnityEngine/UnityEditor 的最小声明）；
   ★ 桩的签名必须与真实一致（不一致时编译不过 —— 这本身就是一道校验）
4. 断言写下"可读的一句话 + 期望值"，失败即抛/计数，最后打印 "通过 N / N"
5. dotnet run -c Release 跑；有随机行为就固定种子，便于复现
```

**注意**：桩工程只能证明"纯 C# 逻辑 + API 用法"成立；Unity 侧行为（MonoBehaviour 生命周期、AssetBundle 真加载、真机路径）仍要靠 demo 场景与真机验证。

## 4. 热更的破坏性测试（8 项，改热更相关代码后逐项过）

1. 下载中途**断网** → 恢复后（重进 Play / 重新点更新）能续上（`.part` 保留）；
2. 下载中**杀进程** → 下次从计划重算，hash 一致的自然跳过；
3. **清单损坏 / 空清单** → 保持当前版本，玩家照常进游戏（不崩）；
4. **hash 不符**（手工改坏一个包）→ 校验拦下、整包重下（不带 offset 续传，避免越续越坏）；
5. **磁盘不足** → 提前报 `DiskFull`，不留垃圾；
6. **版本回滚**：删除 `current.txt` 指向的新版本 → 回到上一版（保留 N 份的意义）；
7. **覆盖式语义**：删掉 `{persistentDataPath}/RevHotUpdate` → 下一次加载回退到 StreamingAssets 首包；
8. **跨大版本**：清单 `@appVersion` 与客户端不一致 → `AppVersionMismatch`（拒绝更新并回调，不硬更）。

## 5. 文档站与分支（交付验证）

- 改了 `Revolution.Document/**` → 推 `main` 后 `pages.yml` 重新部署，在线站可见（https://yokino337088.github.io/Revolution/ ）；
- 改了 `Assets/Revolution/**` / `Assets/Revolution.Demo/**` / `Assets/Revolution.HotUpdate/**` → 对应生成分支（`package` / `demo` / `hotupdate`）自动整份重建，可 `git fetch origin <分支>` 核对提交与文件数。

## 6. 收尾习惯

- 关键数字（断言数、性能、行数）**顺手更新到文档**（README / 《架构解析》），别留旧数字；
- 发现"文档与代码不一致"时，**先确定哪个是对的**再改——多半是代码迁移过、文档漏改（例如目录改名、模块路径）。
