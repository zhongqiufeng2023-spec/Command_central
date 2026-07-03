---
name: command-tent-systems
description: 《中军帐》(The Command Tent) 三大签名系统的详细规格 —— 情报失真管线(延迟/丢失/残缺/失真四类 + 统一掷骰管线)、战术战斗结算(attrition 损耗公式 + 兵种克制三角 + 士气/体力隐藏值)、多轴压力失败系统(5 主轴 + 压力网 + 击穿→危机→结局)。当用户为《中军帐》/ Command Tent 设计、细化、平衡或实现这些具体机制的规则、公式、数值时,加载这个 skill,再按需读取对应的 references 子文件。请先加载 command-tent-core 拿全局上下文与术语表。本 skill 是规格参照,不重复 core 里已有的速查。
---

# 《中军帐》签名系统规格 (路由)

> 先决条件:已加载 `command-tent-core`(术语表、双世界模型、范围红线)。本 skill 把三大签名系统的**详细规则 / 公式**拆成下面的 references,**按需只读你正在做的那一个**,避免一次性吞掉全部规格、浪费 context。

## 何时读哪个

| 你在做… | 读这个 reference |
|---|---|
| 情报怎么延迟 / 丢失 / 残缺 / 失真,信息年龄、幽灵标记、矛盾情报、可信度提示 | `references/intel-pipeline.md` |
| 一场战斗怎么结算,损耗公式、兵种克制三角、士气 / 体力、胜负判定 | `references/combat-resolution.md` |
| 失败系统、5 主轴怎么互喂、评估值、击穿 → 危机 → 挽回 / 结局(**完整层**) | `references/multi-axis-pressure.md` |

## 跨系统的共识 (读任何 reference 前都成立)
- 三大系统**共享同一个注入的随机源**(`IRandomSource`,见 csharp skill §4),保证可测、可复现。
- 情报管线与 SAN 双向绑定:`SAN↓ → 失真↑`(失真管线把"你的 SAN"作为一个因子)。这是原型唯一可早期试水的完整层机制。
- **范围提醒**:`intel-pipeline` 与 `combat-resolution` 大部分属**原型战术核心**;`multi-axis-pressure` 整体属**完整层**(里程碑三 / 四),原型别实现,除非用户在做前瞻设计。
- 这些规格里的**具体数值 / 阈值 / 系数蓄意留白** —— 蓝图 §六明确说留到原型期边做边调。给数值时标注"建议起始值,待平衡",别假装是定数。
