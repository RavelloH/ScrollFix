using System.Globalization;

namespace ScrollFix;

internal static class Strings
{
    private static readonly bool s_isChinese =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);

    private static string L(string zh, string en) => s_isChinese ? zh : en;

    // ── 标签分隔符 ──

    public static string LabelSeparator => L("：", ": ");

    // ── TrayApplicationContext ──

    public static string TrayMenu_OpenPanel => L("打开面板", "Open Panel");
    public static string TrayMenu_EnableSuppression => L("启用回滚抑制", "Enable Rollback Suppression");
    public static string TrayMenu_AutoStart => L("开机自启动", "Start with Windows");
    public static string TrayMenu_Exit => L("退出", "Exit");

    public static string Tooltip_RollbackDetected => L(
        $"{AppInfo.Name} - 检测到回滚",
        $"{AppInfo.Name} - Rollback Detected");

    public static string Tooltip_SuppressionOn => L(
        $"{AppInfo.Name} - 回滚抑制已开启",
        $"{AppInfo.Name} - Suppression Enabled");

    public static string Tooltip_DetectOnly => L(
        $"{AppInfo.Name} - 仅检测",
        $"{AppInfo.Name} - Detect Only");

    public static string Status_MonitoringWithSuppression => L(
        "状态：监控中，抑制已开启",
        "Status: Monitoring, suppression enabled");

    public static string Status_MonitoringDetectOnly => L(
        "状态：监控中，仅检测",
        "Status: Monitoring, detect only");

    public static string Status_Suppressed => L("已抑制", "suppressed");
    public static string Status_NotSuppressed => L("未抑制", "not suppressed");

    public static string Status_RollbackDetectedAt(string time, string action) => L(
        $"状态：{time} 检测到回滚，{action}",
        $"Status: Rollback detected at {time}, {action}");

    public static string Error_UpdateAutoStartFailed(string message) => L(
        $"更新开机自启动失败：{message}",
        $"Failed to update startup setting: {message}");

    // ── ScrollDashboardForm ──

    public static string Dashboard_Title => L(
        $"{AppInfo.Name} 控制面板",
        $"{AppInfo.Name} Dashboard");

    public static string Tab_Chart => L("图表", "Chart");
    public static string Tab_Status => L("状态", "Status");
    public static string Tab_Settings => L("设置", "Settings");
    public static string Tab_About => L("关于", "About");

    public static string Chart_WaveformGroup => L(
        "实时 deltaY（最近 10 秒）",
        "Real-time deltaY (last 10s)");

    public static string Status_OverviewGroup => L("概览", "Overview");
    public static string Status_MetricsGroup => L("统计", "Metrics");
    public static string Status_FieldCurrentState => L("当前状态", "Current State");
    public static string Status_FieldMode => L("运行模式", "Mode");
    public static string Status_FieldCurrentRule => L("当前规则", "Current Rule");
    public static string Status_FieldEventCount => L("事件数", "Events");
    public static string Status_FieldRollbackCount => L("回滚数", "Rollbacks");
    public static string Status_FieldErrorRate => L("错误率", "Error Rate");

    public static string Status_RecentRollback(string suffix) => L(
        $"3 秒内有回滚（{suffix}）",
        $"Rollback within 3s ({suffix})");

    public static string Status_NoRecentRollback => L("3 秒内无回滚", "No rollback within 3s");

    public static string Status_ModeSuppress => L("监控并抑制", "Monitor & Suppress");
    public static string Status_ModeDetectOnly => L("仅监控，不抑制", "Monitor only");

    public static string Status_FilterSummary(decimal reverseMs, decimal resetMs, decimal maxTicks, decimal quickMs, decimal intentionalMs) => L(
        $"反向 {reverseMs}ms · 会话 {resetMs}ms · 最多 {maxTicks} 刻度 · 快速 {quickMs}ms · 主动 {intentionalMs}ms",
        $"reverse {reverseMs}ms · reset {resetMs}ms · max {maxTicks} ticks · quick {quickMs}ms · intentional {intentionalMs}ms");

    public static string Footer_RollbackActive(string mode) => L(
        $"最近检测到回滚，当前模式：{mode}",
        $"Rollback detected recently, mode: {mode}");

    public static string Footer_Normal(string mode) => L(
        $"运行正常，当前模式：{mode}",
        $"Running normally, mode: {mode}");

    public static string Status_SuffixNone => L("暂无", "N/A");

    public static string Status_SuffixAgo(string ms) => L($"{ms}ms 前", $"{ms}ms ago");

    public static string Settings_BasicGroup => L("基本设置", "Basic Settings");
    public static string Settings_EnableSuppression => L("启用滚轮回滚抑制", "Enable scroll rollback suppression");
    public static string Settings_AutoStart => L("开机自动启动", "Start with Windows");
    public static string Settings_DetectionGroup => L("回滚判定", "Rollback Detection");
    public static string Settings_ReverseWindow => L("反向判定窗口", "Reverse Window");
    public static string Settings_ResetWindow => L("会话重置窗口", "Reset Window");
    public static string Settings_MaxRollbackTicks => L("最大回滚刻度", "Max Rollback Ticks");
    public static string Settings_QuickReverse => L("快速回滚阈值", "Quick Reverse Threshold");
    public static string Settings_IntentionalReverse => L("主动反向阈值", "Intentional Reverse Threshold");
    public static string Settings_ResetDefaults => L("恢复默认", "Reset Defaults");

    public static string About_InfoGroup => L("项目信息", "Project Info");
    public static string About_FieldName => L("项目名称", "Name");
    public static string About_FieldAuthor => L("作者", "Author");
    public static string About_FieldEmail => L("邮箱", "Email");
    public static string About_FieldRepository => L("仓库", "Repository");
    public static string About_FieldLicense => L("许可证", "License");
    public static string About_DescriptionGroup => L("说明", "Description");

    public static string About_Description => L(
        "ScrollFix 将拦截全局鼠标滚轮滚动事件，检测并抑制滚轮滚动。",
        "ScrollFix intercepts global mouse wheel events to detect and suppress scroll rollback.");

    public static string About_ClosePanel => L("关闭面板", "Close Panel");
    public static string About_OpenRepo => L("打开仓库", "Open Repository");

    // ── ScrollWaveformControl ──

    public static string Waveform_EmptyHint => L(
        "滚动鼠标滚轮，这里会显示 deltaY 时间序列。",
        "Scroll the mouse wheel to see the deltaY time series here.");

    public static string Waveform_LegendRaw => L("原始滚动", "Raw Scroll");
    public static string Waveform_LegendCorrected => L("校正结果", "Corrected");
    public static string Waveform_YAxisLabel => L("Y 轴：deltaY", "Y-axis: deltaY");
    public static string Waveform_TimeAgo(double seconds) => L($"{seconds:0}s 前", $"{seconds:0}s ago");
    public static string Waveform_TimeNow => L("现在", "Now");

    public static string Chart_ExportButton => L("导出最近日志", "Export Recent Log");

    public static string Export_HeaderTitle(string time) => L(
        $"# ScrollFix 日志导出 @ {time}",
        $"# ScrollFix log export @ {time}");

    public static string Export_HeaderFilter(
        decimal reverseMs,
        decimal resetMs,
        decimal maxTicks,
        decimal quickMs,
        decimal intentionalMs) => L(
        $"# 过滤参数: reverse={reverseMs}ms reset={resetMs}ms maxTicks={maxTicks} quick={quickMs}ms intentional={intentionalMs}ms",
        $"# Filter: reverse={reverseMs}ms reset={resetMs}ms maxTicks={maxTicks} quick={quickMs}ms intentional={intentionalMs}ms");

    public static string Export_HeaderSuppression(bool enabled) => L(
        $"# 抑制开关: {(enabled ? "开" : "关")}",
        $"# Suppression: {(enabled ? "ON" : "OFF")}");

    public static string Export_HeaderCounts(int events, int rollbacks) => L(
        $"# 累计事件: {events}  累计回滚: {rollbacks}",
        $"# Total events: {events}  Total rollbacks: {rollbacks}");

    public static string Export_HeaderColumns => "# columns: rel_ms,abs_time,raw,corrected,effective,is_rollback,was_suppressed,elapsed_ms";

    public static string Export_Success(int count) => L(
        $"已复制 {count} 条日志到剪切板。",
        $"Copied {count} log entries to clipboard.");

    public static string Export_Empty => L(
        "当前没有日志可导出。请切到图表页滚动后再试。",
        "No log entries to export. Switch to the chart tab and scroll first.");

    public static string Export_Failed(string message) => L(
        $"导出失败：{message}",
        $"Export failed: {message}");

    // ── MouseWheelHook ──

    public static string Error_InstallHookFailed => L(
        "安装全局鼠标滚轮钩子失败。",
        "Failed to install global mouse wheel hook.");

    public static string Error_UninstallHookFailed => L(
        "卸载全局鼠标滚轮钩子失败。",
        "Failed to uninstall global mouse wheel hook.");

    // ── Program ──

    public static string Error_AlreadyRunning => L(
        $"{AppInfo.Name} 已在运行，请使用现有托盘实例。",
        $"{AppInfo.Name} is already running. Please use the existing tray instance.");

    public static string Error_StartupFailed(string message) => L(
        $"{AppInfo.Name} 启动失败：{message}",
        $"{AppInfo.Name} failed to start: {message}");

    public static string Error_DashboardStartFailed(string message) => L(
        $"{AppInfo.Name} 面板启动失败：{message}",
        $"{AppInfo.Name} dashboard failed to start: {message}");

    // ── DashboardClientContext ──

    public static string Error_ConnectFailed(string message) => L(
        $"连接 {AppInfo.Name} 主进程失败：{message}",
        $"Failed to connect to {AppInfo.Name} main process: {message}");

    // ── DashboardProcessHost ──

    public static string Error_LaunchDashboardFailed => L(
        "启动面板子进程失败。",
        "Failed to launch dashboard subprocess.");
}
