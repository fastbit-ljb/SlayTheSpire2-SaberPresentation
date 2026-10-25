# Saber 独立角色演出模组 0.3.2

只安装 **SaberPresentation** 一个文件夹即可运行，无前置模组。适配 Slay the Spire 2 v0.111.0，替换铁甲战士外观与角色演出，保留原有玩法与数值。

## 安装

退出游戏后，将 `SaberPresentation-0.3.2.zip` 中的 `SaberPresentation` 文件夹放入游戏 `mods/`，启用并重启。

从 0.3.0／0.3.1 升级时，用新文件夹替换旧文件夹，移除旧 `bundled/` 内的 CznCore、CznStyleUI、CznMainMenu 和 CznEnemySkin DLL/PCK。本机安装脚本会按文件名清理这些旧载荷，保留缓存和旧版成品以便回滚。

```powershell
./tools/package.ps1 -Install -EnableStandalone
```

`EnableStandalone` 先备份配置，再启用本模组并关闭已被它替代的旧 Saber 和 CZN 源模组；其他无关模组保留原设置，创意工坊文件不删除。

## 角色演出

| 操作／场景 | 实现 |
|---|---|
| 悬停、手柄聚焦卡牌 | 左侧大立绘滑入、呼吸与衣摆动画，小模型自然持剑待机 |
| 拖拽、瞄准、取消 | 保持选牌立绘，取消后收回并恢复模型 |
| 出牌 | 三张绘制动作帧顺次播放、前移和收招 |
| 普攻与技能 | 斩击、突刺、上挑、跃斩、横扫连击、重劈和光束，逐段命中特效与日配 |
| 移动与命中 | Nine 的 Spine 烟尘、挥击、冲击和宝具效果资源，按 Saber 的位置、尺寸和节奏调整 |
| 护盾、格挡、破盾、强化、受击 | CznCore 特效资源、原生骨架动作和 Saber 日语反馈 |
| 攻击牌击杀敌人 | 卡梅洛 CG → 完整日语蓄力 → Excalibur → 金色光束和命中爆光 |
| 群攻／多段击杀 | 同一张牌只触发一次长 CG |
| 非致死攻击 | 正常出招；指定重型宝具牌保留短光束 |
| 开战、胜利、死亡、休息、锻造、商店 | 日语回应，胜败结算 CG |

金币、卡框、卡图、能量、血条、结束回合、地图、商店、设置和主菜单使用原版。角色头像仍为 Saber。能量仅提高绘制层级，保证大立绘出现时数值可见。敌人、Boss、战场及敌方音效使用原版。卡图制作继续暂停。

击杀依据游戏已结算的伤害结果，包括格挡、减伤和伤害转移；不重复运行伤害规则、不提前抽取随机目标。演出不改变伤害、费用、命中次数、牌池或随机数。

战斗模型使用原 Saber 皮肤的 9 个 Spine 动画片段，七类攻击路线加入位移、升降与节奏编排；左侧大立绘使用绘制动作帧和网格动画。Nine 的专属骨架、卡组、怒气及数值未移植。

## 素材与设置

内置 28 段阿尔托莉雅 FGO 日语原声，来源记录在 `ASSET_SOURCES.json`。绘制立绘、动作帧、16 格特效、卡梅洛舞台与败北 CG 的来源见 `GENERATED_ASSETS.json` 和 `PROMPTS.md`。暂停使用的卡面草稿保留在 Git，成品不包含它。

`STANDALONE_COMPONENTS.json` 锁定 24 个 CZN 特效入口及 142 个资源依赖，含 Spine 骨架、图集、贴图、导入映射和音效。它们由游戏原生加载器播放；不打包或加载 CZN 程序集、界面或主菜单。本包只含一个运行 DLL 和一个 PCK，运行时不查找 Workshop 路径。

设置文件为 `%APPDATA%/SlayTheSpire2/mod_configs/SaberPresentation.json`，退出游戏后可编辑：

- `CardHoverPresentation`、`ReadyBodyMotion`、`HoverVoice`：选牌反馈。
- `PortraitScale`、`PortraitOpacity`、`PortraitOffsetX`、`PortraitOffsetY`：立绘大小、透明度和位置。
- `KillingBlowExcalibur`、`Cinematics`：击杀咖喱棒与全屏 CG。
- `Dash`、`RichMotion`、`UseCznEffects`、`ScreenShake`、`ScreenFlash`：动作与特效。
- `JapaneseVoice`、`VoiceVolume`、`EffectsVolume`：日配与音量。
- `NoblePhantasmCards`：短光束卡牌，默认 `BLUDGEON`、`FIEND_FIRE`、`PACTS_END`。
- `Enabled`、`PreferSaber`：Saber 演出与皮肤优先。

旧版通用界面、卡图和菜单选项在升级时移出配置；原版界面始终保留。

## 开发与验证

```powershell
python -X utf8 tools/prepare_assets.py
python -X utf8 tools/prepare_standalone.py
dotnet build SaberPresentation.csproj -c Release -p:DefineConstants=SABER_TEST -o build/test-dll
./tools/prepare_test_runner.ps1 -Standalone
./build/runner-standalone/SlayTheSpire2.exe --force-steam=off --saber-test --saber-standalone-test --windowed --resolution 1600x900
```

测试使用独立目录 `SlayTheSpire2-SaberPresentation-Test`，不操作正常存档。正式 DLL 不含测试或演示入口。覆盖原生选牌、拖拽、取消、真实出牌、七类攻击、随机／群攻／多段、数值保持、28 段日配、击杀 CG、角色归位、特效清理、原版金币／卡框／能量／菜单与原生死亡结算。

构建使用 SHA-256 校验通过的本地源缓存，退订源模组后仍可构建。源码、新美术、提示词、来源与验证报告受 Git 管理，旧版标签可回滚；大型源缓存、测试运行器与成品 ZIP 留在本机，通过锁定清单和校验值记录。当前无远程仓库。
