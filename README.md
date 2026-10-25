# Saber 战斗演出扩展

对现有 Saber 外观模组的本地二创扩展，参考 Chaos Zero Nightmare - Nine 的战斗演出组织方式，以及用户指定的 FGO 官方阿尔托莉雅介绍视频。侧重动作编排、特效和日语配音。

## 0.3.0：完整交互演出

- 卡牌悬停、手柄聚焦时出现左侧动态大立绘，攻击与技能使用不同姿态；呼吸、发梢和衣摆持续运动。
- 拖拽选目标时保持预备动作，取消后回收立绘、恢复骨架；出牌时播放绘制攻击帧并收招。
- 六类精细 Saber 卡面，16 格绘制特效，宝具蓄力、光束、卡梅洛舞台和独立败北 CG；原有线条特效已移除。
- 继续使用 Saber 的 28 段日配，加入休息、锻造和商店事件回应。
- 接入 CZN Mod Config 的「Saber 演出」分类，提供 17 个开关／滑块。
- 接入 CZN Main Menu 的已安装角色背景目录，提供「Saber · 阿尔托莉雅」动态背景。
- 同时启用千鹤等铁甲皮肤时，可优先保留原 Saber 模型、头像、卡面及专属演出。

完整对照表见 `CZN_PARITY.md`。这次生成的图片、提示词和校验记录保存在 `assets/generated/`，使用内置 image_gen 工具制作。

## 使用

