## 更新

- 修复：三击左 Ctrl 划词翻译可能永久失灵（外部程序吞掉按键松开事件导致检测状态卡死；现空闲 5 秒后自动恢复，无需重启软件）
- 修复：三击弹出窗口后键盘焦点可能停留在结果区的设置按钮上（隐藏输入框时焦点现在落在译文上，可直接 Ctrl+A / Ctrl+C 复制）
- 修复：主窗口失焦自动隐藏在窗口关闭时序下可能崩溃退出，并丢失进行中的翻译
- 改进：代理与系统代理完全隔离——软件选择直连时即使系统代理开启也保持直连（含检查更新流量），默认并锁定为直连
- 改进：流式翻译请求记录耗时日志（首字节延迟、总耗时、是否取消），便于区分服务端延迟与程序问题

## 其他

- [插件市场](https://stranslate.zggsong.com/plugins.html)
- [使用说明](https://stranslate.zggsong.com/docs/)
- [集成调用](https://stranslate.zggsong.com/docs/invoke.html)
- [安装卸载](https://stranslate.zggsong.com/docs/(un)install.html)
- [FAQ](https://stranslate.zggsong.com/docs/faq.html)

**完整更新日志:** [v2.0.13...v2.0.15](https://github.com/CarterLeeAlt/STranslate/compare/v2.0.13...v2.0.15)
