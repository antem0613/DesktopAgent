using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using AOT;
using UnityEngine;

//展開されているウィンドウの情報を管理する
//最小化ウィンドウは除外し、最大化ウィンドウは着地不可の遮蔽要素として保持する
//ウィンドウの位置、サイズ、重なり順などを管理する
//キャラクターはウィンドウの上辺が他のウィンドウに重なっていなければ着地できる
//タスクバーは常に着地可能でタスクバー以下の高さは着地できない
//最大化ウィンドウが最前面にある場合はタスクバーのみが着地可能になる
//情報は毎フレーム更新される必要がある
//リストは計算に必要な最低限の情報を保持し、ある地点が着地可能かは都度計算する形にする
public class TestWindowManager : MonoBehaviour
{
	private const string LogTag = nameof(TestWindowManager);
	public static TestWindowManager Instance { get; private set; }
	[SerializeField] private bool enableDebugLog;
	[SerializeField] private float edgeOcclusionProbeOffsetPixels = 1f;
	[SerializeField] private bool restrictToCurrentMonitor = true;
	[SerializeField] private float maxAllowedAboveCurrentPixels = 0.5f;

	private readonly List<WindowEntry> _windows = new List<WindowEntry>();
	private IntPtr _currentWindowHandle;
	private int _currentProcessId;
	private bool _hasCurrentMonitorMetrics;
	private Vector2 _currentMonitorOrigin;
	private Vector2 _currentMonitorSize;
	private float _currentWorkBottom;
	private string _currentMonitorDeviceString;

	private sealed class WindowEnumContext
	{
		public TestWindowManager Manager;
		public float DpiScale;
		public int ZOrder;
	}

	private static readonly string[] ExcludedWindowClasses =
	{
		"Shell_TrayWnd",
		"XamlExplorerHostIslandWindow",
		"NotifyIconOverflowWindow"
	};

	public IReadOnlyList<WindowEntry> Windows => _windows;
	public bool HasTaskbar { get; private set; }
	public Rect TaskbarRect { get; private set; }
	public float TaskbarTopY { get; private set; }
	public bool IsForegroundMaximized { get; private set; }

	[Serializable]
	public struct WindowEntry
	{
		public IntPtr Handle;
		public Rect Rect;
		public int ZOrder; // 0 が最前面
		public bool IsMaximized;
	}

	private void Awake()
	{
		if (Instance != null && Instance != this)
		{
			if (enableDebugLog)
			{
				TestLog.Warning(LogTag, "Duplicate TestWindowManager detected. Disabling this component.");
			}

			enabled = false;
			return;
		}

		Instance = this;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
		_currentWindowHandle = WindowsAPI.GetCurrentWindowHandle();
		_currentProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;
#endif
	}

