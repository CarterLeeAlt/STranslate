## 更新

- 添加：文本翻译预装 DeepSeek 服务，支持思考模式开关（开启时使用中等推理强度）
- 添加：文本识别预装 DeepSeek OCR 服务，同样支持思考模式开关
- 添加：划词翻译改为三击 Ctrl 触发，基于原始键盘输入识别，并在监听失效时自动恢复
- 添加：主窗口贴顶自动收起，可配置收起等待时间
- 添加：主窗口唤出时上浮渐显动画
- 添加：图片翻译精简窗口支持贴图，贴图窗口可切换原图与译文图
- 添加：一键重新翻译开关，以及增量翻译时清空输入选项
- 添加：取词失败时可选仅发送托盘通知
- 添加：新增乌克兰语界面
- 添加：内置 Noto Sans SC 字体并设为默认界面字体
- 优化：大模型翻译与 OCR 不再发送温度参数，并移除温度设置，避免推理模型拒绝请求
- 优化：OpenAI、智谱、DeepSeek 只预置一个最新的快速模型（gpt-6-luna、glm-4.7-flash、deepseek-flash）
- 优化：增量翻译默认键设为 F4；与 Alt/Ctrl/Shift/Win 组合时原样放行，Alt+F4 等组合键照常可用
- 优化：主窗口最大高度默认改为屏幕工作区的 75%
- 优化：提示条与各窗口内容区顶部对齐
- 优化：初始化向导默认选中微软翻译，完成时自动添加已选服务
- 修复：三击 Ctrl 后主窗口闪现即消失，或切走后不再自动隐藏
- 修复：取词失败后再次唤出时主窗口高度未更新、翻译结果区被截断
- 修复：OCR 选择"高"图片质量时报错；现自动无损转为 PNG 后识别
- 修复：贴顶收起的感应条出现在 Alt+Tab 中，以及收起状态指示不可见
- 修复：主窗口可被最大化
- 修复：初始化向导中服务设置被裁切
- 修复：有道 API 错误信息未本地化

## 其他

- [插件市场](https://stranslate.zggsong.com/plugins.html)
- [使用说明](https://stranslate.zggsong.com/docs/)
- [集成调用](https://stranslate.zggsong.com/docs/invoke.html)
- [安装卸载](https://stranslate.zggsong.com/docs/(un)install.html)
- [FAQ](https://stranslate.zggsong.com/docs/faq.html)

**完整更新日志:** [v2.0.10...v2.0.11](https://github.com/CarterLeeAlt/STranslate/compare/2a75118fe0fa...v2.0.11)
