using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

public static class NativeWindowApi
{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_LAYERED = 0x80000;
    private const uint GA_ROOTOWNER = 3;

    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

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
#endif

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    public static IntPtr GetForegroundWindowHandle()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        return GetForegroundWindow();
#else
        return IntPtr.Zero;
#endif
    }

    public static IntPtr GetRootOwnerWindowHandle(IntPtr hWnd)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (hWnd == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr rootOwner = GetAncestor(hWnd, GA_ROOTOWNER);
        return rootOwner != IntPtr.Zero ? rootOwner : hWnd;
#else
        return hWnd;
#endif
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
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        var point = new POINT
        {
            X = x,
            Y = y
        };
        return WindowFromPoint(point);
#else
        return IntPtr.Zero;
#endif
    }

    public static bool IsWindowMaximized(IntPtr hWnd)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        return IsZoomed(hWnd);
#else
        return false;
#endif
    }

    public static bool IsWindowFullscreen(IntPtr hWnd)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
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
#else
        return false;
#endif
    }

    public static IntPtr GetTaskbarHandle()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        return FindWindow("Shell_TrayWnd", null);
#else
        return IntPtr.Zero;
#endif
    }

    public static bool TryGetTaskbarRect(out Rect rect)
    {
        rect = default;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
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
#else
        return false;
#endif
    }

    public static bool SetTopmost(IntPtr hWnd, bool topmost)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        return WindowsAPI.SetCurrentWindowTopmost(topmost);
#else
        return false;
#endif
    }

    public static bool PlaceAboveWindow(IntPtr hWnd, IntPtr targetHwnd)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (hWnd == IntPtr.Zero || targetHwnd == IntPtr.Zero)
        {
            return false;
        }

        if (hWnd == WindowsAPI.GetCurrentWindowHandle())
        {
            return WindowsAPI.PlaceCurrentWindowAbove(targetHwnd);
        }

        return SetWindowPos(hWnd, targetHwnd, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
#else
        return false;
#endif
    }

    public static bool PlaceBelowWindow(IntPtr hWnd, IntPtr targetHwnd)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (hWnd == IntPtr.Zero || targetHwnd == IntPtr.Zero)
        {
            return false;
        }

        return SetWindowPos(hWnd, new IntPtr(1), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
#else
        return false;
#endif
    }

    public static bool TryGetWindowMonitorDeviceString(IntPtr hWnd, out string deviceString)
    {
        deviceString = string.Empty;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
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
#else
        return false;
#endif
    }

    public static List<string> GetAllMonitorDeviceStrings()
    {
        var result = new List<string>();

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
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
#endif

        return result;
    }

    public static bool SetClickThrough(IntPtr hWnd, bool enabled)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        var stylePtr = GetWindowLongPtr(hWnd, GWL_EXSTYLE);
        long style = stylePtr.ToInt64();

        if (enabled)
        {
            style |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
        }
        else
        {
            style &= ~WS_EX_TRANSPARENT;
        }

        SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(style));
        return true;
#else
        return false;
#endif
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr lprcClip,
        MonitorEnumProc lpfnEnum,
        IntPtr dwData);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprcMonitor, IntPtr dwData);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
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
#endif
}
