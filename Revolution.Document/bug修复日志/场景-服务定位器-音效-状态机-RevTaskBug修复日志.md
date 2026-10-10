# 场景、服务定位器、音效、状态机与 RevTask Bug 修复日志

> **本轮摘要**：审查了五个指定 Runtime 模块的场景激活、资源引用、服务释放、异步任务完成与状态切换路径，修复了 12 类确认的问题。修复代码旁补了面向初学者的注释，说明问题怎么发生、原来会造成什么结果，以及为什么要增加相应保护。
>
> - 审查日期：2026-10-10
> - 审查范围：`Assets/Revolution/Runtime/RevScene`、`RevServiceLocator`、`RevSoundSystem`、`RevStateMachine`、`RevTask`
> - 构建：`RevolutionFrameWork_Unity.sln` 成功，0 个错误；仍有既有 Demo 字段未赋值和 Unity 依赖版本冲突警告
> - 静态检查：五个目标目录 lint 无诊断；`git diff --check` 通过
> - 自动化测试：源码审查没有找到这五个模块对应的自动化回归测试用例
> - Unity 运行验证：本轮没有完成 PlayMode 测试，因此同步场景事件时序、真实音频异步加载和关闭 Domain Reload 后重进 Play 仍需在 Unity 中验证

---

## 一、场景系统 `RevScene`

### 1. 同步切换在新场景真正加载前就广播 `OnLoaded`

- **严重度：中**
- **问题表现**：同步切场景时，如果刚调用 `SceneManager.LoadScene` 就马上广播“加载完成”，订阅者可能仍处于旧场景上下文。业务在 `OnLoaded` 中读取 `RevScene.CurrentName` 或查找新场景对象时，可能拿到旧信息或找不到对象。
- **修复**：同步加载前临时订阅 Unity 的 `SceneManager.sceneLoaded`；只有收到匹配目标场景的通知后，才将状态设为 `Done` 并广播 `OnLoaded`。场景路径会先转换成场景名进行匹配；调用 `LoadScene` 抛异常时移除临时订阅并进入统一失败收尾。
- **实现**：`RevScene/Support/RevSceneLoader.cs`
- **小白理解**：发出“切场景”命令，就像按了开门按钮，不代表新房间已经打开。`OnLoaded` 应在 Unity 确认新场景已经加载后才通知业务。

### 2. 一个场景事件订阅者出错，会让其他订阅者收不到通知

- **严重度：中**
- **问题表现**：多个系统可以同时监听 `OnProgress`、`OnLoaded` 等事件。旧代码一次调用所有订阅者；如果第一个 UI 回调抛异常，后面的 UI 或业务逻辑就不会执行。
- **修复**：`Guard` 现在逐个调用订阅者并分别捕获异常。一个订阅者出错会被记录，但不会阻断其他订阅者，也不会影响场景切换。
- **实现**：`RevScene/Facade/RevScene.cs`

---

## 二、服务定位器 `RevServiceLocator`

### 3. 一个服务的 `Dispose` 抛异常，会阻止剩余服务释放

- **严重度：中**
- **问题表现**：容器按逆创建顺序释放服务。若一个服务在 `Dispose()` 中因为文件已关闭、网络连接异常等原因抛出异常，旧实现会立刻退出释放循环。后创建的其他服务没有机会清理，它们持有的文件、连接或原生资源可能泄漏。根容器释放子作用域时也可能因此跳过后续作用域及根 Singleton。
- **修复**：`RevServiceRegistry.DisposeAll` 对每个实例分别捕获异常，继续尝试释放其余实例；无论释放是否成功，最后都会清空容器索引。`RevServiceLocator.Dispose` 继续释放所有子作用域和根服务，最后将收集到的异常作为 `AggregateException` 一并报告。
- **实现**：`RevServiceLocator/Implementation/RevServiceRegistry.cs`、`RevServiceLocator/Facade/RevServiceLocator.cs`
- **小白理解**：清理一排房间时，一个房间的灯坏了，不应该因此放弃清理剩下所有房间；先尽力清完，再告诉调用者哪些清理失败。

---

## 三、音效系统 `RevSoundSystem`

### 4. 同名 BGM 和 SFX 可能错误地共用一份缓存

