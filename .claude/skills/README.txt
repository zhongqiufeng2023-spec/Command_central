《中军帐》Claude Code 技能包
============================

本包内有 3 个技能文件夹：
  command-tent-core/      —— 核心设定速查(中枢，几乎总会触发)
  command-tent-csharp/    —— C# 引擎无关内核规范(写代码时触发)
  command-tent-systems/   —— 三大签名系统规格(做具体机制时触发，带 references/)

【放进 Claude Code】
把上面这 3 个文件夹复制到你项目的 .claude/skills/ 下：

  mkdir -p <你的游戏仓库>/.claude/skills
  cp -r command-tent-core command-tent-csharp command-tent-systems \
        <你的游戏仓库>/.claude/skills/

放好后结构应为：
  .claude/skills/
  ├── command-tent-core/SKILL.md
  ├── command-tent-csharp/SKILL.md
  └── command-tent-systems/
      ├── SKILL.md
      └── references/{intel-pipeline,combat-resolution,multi-axis-pressure}.md

【用法】
- 自动：相关时 Claude Code 会自动加载对应技能。
- 手动：输入 /command-tent-core 、/command-tent-csharp 、/command-tent-systems 直接调用。

【时机提醒】
若启动 Claude Code 时 .claude/skills/ 目录尚不存在，新建后需重启一次 Claude Code；
之后再改 SKILL.md 即可热加载，无需重启。

注：本 zip 是给 Claude Code(文件夹形式)用的。
claude.ai 网页版 / API 请改用单独的 .skill 文件(已另行提供)。
