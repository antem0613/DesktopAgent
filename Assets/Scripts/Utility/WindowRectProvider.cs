using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

public interface IWindowRectProvider
{
    List<Rect> GetWindowRects();
}

public sealed class EditorMockWindowRectProvider : IWindowRectProvider, IWindowRectProviderWithSource
{
    private readonly WindowRectDebugConfig _config;

    public EditorMockWindowRectProvider(WindowRectDebugConfig config)
    {
        _config = config;
    }

    public List<Rect> GetWindowRects()
    {
        if (_config == null || !_config.useMockInEditor)
        {
            return new List<Rect>();
        }

        return new List<Rect>(_config.mockWindowRects);
    }

    public List<WindowRectWithSource> GetWindowRectsWithSource()
    {
        if (_config == null || !_config.useMockInEditor)
        {
            return new List<WindowRectWithSource>();
        }

        var result = new List<WindowRectWithSource>(_config.mockWindowRects.Count);
        foreach (var rect in _config.mockWindowRects)
        {
            result.Add(new WindowRectWithSource(rect, _config));
        }

        return result;
    }
}

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
public sealed class WindowsTopLevelWindowRectProvider : IWindowRectProvider, IWindowRectProviderWithSource
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    public List<Rect> GetWindowRects()
    {
        var windows = new List<Rect>();
        var withSource = GetWindowRectsWithSource();
        if (withSource != null)
        {
            foreach (var item in withSource)
            {
                windows.Add(item.Rect);
            }
        }
        return windows;
    }

    public List<WindowRectWithSource> GetWindowRectsWithSource()
    {
        var windows = new List<WindowRectWithSource>();

        // DPI補正（Unity Screen座標に寄せる）
        float dpiScale = WindowsExplorerUtility.GetDPIScale();
        dpiScale = Mathf.Max(0.01f, dpiScale);

        // EnumWindowsを使って可視ウィンドウを列挙
        var handle = GCHandle.Alloc(windows);
        try
        {
            WindowsAPI.EnumWindows((hWnd, lParam) =>
            {
                if (!WindowsAPI.IsWindowVisible(hWnd))
                {
                    return true;
                }

                // タイトルが空のものは除外（ツール/不可視系が多い）
                int len = GetWindowTextLength(hWnd);
                if (len <= 0)
                {
                    return true;
                }

                var sb = new StringBuilder(len + 1);
                GetWindowText(hWnd, sb, sb.Capacity);
                var title = sb.ToString();
                if (string.IsNullOrWhiteSpace(title))
                {
                    return true;
                }

                // 位置取得
                if (!WindowsAPI.GetWindowRect(hWnd, out var rect))
                {
                    return true;
                }

                // サイズ0や異常値を除外
                int w = rect.right - rect.left;
                int h = rect.bottom - rect.top;
                if (w <= 10 || h <= 10)
                {
                    return true;
                }

                // DPI補正（Win32は物理解像度寄り、Unityは論理解像度寄り）
                float left = rect.left / dpiScale;
                float right = rect.right / dpiScale;
                float top = rect.top / dpiScale;
                float bottom = rect.bottom / dpiScale;

                // Unityのスクリーン座標(左下原点)へ変換
                float unityTop = Screen.height - top;
                float unityBottom = Screen.height - bottom;
                float unityLeft = left;
                float unityRight = right;

                var unityRect = Rect.MinMaxRect(unityLeft, unityBottom, unityRight, unityTop);

                // 画面外を極端に含むものは軽く除外
                if (unityRect.width <= 10 || unityRect.height <= 10)
                {
                    return true;
                }

                windows.Add(new WindowRectWithSource(unityRect, hWnd));
                return true;
            }, IntPtr.Zero);
        } finally
        {
            handle.Free();
        }

        return windows;
    }
}
#endif
