#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

/// <summary>
/// トップレベルウィンドウの情報。
/// </summary>
public class WindowInfo
{
    public WindowsAPI.RECT rect;
    public IntPtr hWnd;
}

public static class WindowsAPI
{
    // デリゲートの宣言
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprcMonitor, IntPtr dwData);

    // EnumWindows 関数の宣言
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out RECT pvParam, uint fWinIni);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr lprcClip,
        MonitorEnumProc lpfnEnum,
        IntPtr dwData);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_LAYERED = 0x80000;
    private const uint GW_HWNDNEXT = 2;
    private const uint GA_ROOTOWNER = 3;
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    private const uint SPI_GETWORKAREA = 0x0030;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

    public static IntPtr GetNextWindow(IntPtr hWnd)
    {
        return GetWindow(hWnd, GW_HWNDNEXT);
    }

    /// <summary>
    /// ロジックスクリーン座標系への補正に使うDPIスケールを取得する。
    /// </summary>
    public static float GetDPIScale()
    {
        IntPtr hdc = GetDC(IntPtr.Zero);
        try
        {
            const int LOGPIXELSX = 88;
            const int LOGPIXELSY = 90;

            int dpiX = GetDeviceCaps(hdc, LOGPIXELSX);
            int dpiY = GetDeviceCaps(hdc, LOGPIXELSY);

            float dpiScaleX = dpiX / 96.0f;
            float dpiScaleY = dpiY / 96.0f;
            return (dpiScaleX + dpiScaleY) / 2.0f;
        } finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    /// <summary>
    /// 画面上の可視なトップレベルウィンドウを列挙する。
    /// </summary>
    public static List<WindowInfo> GetTopLevelWindows()
    {
        var windows = new List<WindowInfo>();
        var context = new WindowEnumContext
        {
            windows = windows,
        };

        var handle = GCHandle.Alloc(context);
        try
        {
            EnumWindows(EnumWindowsCallback, GCHandle.ToIntPtr(handle));
        } finally
        {
            handle.Free();
        }

        return windows;
    }

    private sealed class WindowEnumContext
    {
        public List<WindowInfo> windows;
    }

    [MonoPInvokeCallback(typeof(EnumWindowsProc))]
    private static bool EnumWindowsCallback(IntPtr hWnd, IntPtr lParam)
    {
        var handle = GCHandle.FromIntPtr(lParam);
        if (handle.Target is not WindowEnumContext context)
        {
            return true;
        }

        if (!IsWindowVisible(hWnd))
        {
            return true;
        }

        if (GetWindowRect(hWnd, out var rect))
        {
            context.windows.Add(new WindowInfo
            {
                hWnd = hWnd,
                rect = rect
            });
        }

        return true;
    }

    public static bool TryGetWorkArea(out RECT workArea)
    {
        return SystemParametersInfo(SPI_GETWORKAREA, 0, out workArea, 0);
    }

    public static IntPtr GetForegroundWindowHandle()
    {
        return GetForegroundWindow();
    }

    public static IntPtr GetCurrentWindowHandle()
    {
        IntPtr hWnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
        if (hWnd == IntPtr.Zero)
        {
            hWnd = GetActiveWindow();
        }

        return hWnd;
    }

    public static bool PlaceCurrentWindowAbove(IntPtr targetHWnd)
    {
        if (targetHWnd == IntPtr.Zero)
        {
            return false;
        }

        IntPtr hWnd = GetCurrentWindowHandle();
        if (hWnd == IntPtr.Zero || hWnd == targetHWnd)
        {
            return false;
        }

        return SetWindowPos(
            hWnd,
            targetHWnd,
            0,
            0,
            0,
            0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public static bool SetCurrentWindowTopmost(bool topmost)
    {
        IntPtr hWnd = GetCurrentWindowHandle();
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        IntPtr insertAfter = topmost ? HWND_TOPMOST : HWND_NOTOPMOST;
        return SetWindowPos(
            hWnd,
            insertAfter,
            0,
            0,
            0,
            0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public static bool SetCurrentWindowRect(int x, int y, int width, int height)
    {
        IntPtr hWnd = GetCurrentWindowHandle();
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        int safeWidth = Math.Max(1, width);
        int safeHeight = Math.Max(1, height);
        return SetWindowPos(
            hWnd,
            IntPtr.Zero,
            x,
            y,
            safeWidth,
            safeHeight,
            SWP_NOZORDER | SWP_NOACTIVATE);
    }

    public static bool MoveCurrentWindowBy(int deltaX, int deltaY)
    {
        IntPtr hWnd = GetCurrentWindowHandle();
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        if (!GetWindowRect(hWnd, out RECT rect))
        {
            return false;
        }

        int width = rect.right - rect.left;
        int height = rect.bottom - rect.top;
        return SetCurrentWindowRect(rect.left + deltaX, rect.top + deltaY, width, height);
    }

    public static bool BringCurrentWindowToFront()
    {
        IntPtr hWnd = GetCurrentWindowHandle();
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        return SetWindowPos(
            hWnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public static bool TryGetCurrentWindowRect(out RECT rect)
    {
        rect = default;
        IntPtr hWnd = GetCurrentWindowHandle();
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        return GetWindowRect(hWnd, out rect);
    }

    public static bool IsWindowMinimized(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        return IsIconic(hWnd);
    }

    public static IntPtr GetRootOwnerWindowHandle(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr rootOwner = GetAncestor(hWnd, GA_ROOTOWNER);
        return rootOwner != IntPtr.Zero ? rootOwner : hWnd;
    }

    public static bool AreSameRootOwnerWindow(IntPtr lhs, IntPtr rhs)
    {
        if (lhs == IntPtr.Zero || rhs == IntPtr.Zero)
        {
            return lhs == rhs;
        }

        IntPtr lhsRootOwner = GetRootOwnerWindowHandle(lhs);
        IntPtr rhsRootOwner = GetRootOwnerWindowHandle(rhs);
        return lhsRootOwner == rhsRootOwner;
    }

    public static IntPtr GetWindowFromScreenPoint(int x, int y)
    {
        var point = new POINT
        {
            X = x,
            Y = y
        };
        return WindowFromPoint(point);
    }

    public static bool IsWindowMaximized(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        return IsZoomed(hWnd);
    }

    public static bool IsWindowFullscreen(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        if (!GetWindowRect(hWnd, out var rect))
        {
            return false;
        }

        if (!TryGetMonitorRect(hWnd, out var monitorRect))
        {
            return false;
        }

        return RectEquals(rect, monitorRect);
    }

    public static IntPtr GetTaskbarHandle()
    {
        return FindWindow("Shell_TrayWnd", null);
    }

    public static bool TryGetTaskbarRect(out Rect rect)
    {
        rect = default;

        var hWnd = GetTaskbarHandle();
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        if (!GetWindowRect(hWnd, out var winRect))
        {
            return false;
        }

        rect = ToUnityRect(winRect);
        return true;
    }

    public static bool SetTopmost(IntPtr hWnd, bool topmost)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        if (hWnd == GetCurrentWindowHandle())
        {
            return SetCurrentWindowTopmost(topmost);
        }

        IntPtr insertAfter = topmost ? HWND_TOPMOST : HWND_NOTOPMOST;
        return SetWindowPos(
            hWnd,
            insertAfter,
            0,
            0,
            0,
            0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public static bool PlaceAboveWindow(IntPtr hWnd, IntPtr targetHwnd)
    {
        if (hWnd == IntPtr.Zero || targetHwnd == IntPtr.Zero)
        {
            return false;
        }

        if (hWnd == GetCurrentWindowHandle())
        {
            return PlaceCurrentWindowAbove(targetHwnd);
        }

        return SetWindowPos(hWnd, targetHwnd, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public static bool PlaceBelowWindow(IntPtr hWnd, IntPtr targetHwnd)
    {
        if (hWnd == IntPtr.Zero || targetHwnd == IntPtr.Zero)
        {
            return false;
        }

        return SetWindowPos(hWnd, new IntPtr(1), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public static bool TryGetWindowMonitorDeviceString(IntPtr hWnd, out string deviceString)
    {
        deviceString = string.Empty;

        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        if (!TryGetMonitorInfo(hWnd, out var info))
        {
            return false;
        }

        deviceString = info.szDevice;
        return !string.IsNullOrWhiteSpace(deviceString);
    }

    public static bool TryGetWindowMonitorRects(IntPtr hWnd, out Rect monitorRect, out Rect workRect, out string deviceString)
    {
        monitorRect = default;
        workRect = default;
        deviceString = string.Empty;

        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        if (!TryGetMonitorInfo(hWnd, out var info))
        {
            return false;
        }

        monitorRect = ToScreenRect(info.rcMonitor);
        workRect = ToScreenRect(info.rcWork);
        deviceString = info.szDevice ?? string.Empty;
        return true;
    }

    public static List<string> GetAllMonitorDeviceStrings()
    {
        var result = new List<string>();

        var handles = new List<IntPtr>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, __, ___) =>
        {
            handles.Add(hMonitor);
            return true;
        }, IntPtr.Zero);

        foreach (var handle in handles)
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(handle, ref info))
            {
                if (!string.IsNullOrWhiteSpace(info.szDevice) && !result.Contains(info.szDevice))
                {
                    result.Add(info.szDevice);
                }
            }
        }

        return result;
    }

    public static bool SetClickThrough(IntPtr hWnd, bool enabled)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        var stylePtr = GetWindowLongPtr(hWnd, GWL_EXSTYLE);
        long style = stylePtr.ToInt64();

        if (enabled)
        {
            style |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
        } else
        {
            style &= ~WS_EX_TRANSPARENT;
        }

        SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(style));
        return true;
    }

    // RECT 構造体の定義
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private static bool TryGetMonitorRect(IntPtr hWnd, out RECT rect)
    {
        rect = default;
        if (!TryGetMonitorInfo(hWnd, out var info))
        {
            return false;
        }

        rect = info.rcMonitor;
        return true;
    }

    private static bool TryGetMonitorInfo(IntPtr hWnd, out MONITORINFOEX info)
    {
        info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        var monitor = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        return GetMonitorInfo(monitor, ref info);
    }

    private static bool RectEquals(RECT a, RECT b)
    {
        return a.left == b.left
               && a.top == b.top
               && a.right == b.right
               && a.bottom == b.bottom;
    }

    private static Rect ToUnityRect(RECT rect)
    {
        float left = rect.left;
        float right = rect.right;
        float top = rect.top;
        float bottom = rect.bottom;

        float unityTop = Screen.height - top;
        float unityBottom = Screen.height - bottom;
        return Rect.MinMaxRect(left, unityBottom, right, unityTop);
    }

    private static Rect ToScreenRect(RECT rect)
    {
        return Rect.MinMaxRect(rect.left, rect.top, rect.right, rect.bottom);
    }
}
#endif