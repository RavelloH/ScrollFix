# ScrollCapture

ScrollCapture is a small Windows Forms app for collecting raw vertical and horizontal mouse-wheel events while investigating wheel rollback behavior. It can be started and stopped without changing ScrollFix's filter settings.

## Run

From the repository root:

```powershell
dotnet run --project ScrollCapture/ScrollCapture.csproj
```

Use **开始捕捉** and **停止捕捉** to control one capture session. The graph shows vertical wheel deltas over the latest 10 seconds, matching ScrollFix's deltaY graph. The counters show total, vertical, and horizontal events, recent vertical events, and any events dropped because the file writer queue was full.

## Output

Each session writes one UTF-8 JSON Lines file under:

```text
%LOCALAPPDATA%\ScrollFix\Captures
```

The file has `session_start`, `wheel_event`, and `session_end` records. Wheel records include the original signed `delta`, axis, UTC timestamp, monotonic session timing, cursor position, low-level hook flags, foreground window handle, process ID, and window title. The app does not capture keyboard input, mouse movement, or button clicks.

The window title may contain text shown by the foreground app. Keep or share capture files accordingly.
