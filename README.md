# ScrollFix

A minimal Windows tray application that detects and suppresses mouse wheel rollback.

一个最小的 Windows 托盘应用，用来检测并抑制鼠标滚轮回滚。

大小 200 kb 左右，内存占用 7MB 左右。

<img width="1158" height="807" alt="image" src="https://github.com/user-attachments/assets/b0278c9a-fe7a-466b-9984-be9972b58333" />
<img width="1158" height="807" alt="image" src="https://github.com/user-attachments/assets/c4a6561a-c7e9-4328-a1e6-4c54ca1c899b" />


## 功能

- 全局监听鼠标滚轮
- 检测短时间内出现的单次反向滚动
- 命中后直接拦截该次回滚事件
- 提供原生 Windows 风格控制面板
- 提供监控窗体，实时显示最近 10 秒的 deltaY 图表
- 支持开机自启动

## 运行

到仓库的 [Releases](https://github.com/RavelloH/ScrollFix/releases) 页面下载最新版本的 `ScrollFix.exe`，双击运行即可。

## 构建

```powershell
dotnet build
```

## 发布

发布为单文件 exe（需要用户安装 .NET 9 运行时）：

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

输出位于 `bin\Release\net9.0-windows\win-x64\publish\ScrollFix.exe`。

## 判定规则

当前实现会结合最近的滚动方向趋势判断小幅反向事件。遇到疑似回滚时会先拦截；如果随后回到原方向，就继续沿用原方向；如果反向连续出现 3 次，则从第 3 次起接受新方向。这样可以过滤短暂的方向翻转，同时仍允许用户换向。

当前检测阈值包括：

- 最近一次正常事件后的默认 45ms 内出现最多 2 个刻度的反方向滚动
- 或者在默认 120ms 判定窗口内，至少 2 个同向事件累计形成至少 3 个刻度的趋势
- 或者在默认 500ms 重置窗口内，至少 3 个同向事件累计形成至少 3 个刻度的趋势

时间和最大反向刻度阈值可以在控制面板里实时调整。短时间连续换向时，前两次反向事件可能会被拦截；间隔超过默认 180ms 的反向事件会直接接受。

## 配置存储

- 抑制开关：`%LOCALAPPDATA%\ScrollFix\settings.json`
- 开机自启动：`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ScrollFix`

## 滚轮事件捕捉（ScrollCapture）

仓库还包含一个独立的 Windows 捕捉工具，可手动开始/停止记录原始鼠标滚轮事件，并显示与主程序相同样式的最近 10 秒垂直滚轮 delta 波形图。界面会显示当前会话累计事件数、垂直/水平事件数、最近 10 秒垂直事件数，以及写入队列丢失数。

测试滚轮时关闭 ScrollFix 主程序，然后运行：

```powershell
dotnet run --project ScrollCapture/ScrollCapture.csproj
```

捕捉文件以 JSON Lines 格式写入 `%LOCALAPPDATA%\ScrollFix\Captures`，每行是一条独立 JSON 记录。事件包含 UTC 时间、高精度会话内时间、滚轮轴和原始 delta、鼠标坐标、注入标记，以及前台窗口标题和进程 ID。该工具只记录垂直/水平滚轮消息，不记录键盘、鼠标移动或按键。

构建捕捉工具：

```powershell
dotnet build ScrollCapture/ScrollCapture.csproj
```
