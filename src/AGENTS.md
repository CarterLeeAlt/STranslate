本文件是仓库文档唯一导航入口。`docs/` 目录保持扁平结构。

## 语言规则
- 所有解释、推理和评论必须使用简体中文。
- 代码标识符、接口名、文件名保持原始英文。
- 错误说明、改动说明与结论统一用中文。
- Git 提交信息必须使用英文。

## 全局开发规则
- Git 提交直接提交到 `main` 分支，不新建功能分支（用户另行要求时除外）；推送仍需用户明确要求。
- 项目内 MessageBox 必须统一走 `AppMessageBox.Show()`，不要直接调用 iNKORE `MessageBox.Show()`。
- 从具体窗口或控件触发的 iNKORE `ContentDialog` 必须显式传入 owner，避免依赖活动窗口推断。
- 调试运行使用便携模式：运行 Debug 构建（`dotnet run` 或直接启动 exe）前，在输出目录 `src/.artifacts/Debug/` 下创建空的 `PortableConfig` 文件夹，使配置写入程序目录而非 `%APPDATA%\STranslate`。
- **每次重新构建时必须同时清理运行配置**，保证每次构建后程序以全新状态启动（首次运行向导重新出现）：
  1. 程序未运行（进程占用时不得清理）；
  2. 删除 `src/.artifacts/Debug/PortableConfig/` 整个目录（回收站方式），再重建空目录保持便携模式；
  3. 若 `%APPDATA%\STranslate` 存在（曾以非便携模式运行过），一并删除（回收站方式）。

## 最小构建与运行
```powershell
# 运行前启用便携模式（配置跟随程序目录，不污染 AppData）
New-Item -ItemType Directory -Force .artifacts/Debug/PortableConfig | Out-Null

# 最常用：直接运行
 dotnet run --project STranslate/STranslate.csproj

# 调试构建
 dotnet build STranslate.slnx --configuration Debug
```

## 部署到本地便携安装

- 日常安装位置：`C:\UserProgram\STranslate-win-Portable`（程序文件直接在根目录，非 Velopack 的 `current\` 布局；用户数据在其 `PortableConfig\`，绝不触碰）。
- 用仓库根的 `deploy-local.ps1` 热替换（被运行中进程锁定的文件自动重命名为 `.old-<时间戳>` 后放新文件），**脚本末尾内置关键文件哈希验证，验证不过即失败退出**。
- 注意：`git stash` 等操作后 `dotnet test` 会用 stash 后的源码重新编译并覆盖构建产物，**任何 stash 对照之后必须重新 `dotnet build` 再部署**。

## 发版流程

1. 更新仓库根 `CHANGELOG.md`（格式见文件内），提交 `docs: write changelog for vX.Y.Z`。
2. `git tag vX.Y.Z && git push origin main --tags`：**CI 仅由 tag 推送或 Release 发布触发**（`.github/workflows/dotnet.yml`），普通 main 推送只跑构建与测试、不发 Release。
3. CI（windows-latest）执行 `build.ps1` + `post_build.ps1` 打包，tag 构建自动发布 GitHub Release（正文取 CHANGELOG.md）。
4. Release 的 `STranslate-win-Portable.zip` 是 Velopack 布局（程序在 `current\` 子目录）；替换本地根目录布局安装时从 zip 的 `current\` 层拷贝（排除 zip 里的空 `PortableConfig`）。

## 测试

- `dotnet test Tests/STranslate.Tests/STranslate.Tests.csproj`。
- `WindowShowAnimationTests` 等真实窗口/渲染时序测试标记为交互 UI 测试，**默认跳过**（非交互会话与 CI 中必然失败，不代表回归）；设置环境变量 `ST_UI_TESTS=1` 显式启用。
- DeepSeek 等 live 测试需相应 API key 环境变量（如 `DEEPSEEK_API_KEY`），未设置时自动跳过。

## 文档导航（唯一入口）

| 功能模块 | 文档 | 重点内容 |
| --- | --- | --- |
| 运行时启动 | [docs/runtime-bootstrap.md](docs/runtime-bootstrap.md) | 单实例、DI、窗口生命周期、统一前台激活、异常与退出链路 |
| 插件与服务运行时 | [docs/runtime-plugin-service.md](docs/runtime-plugin-service.md) | 插件发现/加载、服务实例化、PluginContext 与配置持久化 |
| 主翻译链路 | [docs/flow-main-translation.md](docs/flow-main-translation.md) | 自动翻译、防抖、缓存命中、回译、复制与词典路径 |
| OCR 链路 | [docs/flow-ocr-image.md](docs/flow-ocr-image.md) | 截图翻译、静默 OCR、OCR窗口、坐标归一化 |
| 图片翻译链路 | [docs/flow-image-translation.md](docs/flow-image-translation.md) | 专用 OCR/翻译服务、分段逻辑、译文覆盖、插件能力 |
| 输入与触发系统 | [docs/flow-input-trigger.md](docs/flow-input-trigger.md) | 全局/软件内热键、低级键盘钩子、三击 Ctrl、鼠标划词、剪贴板监听与触发后置前 |
| 插件市场与管理 | [docs/plugin-market-management.md](docs/plugin-market-management.md) | 已安装插件管理、市场加载、下载/取消/升级/重启策略 |
| 配置、存储与历史 | [docs/config-storage-history.md](docs/config-storage-history.md) | Settings/ServiceSettings、存储抽象、便携/漫游路径、历史记录 |
| 网络集成与运维 | [docs/integration-network-ops.md](docs/integration-network-ops.md) | HTTP层、代理测试、外部调用、更新、备份恢复 |
| 插件SDK开发 | [docs/plugin-sdk-development.md](docs/plugin-sdk-development.md) | SDK接口、插件生命周期、`plugin.json` 规范、官方实现范式 |
| 社区插件开发 | [docs/community-plugin-development.md](docs/community-plugin-development.md) | 社区插件本地开发、断点调试、`.spkg` 打包导入、日志与排障 |
| 国际化与语言贡献 | [docs/i18n-internationalization.md](docs/i18n-internationalization.md) | 多语言机制、新增语言 Checklist、资源字典/插件翻译规范 |

## 模块检索入口

| 任务场景 | 先看文档 |
| --- | --- |
| 启动崩溃/窗口行为异常 | [docs/runtime-bootstrap.md](docs/runtime-bootstrap.md) |
| 插件加载失败/服务实例异常 | [docs/runtime-plugin-service.md](docs/runtime-plugin-service.md) |
| 翻译结果异常/缓存不命中 | [docs/flow-main-translation.md](docs/flow-main-translation.md) |
| OCR 结果异常 | [docs/flow-ocr-image.md](docs/flow-ocr-image.md) |
| 图片翻译结果、分段或覆盖异常 | [docs/flow-image-translation.md](docs/flow-image-translation.md) |
| 热键冲突/触发不生效 | [docs/flow-input-trigger.md](docs/flow-input-trigger.md) |
| 插件市场下载或升级问题 | [docs/plugin-market-management.md](docs/plugin-market-management.md) |
| 配置丢失/路径不一致/历史问题 | [docs/config-storage-history.md](docs/config-storage-history.md) |
| 代理、更新、备份与外部调用 | [docs/integration-network-ops.md](docs/integration-network-ops.md) |
| 新增或改造插件 | [docs/plugin-sdk-development.md](docs/plugin-sdk-development.md) |
| 社区插件本地开发/调试/打包导入 | [docs/community-plugin-development.md](docs/community-plugin-development.md) |
| 新增界面语言/国际化改造 | [docs/i18n-internationalization.md](docs/i18n-internationalization.md) |