- **严重度：中**
- **问题表现**：项目可以同时有 `Audio/Sfx/theme` 和 `Audio/Bgm/theme`。旧音效缓存只用 `theme` 作为字典键；如果先请求 SFX，再请求 BGM，第二次请求会复用 SFX 的缓存，可能播错音乐或错误地认为 BGM 已加载失败。
- **修复**：改为使用“资源根目录 + 资源名”作为缓存键。SFX 与 BGM 即使文件名相同，也会有各自的加载状态、AudioClip 和资源引用。
- **实现**：`RevSoundSystem/Implementation/RevSoundAssets.cs`、`RevSoundSystem/Implementation/RevSoundCore.cs`

### 5. 加载尚未完成时卸载，资源引用会丢失且无法归还

- **严重度：高**
- **问题表现**：请求音频后，资源系统已经给本次请求增加了引用计数。如果音效仍在加载时调用 `Unload`，旧代码会把本地缓存项删掉，却因为 `Loading == true` 跳过释放。加载完成后的回调只更新已脱离缓存表的旧 Entry，后续没有对象能找到并归还那份引用，资源可能一直留在资源缓存中。
- **修复**：每条音效缓存 Entry 保存本次 `LoadAsync` 返回的 `RevResHandle`。卸载时，即使加载仍在进行，也通过该句柄归还本次请求的引用；先从本地字典移除旧 Entry，让迟到回调无法把它重新放回缓存。`UnloadAll` 同样逐条归还句柄。
- **实现**：`RevSoundSystem/Implementation/RevSoundAssets.cs`

### 6. `PlayOn` 目标在等音频加载时销毁，会错误地从原点发声

- **严重度：中**
- **问题表现**：`PlayOn` 的音频片段尚未加载时，目标 GameObject 被销毁。Unity 会让它看起来像 null；旧逻辑随后把这次请求误当成 `PlayAt`，并使用默认世界坐标 `(0,0,0)`，声音突然从世界原点响起。
- **修复**：声音槽位单独记录这次请求是否来自 `PlayOn`。等待资源期间及真正创建播放器前都会检查目标；目标已销毁时回收槽位并报告 `NoTarget`，不再回退到原点。如果声音未能启动、槽位已经回收，也返回空句柄，避免给业务一个看似有效但无法控制声音的句柄。
- **实现**：`RevSoundSystem/Implementation/RevSoundCore.cs`

### 7. 关闭 Domain Reload 后，上一局的音效状态和事件订阅残留

- **严重度：高，需在关闭 Domain Reload 的 Editor 配置中验证**
- **问题表现**：关闭 Domain Reload 时，静态 `RevSound.Core` 不会在下一局 Play 自动重新创建。上一局留下的槽位、`AudioSource` / `Transform`、资源缓存、手动驱动状态和事件委托可能继续存在；旧 Unity 对象可能已经销毁，旧事件回调也可能指向销毁的 UI。
- **修复**：在 `SubsystemRegistration` 阶段调用 `ResetForNewSession`：停止并归还活动声音、释放音频资源引用、清空槽位和 Unity 对象数组、清除音效表及事件订阅，并恢复默认配置，使新一局首次播放重新初始化。
- **实现**：`RevSoundSystem/Facade/RevSound.cs`、`RevSoundSystem/Implementation/RevSoundCore.cs`

### 8. `VoiceFinished` / `Failed` 回调抛异常，会中断音效内核

- **严重度：中**
- **问题表现**：BGM 歌单的一首歌播放结束时，内核会通知 `VoiceFinished`，再开始下一首。如果任一业务订阅者抛异常，旧逻辑会直接中断这段流程，歌单就停住；失败通知也可能把异常带回播放入口。
- **修复**：对 `Failed` 和 `VoiceFinished` 的订阅者逐个捕获异常。一个业务回调出错只记日志，不阻止其他监听者、音效回收或 BGM 歌单继续推进。
- **实现**：`RevSoundSystem/Implementation/RevSoundCore.cs`

---

## 四、状态机 `RevStateMachine`

### 9. 异步切换中又请求“留在当前状态”，会重复退出并进入同一状态

