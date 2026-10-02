# 工程外断言工程模板（offline harness）

> 用途：把框架里**不引用 `UnityEngine`** 的源码链接进普通 .NET 控制台工程，`dotnet run -c Release` 直接跑断言。
> 这是本框架"**没有'猜它能不能工作'，只有'跑给看'**"的落地方式；改内核（路径/槽位表/池/日志/解析/规则）后**必须**用它复验。

## 怎么用

```text
1. 复制本目录的三个文件到一个临时目录（**不要**放进 Assets，Unity 会去编译它）：
     Harness.csproj / Program.cs（外加按需的 UnityStubs.cs）
2. 把 <REPO> 换成仓库绝对路径；按需增删 <Compile Include> 的清单（见下）
3. 需要 Unity 类型时补一个 UnityStubs.cs（桩的**签名必须与真实一致**，不一致就编译不过 —— 这本身是一道校验）
4. dotnet run -c Release   → 输出 "通过 N / N"，任何一条不过就打印"期望 vs 实际"
5. 验完删掉临时工程（这些是校验产物，不属于框架）
```

## 可以直接链接的源码（框架官方清单）

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

> 其它模块（事件中心、热更清单/URL/版本比对、GM 解析与匹配…）也都能这样验：只要它不引用 `UnityEngine` 就能链接进来；
> 依赖 Unity 类型时用桩（历史上用这个办法验过：事件系统 52 项断言 + fuzz、GM 指令 112 项断言 + fuzz、热更清单与 URL 规则）。

## 写断言的约定

1. **每条断言一句人话**：`That(actual == expected, "嵌套派发：外层 2 次调用 + 内层 2 次调用，不死循环")`；
2. **优先验"不变量"**，而不只是正常路径：节点不重复、不双归还、计数不为负、派发深度归零、无残留删除标记、键相等；
3. **零分配类契约要实测**：`GC.GetAllocatedBytesForCurrentThread()` 前后差值（例如"Add/Remove 10000 轮 0 字节"）；
4. **随机行为固定种子**（`new Random(seed)`），失败能复现；
5. **性能数字取多轮最小值**（避开偶发 GC 停顿），并把数字写进结论文档；
6. 断言失败**不要 early return**，收集起来最后统一打印（一次跑完能看清有几处坏）。