需要原作者「会画画的Small_day」的 [Saber 外观模组](https://steamcommunity.com/sharedfiles/filedetails/?id=3747659940)。保留原模组，在游戏 `mods/SaberPresentation/` 放入同名 DLL、JSON、PCK 三个文件。启动游戏，启用 Saber 和「Saber 战斗演出 / Saber Combat Presentation」，重启后选择铁甲战士。程序通过骨架内的 `attackvfff` 动画识别 Saber，其他角色与其他铁甲皮肤不会触发本扩展。

扩展没有修改原作者的 Workshop 文件。基础演出只需要 Saber；启用 [CznCore](https://steamcommunity.com/sharedfiles/filedetails/?id=3826011578) 和 [Chaos Zero Nightmare - Nine](https://steamcommunity.com/sharedfiles/filedetails/?id=3815657213) 后，0.2.0 会自动使用其中的战斗素材。订阅后仍需在游戏的模组界面启用并重启。原模组的模型、选人、商店、休息点外观仍由原模组提供。

要获得用户参考图中的整套界面与战场，同时启用：

- [CZN Style UI](https://steamcommunity.com/sharedfiles/filedetails/?id=3771060891)：卡框、能量与战斗 HUD、地图、商店、菜单等通用界面及交互。
- [CZN Main Menu](https://steamcommunity.com/sharedfiles/filedetails/?id=3774940754)：动态主菜单、背景选择和音乐库。
- [CZN Enemy Skin](https://steamcommunity.com/sharedfiles/filedetails/?id=3783041069)：敌人、Boss、敌方动画／音效与战场。

本机已订阅这些模组。`tools/enable_czn_suite.ps1` 会先备份普通游戏配置，再启用上述套装和 Saber，并选择 Saber 动态背景；现有其他模组的开关保留。关闭游戏后运行，重启生效。打包版提供同一脚本 `ENABLE_CZN_SUITE.ps1`。

## CZN 素材增强（0.2.0）

根据本机的 CznCore 1.0.1 / Nine 2.0.0-beta 验证。读取已启用 PCK 的原始资源，并按 Saber 的动作调整位置、尺寸、播放速度与轻微色调：

- 突进的起步、落点烟尘；挥剑粒子；普通、重击、跃斩的不同命中层。
- 三层蓝色球形护盾、格挡与破盾光效，强化时的魔力光环。
- 剑击、重击、宝具、护盾与强化的音效层。

28 段角色配音继续使用 Saber 的日配。金色剑光、蓄力、立绘和宝具光束继续由本扩展制作。缺少某项 CZN 资源时自动使用原有效果；`UseCznEffects=false` 可关闭复用。

本扩展不绑定 CZN 程序集进行编译。通过已安装资源路径、CZN 扩展描述符协议、背景元数据及可选兼容钩子接入，不重复打包其素材。资源路径和来源校验记录见 `CZN_ASSETS.json`；源包升级或改名后可能需要更新映射。

## 演出

| 场景或卡牌 | 动作与特效 |
|---|---|
| 普通攻击 | 普通斩击、突进与组合斩交替，近身位移、蓝白风痕、金色命中剑光 |
| Pommel Strike、Headbutt | 前刺突进与风刃 |
| Uppercut、Thunderclap | 上挑动作编排与竖向剑光 |
| Hemokinesis、Stomp、Breakthrough | 跃起、下劈与重击爆光 |
| Whirlwind、Twin Strike、Sword Boomerang、Thrash | 横扫、组合攻击与逐段命中特效 |
| Bash、Mangle、Tear Asunder、Perfected Strike | 重劈与命中震动 |
| Bludgeon、Fiend Fire、Pact's End | 宝具演出：举剑蓄光 → 立绘切入 → 誓约胜利之剑金色光束 |
| 获得格挡、格挡受击 | 蓝色六边形护盾；启用 CZN 后使用球形护盾、格挡与破盾特效 |
| 施法、强化 | 原皮肤举剑动画、魔力光环与升腾光点 |
| 受击 | 原皮肤受击动作、火花与日语受击语音 |
| 开战、胜利、死亡 | 日语角色语音 |

七类攻击路线是对原皮肤已有 Spine 动画的动作编排，搭配位移、短暂升降和旋转；不是从 FGO 视频完整提取并移植的新骨骼动画。演出在攻击与真实命中时触发，不改变伤害、费用、目标、命中次数、牌池或随机数。随机攻击只在原游戏已经选好目标后生成对应的命中光效。

## 日语语音

目前收录 28 个日语语音片段，覆盖攻击、重击、技能、开战、受击、死亡、胜利、宝具蓄力与释放。同类语音轮换播放；普通语音避免重叠，宝具优先播放；音量跟随游戏的主音量和音效音量。

立绘及语音从 Atlas Academy 的 JP 阿尔托莉雅资料接口定位获取：

- [JP 角色资料接口](https://api.atlasacademy.io/nice/JP/servant/2?lore=true)
- [用户提供的 FGO 官方参考视频](https://www.bilibili.com/video/BV1wA4m1A7ZD/)
- 每个文件的原始链接和 SHA-256 记录在 `assets/sources.json`。

原始角色、美术和声音版权归 TYPE-MOON / FGO 相关权利方；原 Saber 皮肤归原作者。CznCore / Nine 模组作者为 Pelleas，CZN 原始素材归其相关权利方。本项目是本地非官方粉丝扩展，未上传到创意工坊，安装包不包含 CZN 素材、DLL 或反编译代码。

## 设置

首次加载生成 `%APPDATA%/SlayTheSpire2/mod_configs/SaberPresentation.json`。退出游戏后编辑，重启生效：

- `Enabled`：整个扩展开关。
- `Dash`：接近目标的位移。
- `RichMotion`：上挑、跃斩、前刺的附加动作编排。
- `UseCznEffects`：使用已启用的 CZN 素材，默认开启；无相应源包时自动回退。
- `CardHoverPresentation`、`ReadyBodyMotion`：选牌大立绘与战斗模型预备动作。
- `SaberCardArt`、`HoverVoice`：专属卡面与选牌日配回应。
- `PortraitScale`、`PortraitOpacity`、`PortraitOffsetX`、`PortraitOffsetY`：左侧立绘尺寸、透明度和位置。
- `PreferSaber`：与其他铁甲皮肤同时启用时优先使用 Saber；更换模型需重启游戏。
- `Cinematics`：宝具立绘切入。关闭后仍保留光束、蓄光与配音。
- `ScreenShake`、`ScreenFlash`：震屏与轻微闪光。
- `JapaneseVoice`、`VoiceVolume`、`EffectsVolume`：语音开关、语音和附加音效音量。
- `NoblePhantasmCards`：触发宝具演出的卡牌 ID 列表，默认 `BLUDGEON`、`FIEND_FIRE`、`PACTS_END`。

不将宝具制作成额外的可点击技能，不添加 NP/怒气资源，不改变原版数值。配置只是为原有卡牌选择对应的演出。

## 开发

目标游戏：本机 `v0.111.0`，.NET 9。项目使用当前游戏 DLL 作为引用。

```powershell
python -X utf8 tools/prepare_assets.py
dotnet build SaberPresentation.csproj -c Release
```

`build/SaberPresentation.pck` 内仅打包 `SaberPresentation/` 下的 MP3、立绘导入映射，以及独立命名的 `.godot/imported/saber_artoria-*` WebP CTEX，不覆盖原版资源。

自动验证代码只在 `SABER_TEST` 编译符号下编入，正式 DLL 不含测试入口。测试运行器使用独立用户目录 `SlayTheSpire2-SaberPresentation-Test`，强制关闭 Steam，通过真实游戏战斗验证路线、数值不变、随机与多段攻击、动作归位、音频解码及特效清理；截图和结果保存在 `build/verification/`。

```powershell
dotnet build SaberPresentation.csproj -c Release -p:DefineConstants=SABER_TEST -o build/test-dll
./tools/prepare_test_runner.ps1
./build/runner/SlayTheSpire2.exe --force-steam=off --saber-test --windowed --resolution 1280x720

# 带 CZN 的第二组实机检查；结果保存在 build/verification-czn/。
./tools/prepare_test_runner.ps1 -WithCzn
./build/runner-czn/SlayTheSpire2.exe --force-steam=off --saber-test --saber-czn-test --windowed --resolution 1280x720

# 正式编译、打包并安装。正式 DLL 不含上面的测试入口。
./tools/package.ps1 -Install

# 启用已订阅的 CZN 通用界面／主菜单／敌人套装，并选择 Saber 背景。
./tools/enable_czn_suite.ps1

# 复现全部 18 个本机订阅模组同时加载的检查。
./tools/prepare_test_runner.ps1 -WithAllMods
./build/runner-all/SlayTheSpire2.exe --force-steam=off --saber-test --saber-all-test --windowed --resolution 1280x720
```

`build/runner/` 中的游戏资源是本机游戏的硬链接/联接，不要将它们随模组发布或在这些链接内修改游戏内容。

## Git

仓库位于当前项目目录，`v0.1.0` 保存创作前基线，`v0.2.0` 保存第一轮素材增强版，`v0.3.0` 保存本次完整交互版，版本变化见 `VERSION_HISTORY.md`。源文件、生图素材、提示词、素材来源和已验证安装包纳入本地 Git；游戏本体、测试运行器和配置备份排除。当前没有配置远程仓库。

`dist/SaberPresentation-0.1.0.zip` 保留旧安装包；解压覆盖本扩展的三个安装文件即可回退。
