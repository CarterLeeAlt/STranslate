## 更新

- 新增：智谱文本翻译默认模型改为 GLM-5.3-Flash，思考强度支持 low / high / max 三档，默认 low
- 改进：严格便携模式，配置、日志、缓存、历史、插件与临时文件固定写入程序目录 `PortableConfig`，不再写入 `%APPDATA%`；每次启动自动清空 `PortableConfig\Temp`
- 移除：内置自动更新与便携模式切换；发布包改为普通便携 zip，不再附带安装器、`Update.exe` 与更新元数据，升级时直接解压新版覆盖
- 移除：关于页“检查更新”与通用页“自动检查更新”

## 其他

- [插件市场](https://stranslate.zggsong.com/plugins.html)
- [使用说明](https://stranslate.zggsong.com/docs/)
- [集成调用](https://stranslate.zggsong.com/docs/invoke.html)
- [安装卸载](https://stranslate.zggsong.com/docs/(un)install.html)
- [FAQ](https://stranslate.zggsong.com/docs/faq.html)

**完整更新日志:** [v2.0.15...v2.0.16](https://github.com/CarterLeeAlt/STranslate/compare/v2.0.15...v2.0.16)