- **严重度：高**
- **问题表现**：当前是 A，状态机正等待异步准备以切到 B；此时业务又请求切到 A。旧逻辑会取消 B，然后仍把 A 当作一次真正切换，重复调用 A 的退出和进入回调，可能重复注册事件、重新加载资源或重置状态计时。
- **修复**：目标已经是当前状态时，若有异步切换就只取消那次旧切换，然后保持当前状态；不再对同一实例执行 `OnExit` / `OnEnter`。重量级状态机的同步请求先取消在途切换，再检查目标是否已经是当前状态，符合“同步请求优先”的约定。
- **实现**：`RevStateMachine/Lightweight/Machines/RevLightStateMachine.cs`、`RevStateMachine/Heavyweight/RevHeavyFsm.cs`

### 10. 栈状态机 `Change` 会把栈下层的同一对象再放到栈顶

- **严重度：中**
- **问题表现**：栈里已有大厅状态 A 和顶层战斗状态 B，再调用 `Change(A)`。旧代码会弹掉 B，再把同一个 A 压到顶层，结果同一对象在栈里出现两份；后续 Pop 会对它重复执行退出/恢复回调，状态时序错乱。
- **修复**：`Change` 只允许替换栈顶。若目标实例已经在栈的下层，就明确报错；需要在栈中同时存在多个同类型状态时，使用工厂创建不同实例。
- **实现**：`RevStateMachine/Lightweight/Machines/RevLightStackStateMachine.cs`

---

## 五、异步任务 `RevTask`

### 11. 任务已经成功后，迟到的异常会把结果改写成失败

- **严重度：高**
- **问题表现**：同一个 `RevTaskCompletionSource` 可能被多个回调尝试完成。旧 `SetException` 会先写异常字段，再调用带“防重复完成”保护的 `SetResult`。如果任务早已成功，`SetResult` 会直接返回，但异常字段已被改写；之后 await 同一个任务时可能突然抛异常。泛型任务还可能丢失已给出的结果值。
- **修复**：泛型和非泛型 Promise 的 `SetException` 都先检查 `IsCompleted`。任务只接受第一次完成结果，后续成功或失败信号不会改写它。
- **实现**：`RevTask/RevTask.cs`

### 12. `WhenAll` 吞掉失败，导致“有任务失败但整批报告成功”

- **严重度：高**
- **问题表现**：旧 `WhenAll` 只统计完成数量；所有子任务结束后总是成功完成，不检查任何子任务的异常。比如一批资源或文件中有一个加载失败，`await WhenAll(...)` 仍会继续执行成功分支。
- **修复**：每个子任务结束时读取它的 await 结果并记住第一个异常；仍等待整批任务全部结束，让其余任务有机会完成清理。最后，有异常才让 `WhenAll` 失败，全部成功才报告成功。空任务列表仍立即成功。
- **实现**：`RevTask/RevTaskScheduler.cs`

---

## 验证结果与未完成事项

- **已完成**：完整 `RevolutionFrameWork_Unity.sln` 构建成功，0 个错误；五个目标目录 lint 无诊断；`git diff --check` 通过。
- **现有构建警告**：Demo 序列化字段未赋值及 Unity 依赖程序集版本冲突；没有改动这些非目标警告。
- **未完成 Unity 回归**：本轮没有运行 PlayMode 测试，且源码检索未发现这五个模块的自动化测试用例。编译成功只说明代码通过编译，不等于真实 Unity 场景、音频播放或生命周期行为已验证。
- **建议恢复 Unity 后优先验证**：同步场景加载传场景名和完整路径时 `OnLoaded` 的时序；一个场景事件订阅者抛异常时其他订阅者仍收到通知；一个服务 `Dispose` 故意抛错后其他服务仍释放；同名 BGM/SFX 分别加载；音频加载中执行 `Unload`；`PlayOn` 目标在加载期间销毁；关闭 Domain Reload 后第二次进入 Play；状态机准备切换时切回当前状态；栈 `Change` 指向下层实例被拒绝；`WhenAll` 中子任务失败会使 await 失败。

> 本轮修复有明确代码路径依据，但由于缺少自动化用例和 Unity PlayMode 验证，不能据此声称这些模块已经不存在其他 Bug。
