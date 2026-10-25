# Saber 独立角色演出模组 0.3.5

只安装 **SaberPresentation** 一个文件夹即可运行，无前置模组。适配 Slay the Spire 2 v0.111.0，替换铁甲战士外观与角色演出，保留原有玩法与数值。

## 安装

退出游戏后，将 `SaberPresentation-0.3.5.zip` 中的 `SaberPresentation` 文件夹放入游戏 `mods/`，启用并重启。

从 0.3.0／0.3.1 升级时，用新文件夹替换旧文件夹，移除旧 `bundled/` 内的 CznCore、CznStyleUI、CznMainMenu 和 CznEnemySkin DLL/PCK。本机安装脚本会按文件名清理这些旧载荷，保留缓存和旧版成品以便回滚。

```powershell
./tools/package.ps1 -Install -EnableStandalone
```

`EnableStandalone` 先备份配置，再启用本模组并关闭已被它替代的旧 Saber 和 CZN 源模组；其他无关模组保留原设置，创意工坊文件不删除。

## 角色演出

| 操作／场景 | 实现 |
|---|---|
| 悬停、手柄聚焦攻击牌 | 左侧分层角色滑入并连续待机；场内 Saber 举剑蓄力定格，不提前出现剑光或粒子 |
| 悬停、手柄聚焦技能／防御牌 | 左侧分层角色保持待机；场内 Saber 剑尖插地进入守势，不播放特效 |
| 移开、拖拽、瞄准、取消 | 鼠标移开或取消立即收回场内姿态；瞄准期间保持预备层，取消后恢复普通待机 |
| 正式出牌 | 左侧大立绘约 170 ms 退场，场内模型接管攻击并播放前后景／目标特效；不切换整张人物图，不让大立绘伪造挥剑 |
| 普攻与技能 | 斩击、突刺、上挑、跃斩、横扫连击、重劈和光束，逐段命中特效与日配 |
| 移动与命中 | Nine 的 Spine 烟尘、挥击、冲击和宝具效果资源，按 Saber 的位置、尺寸和节奏调整 |
| 护盾、格挡、破盾、强化、受击 | CznCore 特效资源、原生骨架动作和 Saber 日语反馈 |
| 攻击牌击杀敌人 | 官方 FGO 动画片段与原日语音轨 → 从剑部发射 Excalibur 光束和命中爆光 |
| 群攻／多段击杀 | 同一张牌只触发一次长 CG |
| 非致死攻击 | 正常出招；指定重型宝具牌保留短光束 |
| 开战、胜利、死亡、休息、锻造、商店 | 日语回应；败北使用 FGO Memorial Movie 2023 的真实动画片段 |
| Saber 整局音乐 | Fate 动漫《EMIYA_UBW Extended》纯音乐循环；跨房间连续播放，日配时自动压低，CG 时暂停 |

金币、卡框、卡图、能量、血条、结束回合、地图、商店、设置和主菜单使用原版。角色头像仍为 Saber。能量仅提高绘制层级，保证大立绘出现时数值可见。敌人、Boss、战场及敌方音效使用原版。卡图制作继续暂停。

击杀依据游戏已结算的伤害结果，包括格挡、减伤和伤害转移；不重复运行伤害规则、不提前抽取随机目标。演出不改变伤害、费用、命中次数、牌池或随机数。

战斗模型使用原 Saber 皮肤的 9 个 Spine 动画片段，七类攻击路线加入位移、升降与节奏编排。左侧高清立绘已重做成原生 Skeleton2D 分层骨骼：头部／身体与刚性手臂、双手、完整剑始终同时显示，纹理不换，呼吸、头发、发带和裙摆由 Bone2D 与 AnimationPlayer 连续驱动。Nine 的骨骼数据用于核查连续待机；千鹤完整演示用于核查预备层与场内攻击的职责分离，见 `NINE_PORTRAIT_REVIEW.md` 和 `CHIZURU_PRESENTATION_REVIEW.md`。没有移植角色骨架、卡组、怒气或数值。

