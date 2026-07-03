---
name: command-tent-csharp
description: 《中军帐》(The Command Tent) C# 引擎无关内核的实现规范与编码约定。涵盖项目 / 命名空间结构、引擎无关纪律(内核零引擎依赖)、战术内核纯函数契约(战场状态 → 战斗结果)、确定性 / 可种子化随机(为可测性)、xUnit 测试范式、基于术语表的命名约定,以及内核代码的"完成定义"清单。只要用户为《中军帐》/ Command Tent 写、读、审、重构 C# / .NET 内核代码 —— 真相世界 / 认知世界 / 传令系统 / 战术结算 / 单元测试 —— 就加载这个 skill。请先加载 command-tent-core 拿到术语表与范围红线,再用本 skill。
---

# 《中军帐》C# 内核实现规范

> 先决条件:已加载 `command-tent-core`(术语表 §10、范围红线 §6、技术决策 §7)。本 skill 只补充**写代码时**的具体约定。

> ⚠️ 下列结构 / 签名是**与蓝图原则一致的建议默认**(蓝图是设计记录,未规定代码细节)。第一次落地时与用户确认即可,之后即视为项目约定。

## 1. 引擎无关纪律 (最高优先级,不可破)
内核 = 纯 .NET 类库,实现真相世界 / 认知世界 / 传令系统 / 战术结算。**内核代码绝不 `using` 任何引擎 / 渲染 / 输入 / 平台 API**(Godot、Unity、System.Windows、UI 框架…均禁止)。

- 内核**只暴露数据与纯逻辑**;由引擎层(里程碑二的 Godot)去订阅、渲染、采集输入。
- 通过**事件 / 不可变快照**对外通信,而非让内核回调引擎。
- 检验:内核项目能在纯 `dotnet test` 下跑全部测试,不引入任何 UI 包。若一段代码需要引擎才能编译,它就**放错层了**。

## 2. 解决方案 / 项目结构 (建议)
```
CommandTent.sln
├── src/
│   ├── CommandTent.Core/            # 引擎无关内核(纯 .NET,无引擎依赖)
│   │   ├── World/                   # TruthWorld, BeliefWorld, 快照
│   │   ├── Units/                   # Unit, Lieutenant, Arm, 属性
│   │   ├── Courier/                 # CourierSystem, Messenger, Dispatch, 延迟/丢失/失真管线
│   │   ├── Tactics/                 # 战术内核:Intent, 结算纯函数, 战斗模型
│   │   ├── Intel/                   # 情报失真四类、InfoAge、GhostMarker
│   │   └── Ai/                      # Utility AI(敌方,基于 BeliefWorld 决策)
│   └── CommandTent.Console/         # 原型 ASCII 壳(可引用 Core,薄)
└── tests/
    └── CommandTent.Core.Tests/      # xUnit,镜像 src 结构
```
- 命名空间随文件夹:`CommandTent.Core.Tactics` 等。
- 原型期 ASCII 壳放 `CommandTent.Console`,**它依赖 Core,Core 绝不反向依赖它**。

## 3. 战术内核 = 纯函数契约 (蓝图 §3.5 明确要求)
战术结算做成**纯函数**:`(战场真实状态, 一组已送达的意图, 随机源) → 战斗结果(胜负 / 伤亡 / 士气变化)`。无隐藏全局状态、无副作用、同输入同输出。

建议形态(确认后落地):
```csharp
// 纯函数:不读写任何外部可变状态
public static TacticalResult Resolve(
    TruthWorldSnapshot world,      // 不可变输入快照
    IReadOnlyList<DeliveredIntent> intents,
    IRandomSource rng);            // 注入的随机源(见 §4)
```
- 输入用**不可变快照 / 只读集合**;不在结算内 mutate 传入对象。
- 战略层(完整层)将直接调用同一个 `Resolve` —— **两层解耦,各自可独立单测**。这是零重写的关键,别破坏它。

## 4. 确定性与随机 (可测性命脉)
迷雾 / 失真 / attrition 都含随机,但测试必须可复现。

- **绝不**在内核里直接 `new Random()` 或用静态随机。
- 注入一个 `IRandomSource`(可种子化)。测试用固定种子 → 结果确定可断言。
- 损耗公式 `f(兵种克制 × 地形 × 士气 × 体力 × 随机)`:把"随机"那一项做成 `rng` 的显式调用,便于测试时锁定。
- 同理,情报失真管线的"掷骰"(延迟 / 完整度 / 可信度,见 systems skill)也走同一个注入的 `rng`。

## 5. 真相世界 vs 认知世界:严格单向 (体验红线落到代码)
- `BeliefWorld` **只能**经由 `CourierSystem` 送达的 `Dispatch` 更新;**绝不**直接读 `TruthWorld`。
- 渲染 / UI 层**只能**读 `BeliefWorld`,**永远拿不到** `TruthWorld` 引用(复盘除外,复盘是战后专门通道)。
- 这条单向约束是支柱①"无上帝视角"在架构上的保证。任何让 UI 直接读真相的代码 = bug。

## 6. xUnit 测试范式
- 测试项目 `CommandTent.Core.Tests`,结构镜像 `src`。
- 命名:`方法名_场景_预期`,如 `Resolve_SpearVsCavalry_CavalryTakesHeavyLosses`。
- 每个机制至少覆盖:正常路径 + 边界(士气崩 / 体力竭 / 情报丢失纯沉默)+ 确定性(固定种子下结果稳定)。
- 纯函数内核 = 测试天堂:直接喂快照、断言结果,无需 mock 引擎。
- 失真 / 随机机制:用固定种子断言精确值;或断言统计性质(跑 N 次,均值 / 分布在区间内)。

## 7. 命名约定
- 一律走 `command-tent-core` §10 术语表(`TruthWorld` / `BeliefWorld` / `Unit` / `Lieutenant` / `Dispatch` / `Messenger` …)。
- 枚举:`Verb { Move, Attack, Hold, Withdraw }`、`Tone { Cautious, Normal, Aggressive, AtAllCosts, UseJudgment }`、`Arm { Spear, Bow, Cavalry, Shield }`。
- 表示"某时刻已知"的类型带 `InfoAge` / 时间戳;过期的渲染为 `GhostMarker`。
- 不可变快照类型用 `record` / `readonly struct`,后缀 `Snapshot`。

## 8. 完成定义 (内核代码 DoD 清单)
提交内核代码前自查:
- [ ] 零引擎 / UI / 平台依赖(`using` 干净)。
- [ ] 战术结算保持纯函数,无隐藏可变状态。
- [ ] 随机走注入的 `IRandomSource`,测试可用固定种子复现。
- [ ] UI / 渲染路径拿不到 `TruthWorld`(单向约束未破)。
- [ ] 命名符合术语表。
- [ ] xUnit 覆盖正常 + 边界 + 确定性。
- [ ] 仍在**原型范围内**(没顺手实现完整层系统,除非用户明确要前瞻)。
