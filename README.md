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

当前实现把“回滚”定义为：

- 在最近一次正常滚轮事件后的默认 120ms 内
- 出现最多 2 个刻度的反方向滚动

这些阈值可以在控制面板里实时调整。

## 配置存储

- 抑制开关：`%LOCALAPPDATA%\ScrollFix\settings.json`
- 开机自启动：`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ScrollFix`
