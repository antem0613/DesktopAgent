#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class WindowsAPI
{
    // デリゲートの宣言
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

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

    private const uint SPI_GETWORKAREA = 0x0030;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

    public static bool TryGetWorkArea(out RECT workArea)
    {
        return SystemParametersInfo(SPI_GETWORKAREA, 0, out workArea, 0);
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

    // RECT 構造体の定義
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }
}
#endif