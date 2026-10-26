# Steam 创意工坊发布

公开条目：[Saber 战斗演出 / Saber Combat Presentation](https://steamcommunity.com/sharedfiles/filedetails/?id=3827762528)。首次发布版本为 0.3.8；`published.json` 保存 Steam 返回的公开状态与上传载荷校验值。

`workshop.json` 保存公开标题、说明、可见性、更新说明与依赖。`image.png` 和 `previews/` 均来自实际游戏截图，其中卡美洛截图仅转换为 JPEG 以满足 Steam 每张预览小于 1 MB 的要求。

发布工作区位于相邻目录 `SaberPresentationWorkshop/`，其 `content/` 平放当前 `dist/<版本>/SaberPresentation/` 的成品。准备脚本核对全部文件的数量和 SHA-256，避免把旧文件混入工坊版本。Steam 当前登录账号需拥有游戏。

```powershell
# 生成并校验成品后准备工作区。
./tools/package.ps1 -Install
python -X utf8 tools/verify_release.py
./tools/publish_workshop.ps1

# 将准备好的版本上传到 Steam 创意工坊。
./tools/publish_workshop.ps1 -Upload
```

首次发布成功后，`mod_id.txt` 会从工作区同步至此目录。后续上传始终复用此 ID，以更新现有条目。上传器日志位于相邻 `ModUploader-win-x64/mod-uploader.log`；上传结束应确认 `Successfully uploaded`，并核查公开工坊页面。

官方工具与格式说明：https://github.com/megacrit/sts2-mod-uploader
