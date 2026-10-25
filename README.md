# Saber 战斗演出扩展

对现有 Saber 外观模组的本地二创扩展，参考 Chaos Zero Nightmare - Nine 的战斗演出组织方式，以及用户指定的 FGO 官方阿尔托莉雅介绍视频。侧重动作编排、特效和日语配音。

## 使用

需要原作者「会画画的Small_day」的 [Saber 外观模组](https://steamcommunity.com/sharedfiles/filedetails/?id=3747659940)。保留原模组，在游戏 `mods/SaberPresentation/` 放入同名 DLL、JSON、PCK 三个文件。启动游戏，启用 Saber 和「Saber 战斗演出 / Saber Combat Presentation」，重启后选择铁甲战士。程序通过骨架内的 `attackvfff` 动画识别 Saber，其他角色与其他铁甲皮肤不会触发本扩展。

扩展没有修改原作者的 Workshop 文件，不需要 Nine、CznCore 或 BaseLib。原模组的模型、选人、商店、休息点外观仍由原模组提供。

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
| 获得格挡 | 蓝色六边形护盾 |
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

原始角色、美术和声音版权归 TYPE-MOON / FGO 相关权利方；原 Saber 皮肤归原作者。本项目是本地非官方粉丝扩展，未上传到创意工坊，也不包含 Nine 的素材或反编译代码。

## 设置

首次加载生成 `%APPDATA%/SlayTheSpire2/mod_configs/SaberPresentation.json`。退出游戏后编辑，重启生效：

- `Enabled`：整个扩展开关。
- `Dash`：接近目标的位移。
- `RichMotion`：上挑、跃斩、前刺的附加动作编排。
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

`build/SaberPresentation.pck` 内仅打包 `SaberPresentation/` 命名空间的 MP3、立绘导入映射与 WebP CTEX，不覆盖原版资源。

自动验证代码只在 `SABER_TEST` 编译符号下编入，正式 DLL 不含测试入口。测试运行器使用独立用户目录 `SlayTheSpire2-SaberPresentation-Test`，强制关闭 Steam，通过真实游戏战斗验证路线、数值不变、随机与多段攻击、动作归位、音频解码及特效清理；截图和结果保存在 `build/verification/`。

```powershell
dotnet build SaberPresentation.csproj -c Release -p:DefineConstants=SABER_TEST -o build/test-dll
./tools/prepare_test_runner.ps1
```

`build/runner/` 中的游戏资源是本机游戏的硬链接/联接，不要将它们随模组发布或在这些链接内修改游戏内容。
