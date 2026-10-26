# 网络集成与运维链路

## 模块职责
- 提供统一 HTTP 访问层（GET/POST/流式/下载/代理测试）。
- 管理外部调用 API（本地 HTTP 服务）与命令路由。
- 管理配置备份恢复（本地 zip 与 WebDav）。

## 关键入口
- `STranslate/Core/HttpService.cs`
  - 通用 HTTP 封装、超时/请求头/查询参数处理、下载进度。
  - `StreamPostAsync()` / `StreamPostAsyncEnumerable()`：流式 POST 响应消费。
  - `TestProxyAsync()` / `GetCurrentIpAsync()` 代理连通性验证。
- `STranslate/ViewModels/Pages/NetworkViewModel.cs`
  - 网络设置页测试入口与外部调用服务状态展示。
- `STranslate/Core/ExternalCallService.cs`
  - `HttpListener` 本地服务，路由外部请求到主窗口命令。
- `STranslate/Services/BackupService.cs`
  - 本地备份恢复与 WebDav 备份恢复。

## 核心流程
### 从入口到结果：代理测试
1. 网络页执行 `TestConnectionCommand`。
2. `NetworkViewModel.TestConnectionAsync()` 调用：
   - `_httpService.TestProxyAsync()`。
   - `_httpService.GetCurrentIpAsync()`。
3. 将连通结果与 IP 信息回写 `TestResult`。

### 从入口到结果：外部调用 API
1. `Settings.EnableExternalCall=true` 时，`Settings.ApplyExternalCall()` 调用 `ExternalCallService.StartService("http://127.0.0.1:{port}/")`。
2. `ExternalCallService` 用 `HttpListener` 接收请求，解析路径为 `ExternalCallAction`。
3. 按 GET/POST 请求内容路由到 `MainWindowViewModel` 对应命令（翻译、OCR、图片翻译、静默 OCR/TTS、窗口操作、热键开关等）。
4. action 获得串行执行锁后压入 `ForceForeground` 激活上下文；异步命令会等待完成，保证上下文覆盖完整请求链并在异常、取消或提前返回后恢复。
5. 主窗口、设置、历史、OCR、图片翻译窗口以及请求派生的异常提示仍复用应用内显示入口，但会自动使用 `ForceSetForegroundWindow()` 强制置前；HTTP action 无需逐层传递激活参数。
6. 统一返回 JSON：`code + data`。

### 从入口到结果：流式 HTTP 请求
1. 插件或主程序通过 `IHttpService.StreamPostAsync()` 使用回调消费逐行响应。
2. 需要 `await foreach` 消费时，使用 `StreamPostAsyncEnumerable()`：
   - 发送 POST 请求时使用 `HttpCompletionOption.ResponseHeadersRead`。
   - 响应成功后逐行读取内容，忽略空行，避免把 SSE 心跳空包暴露给调用方。
   - `CancellationToken` 取消时向上抛出取消状态，不包装为普通业务失败。
3. 两套接口都支持默认 HTTP 客户端和指定 `serviceName` HTTP 客户端。

### 从入口到结果：本地与 WebDav 备份恢复
- 本地备份/恢复：
  1. 选择 zip 文件。
  2. 调用 `STranslate.Host`（`backup` 模式）对 `Plugins` 与 `Settings` 打包或还原。
  3. 程序重启并通过 `InfoFilePath` 向主程序传递提示信息。
- WebDav 备份：
  1. 预检查 WebDav 可达。
  2. 重启前通过 Host 先生成本地 zip 并把路径写入 `BackupFilePath`。
  3. 下次启动 `App.WebDavBackupOperation()` 读取路径并调用 `BackupService.PostWebDavBackupAsync()` 上传。
- WebDav 恢复：
  1. 拉取远端可用备份列表。
  2. 下载所选 zip。
  3. 通过 Host 执行还原并重启。

## 关键数据结构/配置
- `Options`（HTTP 参数）
  - `Headers`、`QueryParams`、`Timeout`、`ContentType`。
- `StreamPostAsyncEnumerable`
  - 返回 `IAsyncEnumerable<string>`，适合大模型流式响应或 SSE 逐行处理。
- `DownloadProgress`
  - `DownloadedBytes`、`TotalBytes`、`Speed`、`ElapsedTime`。
- `ExternalCallAction`
  - 外部路由动作枚举。
- 关键设置
  - `Settings.Proxy`、`Settings.HttpTimeout`。
  - `Settings.EnableExternalCall`、`Settings.ExternalCallPort`。
  - `Settings.Backup`（地址、账号、密码、备份类型）。

## 关键文件
- `STranslate/Core/HttpService.cs`
- `STranslate/ViewModels/Pages/NetworkViewModel.cs`
- `STranslate/Core/ExternalCallService.cs`
- `STranslate/Services/BackupService.cs`
- `STranslate/App.xaml.cs`

## 常见改动任务
- 新增外部调用路径：在 `ExternalCallAction` 增加枚举，并在 `ExecuteExternalCall()` 添加分支。
- 新增会显示窗口的外部调用：复用 `MainWindowViewModel` 或 `SingletonWindowOpener` 的现有入口，并确保异步命令被等待；强制置前由外部调用上下文统一继承，不在 action 分支中直接操作窗口线程。
- 下载链路优化：优先扩展 `HttpService.DownloadFileAsync()`，保证进度、取消、异常统一。
- 流式请求改造：优先扩展 `StreamPostAsync()` 与 `StreamPostAsyncEnumerable()` 的共同读取语义，确保取消、空行过滤和错误处理一致。
- 备份策略增强：统一改 `BackupService` 与 Host 参数协议，避免主进程和 Host 参数不一致。
