# 中军帐 · 里程碑一原型(无图形内核)

验证唯一要紧的问题:**「根据滞后/残缺/失真的情报下命令,复盘时发出『原来如此』」是否成立、是否揪心**。
对应设计文档 `../doc/中军帐_设计蓝图_v0.2.md` 的 §九(战术核心)、§十一(情报失真)、§十五(敌方 AI),以及 v0.1 §8.1。

## 结构(引擎无关内核 + 控制台前端 + 单测)

```
prototype/
  CommandPost.sln
  src/CommandPost.Core/        纯逻辑内核(不依赖任何引擎,将来 Godot 直接复用)
    Primitives.cs              Vec2 / 枚举 / Rng / 难度旋钮 / 地形天气
    Entities.cs                Commander(性格) / Intent(动词+基调) / Unit / 兵种克制
    Information.cs             Report(失真) / Messenger(传令兵) / GhostUnit / BeliefWorld(认知世界)
    TruthWorld.cs              真相世界容器
    Simulation.cs              主循环:移动/战斗/失真管线/传令送达拦截/副将解读/敌方 Utility AI
    Scenario.cs                最小遭遇战(每方 3 部队)
  src/CommandPost.Console/     ASCII 渲染 + 步进式输入(只读认知世界,绝不读真相)
  tests/CommandPost.Core.Tests/ xUnit:命令延迟、纯沉默丢失、确定性、胜负、克制…
```

核心架构 = **双世界**:`TruthWorld`(真相,玩家永不可见)+ 每方一个 `BeliefWorld`(认知,只由送达战报拼成)。
渲染层只读 `Beliefs[Side.Friend]`;真相只在 `peek`(复盘)时展示。

## 运行(本机 .NET 8 SDK 装在 `~/.dotnet`,未入 PATH)

PowerShell 下用完整路径(或先把 `~/.dotnet` 加进 PATH):

```powershell
$d = "$env:USERPROFILE\.dotnet\dotnet.exe"
& $d test  prototype\CommandPost.sln              # 跑单元测试(应 7/7 通过)
& $d run --project prototype\src\CommandPost.Console            # 交互式
& $d run --project prototype\src\CommandPost.Console -- --demo  # 脚本化端到端演示
& $d run --project prototype\src\CommandPost.Console -- --hard  # 硬核难度(情报更少)
```

## 交互命令(原型用步进近似「实时可暂停」)

| 命令 | 含义 |
|---|---|
| `n [k]` | 推进 k 个 tick(空回车=1) |
| `m u x y [t]` | 部队 u 移动到 (x,y);t=基调 c/a/o/j |
| `a u e [t]` | 部队 u 攻击敌军 #e(按最后已知位置) |
| `h u` / `r u x y` | 据守 / 后撤 |
| `s u` | 派传令兵探问 u,带回它的**完整局部情报包**(自身近况 + 它所知敌情;往返延迟,可能纯沉默) |
| `c x y` | 中军派斥候去 (x,y) 侦察(视野大、避敌绕道、返回中军) |
| `cs u x y` | 令部队 u 派斥候去 (x,y)(传令兵先把命令送到) |
| `log` / `peek` / `q` | 完整军情 / 真相快照 / 退出 |
| `replay [ms]` | **逐 tick 上帝视角动画复盘**:战中隐藏的斥候 `o` / 传令兵 `*`(含敌方 `x`/`+`)在此显形 |

★ 命令要花时间送到前线,副将按**送达那刻**的真实情况解读执行——你下的不是操作,是意图。

## 这一版已演出的「灵魂」

- 认知世界严重滞后真相(沙盘标记带「信息年龄」,久未更新变「幽灵」)。
- **部队即传感器**:每支部队记住自己看到的敌情;传令兵带回「自身近况 + 它所知敌情」整包——久未通信的部队,其传令兵一到就吐出它所知(已滞后)的整张局部敌情图。
- **斥候侦察**:部队 / 传令兵 / 斥候各有视野(斥候 > 部队 > 传令兵)。等待时部队按将领性格自动派斥候拓展视野;中军可直接派斥候、或经传令兵令部队派斥候。斥候避敌绕道、不靠近,到点/受阻即返回汇报(部队派的回部队、中军派的回中军)。
- 战报四类失真:延迟(全程)、丢失=纯沉默(传令兵被拦截)、残缺(兵力/兵种「?」)、失真(数字夸大,「存疑」)。
- 副将按性格/基调解读意图,可能偏离你的本意(第二层迷雾)。
- 敌方基于自己的(同样滞后的)认知世界用 Utility AI 决策(对称迷雾)。
- `peek` 复盘揭示真相全貌,与你战中所信的对照——常常天差地别。

## 范围(原型刻意不做,留给完整层)

战略层 / 生涯层 / 多轴失败系统(士气·粮草·经费·皇恩·SAN)/ 将领养成 / 全战式复盘可视化 / 中军帐实景 UI —— 见设计文档第三~十七节。原型只验证战术内核好不好玩。
