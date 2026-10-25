# Saber 独立演出模组 0.3.0

只安装 **SaberPresentation** 一个文件夹即可运行。无前置模组，不需要另外安装原 Saber、Nine、CznCore 或 CZN 界面套装。适配 Slay the Spire 2 v0.111.0，替换铁甲战士的外观与演出，保留原有玩法和数值。

## 安装

解压 `SaberPresentation-0.3.0.zip`，将整个 `SaberPresentation` 文件夹放进游戏 `mods/`，在游戏模组菜单启用并重启。**保留文件夹内的 `bundled/` 子目录**，它是成品自带的呈现运行库与资源。

本机开发时可运行 `./tools/package.ps1 -Install -EnableStandalone`。切换脚本先备份配置，再启用成品、关闭被它替代的旧 Saber 和 CZN 源模组及其角色扩展；其他模组保留原设置。它不会删除创意工坊文件。备份位于项目的 `build/config-backups/`。首次启动选择 Saber 卡梅洛动态主菜单，原菜单配置备份为 `settings.cfg.before-saber-standalone`。

## 演出

| 操作／场景 | 实现 |
|---|---|
| 悬停、手柄聚焦卡牌 | 左侧大立绘滑入、呼吸与衣摆动画；模型进入攻击或施法预备姿态 |
| 拖拽、瞄准、取消 | 保持选牌立绘，取消后收回并恢复模型 |
| 出牌 | 三张精细绘制动作帧依次播放、前移与收招；六类 Saber 卡面 |
| 普攻与技能 | 斩击、突刺、上挑、跃斩、横扫连击、重劈和光束，配合原骨架、近身位移与逐段命中特效 |
| 护盾、格挡、破盾、受击、强化 | 内置 CZN Spine 特效与 Saber 日语反馈 |
| **攻击牌击杀敌人** | **Saber CG → 完整日语蓄力 → Excalibur → 金色光束与命中爆光** |
| 非致死攻击 | 正常出招；三类重型宝具牌保留短光束，长 CG 只在击杀时触发 |
| 群攻／多段击杀 | 同一张牌只触发一次击杀演出 |
| 开战、胜利、死亡、休息、锻造、商店 | 日语回应；胜败结算 CG |
| 全局界面、卡框、HUD、地图、商店、配置 | 内置 CznStyleUI 与 CznCore 通用呈现组件 |
| 敌人、Boss、战场、敌方音效 | 内置 CznEnemySkin 呈现组件 |
| 动态主菜单、背景与音乐选择 | 内置 CznMainMenu，新增 Saber 卡梅洛背景 |

击杀依据游戏已经结算的伤害结果，包括格挡、减伤和伤害转移；不会为预判伤害重复运行规则或提前抽取随机目标。演出不改变伤害、费用、命中次数、牌池或随机数。格挡挡住伤害、攻击未击杀时不会误播长 CG。

战斗模型使用原 Saber 皮肤已有的 9 个 Spine 动画片段；七类路线加入位移、升降与节奏编排。左侧大立绘采用新绘制的动作帧和网格动画。不是将 Nine 本人或 FGO 官方视频的整套骨骼逐帧移植。Nine 的卡组、怒气和专属数值不加入铁甲战士。

## 日配与美术

内置 28 段阿尔托莉雅日语原声，来自 Atlas Academy JP 资料定位的 FGO 音频。普通语音轮换并避免重叠；完整宝具蓄力后才释放。音量跟随游戏主音量和音效音量。

六类卡面、透明立绘、攻击动作表、16 格效果图、卡梅洛舞台及败北 CG 是本次生成的位图。提示词与来源在 `PROMPTS.md`、`GENERATED_ASSETS.json`。特效使用绘制贴图和 Spine，不使用早期版本的 Line2D 剑光。

第三方组件及原始素材署名见 `THIRD_PARTY_NOTICES.md`。`ASSET_SOURCES.json` 记录日配链接和校验值；`STANDALONE_COMPONENTS.json` 记录内置组件的版本、来源与 SHA-256。本项目是用户本地二创成品，未上传创意工坊。

## 设置

游戏设置的 CZN Mod Config 中有 **Saber 演出** 分类，18 个开关／滑块可即时调整。模型替换选项重启后完整应用。配置文件为 `%APPDATA%/SlayTheSpire2/mod_configs/SaberPresentation.json`。

- `CardHoverPresentation`、`ReadyBodyMotion`、`SaberCardArt`、`HoverVoice`：选牌与出牌呈现。
- `PortraitScale`、`PortraitOpacity`、`PortraitOffsetX`、`PortraitOffsetY`：立绘大小、透明度和位置。
- `KillingBlowExcalibur`：击杀咖喱棒，默认开启。
- `Cinematics`：击杀全屏 CG；关闭后保留短光束和释放日配。
- `Dash`、`RichMotion`、`UseCznEffects`、`ScreenShake`、`ScreenFlash`：位移、动作、内置特效、震屏和闪光。
- `JapaneseVoice`、`VoiceVolume`、`EffectsVolume`：日配和音量。
- `NoblePhantasmCards`：短光束路线的卡牌，默认 `BLUDGEON`、`FIEND_FIRE`、`PACTS_END`；击杀 CG 适用于所有 Saber 攻击牌。
- `Enabled`、`PreferSaber`：Saber 演出与同职业皮肤优先设置。

主菜单、通用界面和敌人演出可在各自配置分类调整。保留原 CZN 模组时会共享已加载程序集并避免重复初始化；推荐只启用这个成品及其他无关模组。

## 开发与验证

```powershell
python -X utf8 tools/prepare_assets.py
python -X utf8 tools/prepare_standalone.py
dotnet build SaberPresentation.csproj -c Release

# 仅安装一个成品模组的隔离实机检查
dotnet build SaberPresentation.csproj -c Release -p:DefineConstants=SABER_TEST -o build/test-dll
./tools/prepare_test_runner.ps1 -Standalone
./build/runner-standalone/SlayTheSpire2.exe --force-steam=off --saber-test --saber-standalone-test --windowed --resolution 1280x720
```

测试使用独立用户目录 `SlayTheSpire2-SaberPresentation-Test`，不操作正常存档。正式 DLL 不含测试入口。结果与截图在 `build/verification-standalone/`。覆盖原生选牌／拖拽／取消、真实出牌、七类攻击、随机／群攻／多段、伤害保持、28 段音频、击杀 CG、模型归位、特效清理、菜单配置与原生死亡结算。

构建从本机订阅源包提取依赖，成品运行时不查找 Workshop 路径。Nine 仅提取实际使用的效果及其依赖，无 Nine 角色 DLL。通用界面、敌人和菜单的原二进制组件保持完整并记录哈希。

Git 在创作前已建立：`v0.1.0` 为初版，`v0.2.0` 为素材增强版；`c359431` 保存独立化前的完整交互检查点，本次独立化在 `enhancement/standalone-finisher` 分支进行。代码、新美术、提示词、来源锁定清单与验证报告受版本管理。大型第三方运行包和成品 ZIP 位于 `dist/`，通过清单及校验值锁定；游戏本体、测试运行器、配置备份不提交。当前无远程仓库。