0.3.5 将左侧角色等比缩至上一版的约 73.5%，重新定位到左下角。实际加载 Nine 的 Spine 场景并采样其 355 根骨骼；再逐帧检查千鹤的本地模组场景、GIF 与完整演示，确认大立绘只承担悬停待机，出牌时由场内模型和前后景特效接管。Saber 左侧角色只保留 2.24 秒循环待机；攻击牌悬停使用原战斗骨骼 30% 的无特效蓄力帧，技能／防御牌使用施法骨骼 90% 的无特效插地守势帧。移开立即回到 `idle_loop`，正式出牌才释放攻击特效。

## 素材与设置

内置 28 段阿尔托莉雅 FGO 日语原声、两段真实 CG 动画和《EMIYA》纯音乐，来源记录在 `ASSET_SOURCES.json` 与 `THIRD_PARTY_NOTICES.md`。宝具动画裁取原片 1138×456 区域，使用电影画幅去除平台角标和原游戏宝具标题／等级文字，保留完整日语音轨；败北动画裁取 1138×640。两者编码为 1280×720 Theora，不是 4K 源，不叠加结算文字。CG 可按 Esc 跳过。身体与头部骨骼图为 1024×1536 RGBA；手臂、护臂、双手握把和完整剑使用 1536×1024 透明切片图。提示词与来源见 `assets/generated/PROMPTS_RIG.md` 和 `assets/generated/provenance.json`。V2／V3／V4 独立动作图、旧动作表和旧整组手臂层全部退出成品，卡图制作继续暂停。

`STANDALONE_COMPONENTS.json` 锁定 24 个 CZN 特效入口及 142 个资源依赖，含 Spine 骨架、图集、贴图、导入映射和音效。它们由游戏原生加载器播放；不打包或加载 CZN 程序集、界面或主菜单。本包只含一个运行 DLL 和一个 PCK，运行时不查找 Workshop 路径。

设置文件为 `%APPDATA%/SlayTheSpire2/mod_configs/SaberPresentation.json`，退出游戏后可编辑：

- `CardHoverPresentation`、`ReadyBodyMotion`、`HoverVoice`：选牌反馈。
- `PortraitScale`、`PortraitOpacity`、`PortraitOffsetX`、`PortraitOffsetY`：立绘大小、透明度和位置。
- `KillingBlowExcalibur`、`Cinematics`：击杀咖喱棒与全屏 CG。
- `Dash`、`RichMotion`、`UseCznEffects`、`ScreenShake`、`ScreenFlash`：动作与特效。
- `JapaneseVoice`、`VoiceVolume`、`EffectsVolume`：日配与音量。
- `AnimeBackgroundMusic`、`MusicVolume`：动漫纯音乐及额外增益。音乐跟随游戏的主音量／音乐音量，仅作用于 Saber 对局。设为 `false` 后重启恢复原版配乐。
- `NoblePhantasmCards`：短光束卡牌，默认 `BLUDGEON`、`FIEND_FIRE`、`PACTS_END`。
- `Enabled`、`PreferSaber`：Saber 演出与皮肤优先。

旧版通用界面、卡图和菜单选项在升级时移出配置；原版界面始终保留。

## 开发与验证

```powershell
python -X utf8 tools/prepare_assets.py
python -X utf8 tools/prepare_standalone.py
dotnet build SaberPresentation.csproj -c Release -p:DefineConstants=SABER_TEST -o build/test-dll
./tools/prepare_test_runner.ps1 -Standalone
$env:APPDATA = "$PWD/build/test-appdata/Roaming"
$env:SABER_TEST_ISOLATED = '1'
./build/runner-standalone/SlayTheSpire2.exe --force-steam=off --saber-test --saber-standalone-test --windowed --resolution 1600x900
```

测试使用项目内独立 APPDATA 与显式隔离标记，不操作正常存档。正式 DLL 不含测试或演示入口。覆盖原生选牌、拖拽、取消、真实出牌、七类攻击、随机／群攻／多段、数值保持、28 段日配、两段动态 CG、角色归位、剑尖边界、特效清理、原版金币／卡框／能量／菜单与原生死亡结算。音乐检查包括解码、跨更新连续播放、原曲替换、音乐静音、日配压低、CG 暂停和恢复。

构建使用 SHA-256 校验通过的本地源缓存，退订源模组后仍可构建。源码、新美术、提示词、来源与验证报告受 Git 管理，旧版标签可回滚；大型源缓存、测试运行器与成品 ZIP 留在本机，通过锁定清单和校验值记录。当前无远程仓库。
