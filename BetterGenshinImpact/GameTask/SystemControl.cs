using BetterGenshinImpact.View.Windows;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using System.Text;
using System.Runtime.InteropServices;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using Microsoft.Extensions.Logging;


namespace BetterGenshinImpact.GameTask;

public class SystemControl
{
    public static nint FindGenshinImpactHandle()
    {
        var processNames = TaskContext.Instance().GetGenshinGameProcessNameList();
        return FindHandleByProcessName(processNames.ToArray());
    }

    public static async Task<nint> StartFromLocalAsync(string path)
    {
        if (!File.Exists(path))
        {
            await ThemedMessageBox.ErrorAsync($"原神启动路径 {path} 不存在，请前往 启动——同时启动原神——原神安装路径 重新进行配置！");
            return IntPtr.Zero;
        }

        var cfg = TaskContext.Instance().Config.GenshinStartConfig;
        var workdir = Path.GetDirectoryName(path) ?? "";
        var arg = cfg.GenshinStartArgs;

        if (cfg.StartGameWithCmd)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"\" /d \"{workdir}\" \"{path}\" {arg}",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
        }
        else
        {
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                Arguments = arg,
                WorkingDirectory = workdir
            });
        }

        // 等待新启动的进程创建窗口，带超时保护
        var sw = Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(60);
        while (sw.Elapsed < timeout)
        {
            var handle = FindGenshinImpactHandle();
            Logger.LogDebug("等待进程:{handle}", handle);
            if (handle != 0)
            {
                // 稳定等待，确保窗口完全初始化
                await Task.Delay(3000);
                handle = FindGenshinImpactHandle();
                await Task.Delay(2500);
                return handle;
            }
            await Task.Delay(2000);
        }

        // 超时后再做一次最终尝试
        return FindGenshinImpactHandle();
    }

    public static bool IsGenshinImpactActiveByProcess()
    {
        var name = GetActiveProcessName();
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        var processNames = TaskContext.Instance().GetGenshinGameProcessNameList();
        return processNames.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
    }
    
    public static string GetActiveByProcess()
    {
        return GetActiveProcessName() ?? "Unknown";
    }

    public static bool IsGenshinImpactActive()
    {
        var hWnd = User32.GetForegroundWindow();
        return hWnd == TaskContext.Instance().GameHandle;
    }

    public static bool IsGenshinImpactMinimized()
    {
        return User32.IsIconic(TaskContext.Instance().GameHandle);
    }

    public static nint GetForegroundWindowHandle()
    {
        return (nint)User32.GetForegroundWindow();
    }

    public static nint FindHandleByProcessName(params string[] names)
    {
        var currentUser = Environment.UserName;
        foreach (var name in names)
        {
            var pros = Process.GetProcessesByName(name);
            // Logger.LogError($"FindHandleByProcessName_ee: Searching for Process Name={name}, Found={pros.Length}, Current User={currentUser}");
            if (pros.Length == 0)
                continue;

            // 优先选择属于当前登录用户且有主窗口句柄的进程
            foreach (var p in pros)
            {
                try
                {
                    var owner = GetProcessOwnerName(p.Id);
                    if (!string.IsNullOrEmpty(owner))
                    {
                        // owner 可能是 "DOMAIN\User" 或 "User"，只比较用户名部分
                        var ownerUser = owner.Contains("\\") ? owner.Split('\\').Last() : owner;
                        if (string.Equals(ownerUser, currentUser, StringComparison.OrdinalIgnoreCase))
                        {
                            if (p.MainWindowHandle != IntPtr.Zero)
                                return p.MainWindowHandle;
                        }
                    }
                }
                catch
                {
                    // 忽略无法访问的进程，继续尝试其他进程
                }
            }

            // 回退：返回首个有主窗口句柄的进程
            var fallback = pros.FirstOrDefault(pp => pp.MainWindowHandle != IntPtr.Zero);
            //log输出结果信息
            Debug.WriteLine( $"FindHandleByProcessName: Process Name={name}, Found={pros.Length}, Fallback Handle={(fallback != null ? fallback.MainWindowHandle.ToString() : "None")}");
            // Logger.LogError( $"FindHandleByProcessName: Process Name={name}, Found={pros.Length}, Fallback Handle={(fallback != null ? fallback.MainWindowHandle.ToString() : "None")}");
            if (fallback != null)
                return fallback.MainWindowHandle;
        }

        return 0;
    }
    
    private static string? GetProcessOwnerName(int processId)
{
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    const uint TOKEN_QUERY = 0x0008;
    const int TokenUser = 1;

    IntPtr hProcess = IntPtr.Zero;
    IntPtr hToken = IntPtr.Zero;
    IntPtr tokenInfo = IntPtr.Zero;

    try
    {
        hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
        if (hProcess == IntPtr.Zero)
            return null;

        if (!OpenProcessToken(hProcess, TOKEN_QUERY, out hToken) || hToken == IntPtr.Zero)
            return null;

        // 先获取所需缓冲区大小
        if (!GetTokenInformation(hToken, TokenUser, IntPtr.Zero, 0, out var requiredLength))
        {
            var err = Marshal.GetLastWin32Error();
            if (err != 122) // ERROR_INSUFFICIENT_BUFFER
                return null;
        }

        tokenInfo = Marshal.AllocHGlobal(requiredLength);
        if (!GetTokenInformation(hToken, TokenUser, tokenInfo, requiredLength, out _))
            return null;

        var tokenUser = Marshal.PtrToStructure<TOKEN_USER>(tokenInfo);
        var sid = tokenUser.User.Sid;
        if (sid == IntPtr.Zero)
            return null;

        uint nameLen = 0;
        uint domainLen = 0;
        int sidUse;
        // 第一次调用以获取长度
        _ = LookupAccountSid(null, sid, null, ref nameLen, null, ref domainLen, out sidUse);

        var nameSb = new StringBuilder((int)nameLen);
        var domainSb = new StringBuilder((int)domainLen);
        if (LookupAccountSid(null, sid, nameSb, ref nameLen, domainSb, ref domainLen, out sidUse))
        {
            var domain = domainSb.ToString();
            var name = nameSb.ToString();
            return string.IsNullOrEmpty(domain) ? name : $"{domain}\\{name}";
        }
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"GetProcessOwnerName 异常: {ex.Message}");
    }
    finally
    {
        if (tokenInfo != IntPtr.Zero) Marshal.FreeHGlobal(tokenInfo);
        if (hToken != IntPtr.Zero) CloseHandle(hToken);
        if (hProcess != IntPtr.Zero) CloseHandle(hProcess);
    }

    return null;
}
    
    [StructLayout(LayoutKind.Sequential)]
    private struct SID_AND_ATTRIBUTES
    {
        public IntPtr Sid;
        public int Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_USER
    {
        public SID_AND_ATTRIBUTES User;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass, IntPtr TokenInformation, int TokenInformationLength, out int ReturnLength);

    [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool LookupAccountSid(string lpSystemName, IntPtr Sid, StringBuilder Name, ref uint cchName, StringBuilder ReferencedDomainName, ref uint cchReferencedDomainName, out int peUse);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    public static nint FindHandleByWindowName()
    {
        var handle = (nint)User32.FindWindow("UnityWndClass", "原神");
        if (handle != 0)
        {
            return handle;
        }

        handle = (nint)User32.FindWindow("UnityWndClass", "Genshin Impact");
        if (handle != 0)
        {
            return handle;
        }

        handle = (nint)User32.FindWindow("Qt5152QWindowIcon", "云·原神");
        if (handle != 0)
        {
            return handle;
        }

        return 0;
    }

    public static string? GetActiveProcessName()
    {
        try
        {
            var hWnd = User32.GetForegroundWindow();
            _ = User32.GetWindowThreadProcessId(hWnd, out var pid);
            var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    public static Process? GetProcessByHandle(nint hWnd)
    {
        try
        {
            _ = User32.GetWindowThreadProcessId(hWnd, out var pid);
            var p = Process.GetProcessById((int)pid);
            return p;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return null;
        }
    }

    /// <summary>
    /// 获取窗口位置
    /// </summary>
    /// <param name="hWnd"></param>
    /// <returns></returns>
    public static RECT GetWindowRect(nint hWnd)
    {
        // User32.GetWindowRect(hWnd, out var windowRect);
        DwmApi.DwmGetWindowAttribute<RECT>(hWnd, DwmApi.DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, out var windowRect);
        return windowRect;
    }

    /// <summary>
    /// 游戏本身分辨率获取
    /// </summary>
    /// <param name="hWnd"></param>
    /// <returns></returns>
    public static RECT GetGameScreenRect(nint hWnd)
    {
        User32.GetClientRect(hWnd, out var clientRect);
        return clientRect;
    }

    /// <summary>
    /// GetWindowRect or GetGameScreenRect
    /// </summary>
    /// <param name="hWnd"></param>
    /// <returns></returns>
    public static RECT GetCaptureRect(nint hWnd)
    {
        var windowRect = GetWindowRect(hWnd);
        var gameScreenRect = GetGameScreenRect(hWnd);
        var left = windowRect.Left;
        var top = windowRect.Top + windowRect.Height - gameScreenRect.Height;
        var right = left + gameScreenRect.Width;
        var bottom = top + gameScreenRect.Height;
        return new RECT(left, top, right, bottom);
    }

    public static void ActivateWindow(nint hWnd)
    {
        User32.ShowWindow(hWnd, ShowWindowCommand.SW_RESTORE);
        User32.SetForegroundWindow(hWnd);
    }

    public static void ActivateWindow()
    {
        if (!TaskContext.Instance().IsInitialized)
        {
            throw new Exception("请先启动BetterGI");
        }

        ActivateWindow(TaskContext.Instance().GameHandle);
    }
    public static void RestartApplication(string[] newArgs)
    {
        // 获取当前程序路径
        string exePath = Process.GetCurrentProcess().MainModule.FileName;

        // 构建参数字符串
        string arguments = string.Join(" ", [..newArgs,"--no-single"]);

        // 启动新进程
        Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            UseShellExecute = false
        });

        // 关闭当前程序
        Environment.Exit(0);
    }
    public static void FocusWindow(nint hWnd)
    {
        if (User32.IsWindow(hWnd))
        {
            _ = User32.SendMessage(hWnd, User32.WindowMessage.WM_SYSCOMMAND, User32.SysCommand.SC_RESTORE, 0);
            _ = User32.SetForegroundWindow(hWnd);

            // 移除 while (User32.IsIconic(hWnd)) { continue; } busy-spin。
            // 若 SC_RESTORE 后仍为 Iconic，由上层 CheckAndActivateGameWindow 下一轮 iter 通过
            // TryRestoreIconic 路径处理（每秒一次，无上限直到成功）。
            _ = User32.BringWindowToTop(hWnd);
            _ = User32.SetActiveWindow(hWnd);
        }
    }
    public static void MinimizeAndActivateWindow(nint hWnd)
    {
        // 历史上发 Shell_TrayWnd 0x0111 / wParam=419 触发"显示桌面"，会副作用最小化所有窗口
        // （含目标窗口本身），已移除（D3 / BC-3）。改为按需 ShowWindow(SW_RESTORE) + FocusWindow——
        // 方法名保留以维持 ScriptService 等调用方签名（P-3 / P-6）。
        if (!User32.IsWindow(hWnd)) return;
        if (User32.IsIconic(hWnd))
        {
            _ = User32.ShowWindow(hWnd, ShowWindowCommand.SW_RESTORE);
        }
        FocusWindow(hWnd);
    }
    public static void RestoreWindow(nint hWnd)
    {
        if (User32.IsWindow(hWnd))
        {
            _ = User32.SendMessage(hWnd, User32.WindowMessage.WM_SYSCOMMAND, User32.SysCommand.SC_RESTORE, 0);
            _ = User32.SetForegroundWindow(hWnd);

            if (User32.IsIconic(hWnd))
            {
                _ = User32.ShowWindow(hWnd, ShowWindowCommand.SW_RESTORE);
            }

            _ = User32.BringWindowToTop(hWnd);
            _ = User32.SetActiveWindow(hWnd);
        }
    }

    public static bool IsFullScreenMode(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        var exStyle = User32.GetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE);

        return (exStyle & (int)User32.WindowStylesEx.WS_EX_TOPMOST) != 0;
    }

    // private static void StartFromLauncher(string path)
    // {
    //     // 通过launcher启动
    //     var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    //     Thread.Sleep(1000);
    //     // 获取launcher窗口句柄
    //     var hWnd = FindHandleByProcessName("launcher");
    //     var rect = GetWindowRect(hWnd);
    //     var dpiScale = Helpers.DpiHelper.ScaleY;
    //     // 对于launcher，启动按钮的位置时固定的，在launcher窗口的右下角
    //     Thread.Sleep(1000);
    //     Simulation.MouseEvent.Click((int)((float)rect.right * dpiScale) - (rect.Width / 5), (int)((float)rect.bottom * dpiScale) - (rect.Height / 8));
    // }
    //
    // private static void StartCloudYaunShen(string path)
    // {
    //     // 通过launcher启动
    //     var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    //     Thread.Sleep(10000);
    //     // 获取launcher窗口句柄
    //     var hWnd = FindHandleByProcessName("Genshin Impact Cloud Game");
    //     var rect = GetWindowRect(hWnd);
    //     var dpiScale = Helpers.DpiHelper.ScaleY;
    //     // 对于launcher，启动按钮的位置时固定的，在launcher窗口的右下角
    //     Simulation.MouseEvent.Click(rect.right - (rect.Width / 6), rect.bottom - (rect.Height / 13 * 3));
    //     // TODO：点完之后有个15s的倒计时，好像不处理也没什么问题，直接睡个20s吧
    //     Thread.Sleep(20000);
    // }
    public static void CloseGame()
    {
        try
        {
            var processNames = TaskContext.Instance().GetGenshinGameProcessNameList();
            var processes = processNames
                .SelectMany(Process.GetProcessesByName)
                .GroupBy(p => p.Id)
                .Select(g => g.First())
                .ToArray();

            if (processes.Length > 0)
            {
                foreach (var process in processes)
                {
                    try
                    {
                        // 尝试正常关闭进程
                        process.CloseMainWindow();
                        
                        // 给进程一些时间来响应关闭请求
                        if (!process.WaitForExit(5000))
                        {
                            // 如果进程没有在5秒内关闭，则强制终止它
                            process.Kill();
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"关闭游戏进程时出错: {ex.Message}");
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CloseGame方法执行出错: {ex.Message}");
        }
    }

    public static void Shutdown()
    {
        try
        {
            // 使用Windows API安全关闭系统
            // 这里使用的是标准的Windows关机命令，需要适当的权限
            Process.Start("shutdown", "/s /t 60 /c \"系统将在60秒后关闭，请保存您的工作。\"");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Shutdown方法执行出错: {ex.Message}");
        }
    }
}