	private void OnDestroy()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}

	private void Start()
	{
		RefreshWindows();
	}

	private void Update()
	{
		RefreshWindows();
	}

	public void RefreshWindows()
	{
		// 毎フレーム、着地判定に使うウィンドウ情報を再構築する。
		_windows.Clear();

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
		if (_currentWindowHandle == IntPtr.Zero)
		{
			_currentWindowHandle = WindowsAPI.GetCurrentWindowHandle();
		}

		if (_currentProcessId == 0)
		{
			_currentProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;
		}

		UpdateCurrentMonitorMetrics();
#endif

		UpdateTaskbarState();

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

		IntPtr foregroundWindowHandle = WindowsAPI.GetForegroundWindowHandle();
		IsForegroundMaximized = IsForegroundMaximizedByOtherProcessWindow(foregroundWindowHandle);
		float dpiScale = Mathf.Max(0.01f, WindowsAPI.GetDPIScale());
		var context = new WindowEnumContext
		{
			Manager = this,
			DpiScale = dpiScale,
			ZOrder = 0,
		};

		GCHandle gcHandle = GCHandle.Alloc(context);
		try
		{
			WindowsAPI.EnumWindows(EnumWindowsCallback, GCHandle.ToIntPtr(gcHandle));
		}
		finally
		{
			gcHandle.Free();
		}

		if (enableDebugLog)
		{
			TestLog.Info(LogTag, $"Refreshed windows. count={_windows.Count}, taskbarTop={TaskbarTopY:0.##}, foregroundMaximized={IsForegroundMaximized}, monitor={_currentMonitorDeviceString}, monitorSize={_currentMonitorSize}");
		}
#else
		IsForegroundMaximized = false;
#endif
	}

	public bool TryGetLandingY(float screenX, float currentScreenY, out float landingY, out WindowEntry supportWindow)
	{
		// 既定の着地点はタスクバー上端（なければ画面下端）。
		supportWindow = default;
		landingY = Mathf.Clamp(TaskbarTopY, 0f, Screen.height);

		if (!HasTaskbar)
		{
			landingY = 0f;
		}

		if (IsForegroundMaximized)
		{
			return true;
		}

		bool foundWindow = false;
		float bestY = landingY;

		for (int i = 0; i < _windows.Count; i++)
		{
			WindowEntry candidate = _windows[i];
			if (candidate.IsMaximized)
			{
				// 最大化ウィンドウより背面の候補は評価しない。
				break;
			}

			if (screenX < candidate.Rect.xMin || screenX > candidate.Rect.xMax)
			{
				continue;
			}

			float topY = candidate.Rect.yMax;
			if (topY > currentScreenY + Mathf.Max(0f, maxAllowedAboveCurrentPixels))
			{
				continue;
			}

			if (topY < bestY)
			{
				continue;
			}

			if (IsTopEdgeOccluded(screenX, topY, i))
			{
				// 上辺直下が前面ウィンドウで塞がれているため着地不可。
				continue;
			}

			bestY = topY;
			supportWindow = candidate;
			foundWindow = true;
		}

		landingY = Mathf.Clamp(bestY, 0f, Screen.height);
		return foundWindow || HasTaskbar;
	}

	public bool TryGetLandingWorldPosition(Camera targetCamera, Vector3 currentWorldPosition, out Vector3 landingWorldPosition, out WindowEntry supportWindow)
	{
		// スクリーン空間で着地点を決めてから同一深度でワールドへ戻す。
		landingWorldPosition = currentWorldPosition;
		supportWindow = default;

		if (targetCamera == null)
		{
			return false;
		}

		Vector3 screenPosition = targetCamera.WorldToScreenPoint(currentWorldPosition);
		if (screenPosition.z <= 0f)
		{
			return false;
		}

		if (!TryGetLandingY(screenPosition.x, screenPosition.y, out float landingScreenY, out supportWindow))
		{
			return false;
		}

		if (float.IsNaN(landingScreenY) || float.IsInfinity(landingScreenY))
		{
			return false;
		}

		landingWorldPosition = targetCamera.ScreenToWorldPoint(new Vector3(screenPosition.x, landingScreenY, screenPosition.z));
		return true;
	}

	public bool IsBelowTaskbar(float screenY)
	{
		if (!HasTaskbar)
		{
			return false;
		}

		return screenY < TaskbarTopY;
	}

	public bool TryGetWindowEntry(IntPtr handle, out WindowEntry entry)
	{
		entry = default;
		if (handle == IntPtr.Zero)
		{
			return false;
		}

		for (int i = 0; i < _windows.Count; i++)
		{
			if (_windows[i].Handle == handle)
			{
				entry = _windows[i];
				return true;
			}
		}

		return false;
	}

	public string GetWindowDebugLabel(IntPtr handle)
	{
		if (handle == IntPtr.Zero)
		{
			return "0x0";
		}

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
		string title = GetWindowTitle(handle);
		string className = GetWindowClassName(handle);
		return $"0x{handle.ToInt64():X} title='{title}' class='{className}'";
#else
		return $"0x{handle.ToInt64():X}";
#endif
	}

	public bool TryGetTopWindowAtPoint(float screenX, float screenY, out WindowEntry entry)
	{
		entry = default;

		for (int i = 0; i < _windows.Count; i++)
		{
			Rect rect = _windows[i].Rect;
			if (screenX < rect.xMin || screenX > rect.xMax)
			{
				continue;
			}

			if (screenY < rect.yMin || screenY > rect.yMax)
			{
				continue;
			}

			entry = _windows[i];
			return true;
		}

		return false;
	}

	private bool IsTopEdgeOccluded(float screenX, float topY, int candidateIndex)
	{
		// 候補より前面のウィンドウだけを見て、上辺直下の遮蔽を判定する。
		float probeY = topY - Mathf.Max(0.01f, edgeOcclusionProbeOffsetPixels);

		for (int i = 0; i < candidateIndex; i++)
		{
			Rect frontRect = _windows[i].Rect;
			if (screenX < frontRect.xMin || screenX > frontRect.xMax)
			{
				continue;
			}

			if (probeY >= frontRect.yMin && probeY <= frontRect.yMax)
			{
				return true;
			}
		}

		return false;
	}

	private void UpdateTaskbarState()
	{
		// 可能なら現在モニタの作業領域からタスクバー高さを算出する。
		if (_hasCurrentMonitorMetrics)
		{
			float workBottomLocal = _currentWorkBottom - _currentMonitorOrigin.y;
			float taskbarTop = _currentMonitorSize.y - workBottomLocal;
			TaskbarTopY = Mathf.Clamp(taskbarTop, 0f, Screen.height);
			HasTaskbar = TaskbarTopY > 0.5f;
			TaskbarRect = HasTaskbar
				? Rect.MinMaxRect(0f, 0f, Screen.width, TaskbarTopY)
				: Rect.zero;
			return;
		}

		HasTaskbar = WindowsAPI.TryGetTaskbarRect(out Rect taskbarRect);
		TaskbarRect = taskbarRect;
		TaskbarTopY = HasTaskbar ? Mathf.Clamp(taskbarRect.yMax, 0f, Screen.height) : 0f;
	}

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetWindowTextLength(IntPtr hWnd);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

	[System.Runtime.InteropServices.DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

	private static string GetWindowTitle(IntPtr hWnd)
	{
		int length = GetWindowTextLength(hWnd);
		if (length <= 0)
		{
			return string.Empty;
		}

		var builder = new StringBuilder(length + 1);
		GetWindowText(hWnd, builder, builder.Capacity);
		return builder.ToString();
	}

	private static string GetWindowClassName(IntPtr hWnd)
	{
		var builder = new StringBuilder(256);
		GetClassName(hWnd, builder, builder.Capacity);
		return builder.ToString();
	}

	[MonoPInvokeCallback(typeof(WindowsAPI.EnumWindowsProc))]
	private static bool EnumWindowsCallback(IntPtr hWnd, IntPtr lParam)
	{
		// Z順のまま収集し、最大化ウィンドウは着地不可の打ち切り判定に使う。
		GCHandle handle = GCHandle.FromIntPtr(lParam);
		if (!(handle.Target is WindowEnumContext context) || context.Manager == null)
		{
			return true;
		}

		TestWindowManager manager = context.Manager;
		if (hWnd == IntPtr.Zero || hWnd == manager._currentWindowHandle || manager.IsOwnProcessWindow(hWnd))
		{
			return true;
		}

		if (manager.ShouldExcludeWindow(hWnd))
		{
			return true;
		}

		if (!manager.IsWindowOnCurrentMonitor(hWnd))
		{
			return true;
		}

		if (!WindowsAPI.IsWindowVisible(hWnd))
		{
			return true;
		}

		if (WindowsAPI.IsWindowMinimized(hWnd))
		{
			return true;
		}

		bool isMaximized = WindowsAPI.IsWindowMaximized(hWnd);

		if (!WindowsAPI.GetWindowRect(hWnd, out var rect))
		{
			return true;
		}

		Rect unityRect = manager.ToUnityScreenRect(rect, context.DpiScale);
		if (unityRect.width <= 10f || unityRect.height <= 10f)
		{
			return true;
		}

		manager._windows.Add(new WindowEntry
		{
			Handle = hWnd,
			Rect = unityRect,
			ZOrder = context.ZOrder++,
			IsMaximized = isMaximized
		});

		return true;
	}

	private bool IsOwnProcessWindow(IntPtr hWnd)
	{
		if (hWnd == IntPtr.Zero || _currentProcessId == 0)
		{
			return false;
		}

		GetWindowThreadProcessId(hWnd, out uint windowProcessId);
		return windowProcessId == (uint)_currentProcessId;
	}

	private bool ShouldExcludeWindow(IntPtr hWnd)
	{
		string className = GetWindowClassName(hWnd);
		for (int i = 0; i < ExcludedWindowClasses.Length; i++)
		{
			if (string.Equals(className, ExcludedWindowClasses[i], StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		string title = GetWindowTitle(hWnd);
		if (string.IsNullOrWhiteSpace(title))
		{
			return true;
		}

		return false;
	}

	private bool IsForegroundMaximizedByOtherProcessWindow(IntPtr foregroundHwnd)
	{
		if (foregroundHwnd == IntPtr.Zero)
		{
			return false;
		}

		if (IsOwnProcessWindow(foregroundHwnd))
		{
			return false;
		}

		if (_currentWindowHandle != IntPtr.Zero && WindowsAPI.AreSameRootOwnerWindow(_currentWindowHandle, foregroundHwnd))
		{
			return false;
		}

		return WindowsAPI.IsWindowMaximized(foregroundHwnd);
	}

	private void UpdateCurrentMonitorMetrics()
	{
		_hasCurrentMonitorMetrics = false;
		_currentMonitorOrigin = Vector2.zero;
		_currentMonitorSize = new Vector2(Screen.width, Screen.height);
		_currentWorkBottom = 0f;
		_currentMonitorDeviceString = string.Empty;

		if (_currentWindowHandle == IntPtr.Zero)
		{
			return;
		}

		if (!WindowsAPI.TryGetWindowMonitorRects(_currentWindowHandle, out Rect monitorRect, out Rect workRect, out string deviceString))
		{
			return;
		}

		float dpiScale = Mathf.Max(0.01f, WindowsAPI.GetDPIScale());
		float monitorLeft = monitorRect.xMin / dpiScale;
		float monitorTop = monitorRect.yMin / dpiScale;
		float monitorRight = monitorRect.xMax / dpiScale;
		float monitorBottom = monitorRect.yMax / dpiScale;

		_currentMonitorOrigin = new Vector2(monitorLeft, monitorTop);
		_currentMonitorSize = new Vector2(Mathf.Max(1f, monitorRight - monitorLeft), Mathf.Max(1f, monitorBottom - monitorTop));
		_currentWorkBottom = workRect.yMax / dpiScale;
		_currentMonitorDeviceString = deviceString ?? string.Empty;
		_hasCurrentMonitorMetrics = true;
	}

	private bool IsWindowOnCurrentMonitor(IntPtr hWnd)
	{
		if (!restrictToCurrentMonitor || !_hasCurrentMonitorMetrics)
		{
			return true;
		}

		if (string.IsNullOrWhiteSpace(_currentMonitorDeviceString))
		{
			return true;
		}

		if (!WindowsAPI.TryGetWindowMonitorDeviceString(hWnd, out string deviceString))
		{
			return true;
		}

		return string.Equals(_currentMonitorDeviceString, deviceString, StringComparison.OrdinalIgnoreCase);
	}

	private Rect ToUnityScreenRect(WindowsAPI.RECT rect, float dpiScale)
	{
		// Win32座標(左上原点)をUnity座標(左下原点)へ変換する。
		if (_hasCurrentMonitorMetrics)
		{
			float monitorLeft = rect.left / dpiScale - _currentMonitorOrigin.x;
			float monitorRight = rect.right / dpiScale - _currentMonitorOrigin.x;
			float monitorTop = rect.top / dpiScale - _currentMonitorOrigin.y;
			float monitorBottom = rect.bottom / dpiScale - _currentMonitorOrigin.y;

			float monitorUnityTop = Screen.height - monitorTop;
			float monitorUnityBottom = Screen.height - monitorBottom;
			return Rect.MinMaxRect(monitorLeft, monitorUnityBottom, monitorRight, monitorUnityTop);
		}

		float left = rect.left / dpiScale;
		float right = rect.right / dpiScale;
		float top = rect.top / dpiScale;
		float bottom = rect.bottom / dpiScale;

		float unityTop = Screen.height - top;
		float unityBottom = Screen.height - bottom;
		return Rect.MinMaxRect(left, unityBottom, right, unityTop);
	}
#endif

}
