using UnityEngine;

public class TestGroundHeightSource : MonoBehaviour
{
    private const string LogTag = nameof(TestGroundHeightSource);

    [SerializeField] private float groundYPixels = 0f;
    [SerializeField] private float groundMarginPixels = 0f;
    [SerializeField] private bool useTaskbarTopAsMinimumGround = true;

    private float _lastLoggedGroundY = float.NaN;

    public float GroundY
    {
        get
        {
            float baseGroundY = groundYPixels + groundMarginPixels;
            if (!useTaskbarTopAsMinimumGround)
            {
                return baseGroundY;
            }

            float taskbarTopY = GetTaskbarTopGroundYPixels(out _);
            if (taskbarTopY <= 0f)
            {
                return baseGroundY;
            }

            return Mathf.Max(baseGroundY, taskbarTopY);
        }
    }

    private void Start()
    {
        LogGroundYIfChanged();
    }

    private void OnValidate()
    {
        LogGroundYIfChanged();
    }

    private void LogGroundYIfChanged()
    {
        float baseGroundY = groundYPixels + groundMarginPixels;
        string source = "None";
        float taskbarTopY = 0f;
        if (useTaskbarTopAsMinimumGround)
        {
            taskbarTopY = GetTaskbarTopGroundYPixels(out source);
        }

        float currentGroundY = useTaskbarTopAsMinimumGround && taskbarTopY > 0f
            ? Mathf.Max(baseGroundY, taskbarTopY)
            : baseGroundY;

        if (Mathf.Approximately(_lastLoggedGroundY, currentGroundY))
        {
            return;
        }

        _lastLoggedGroundY = currentGroundY;

        if (!useTaskbarTopAsMinimumGround)
        {
            TestLog.Info(LogTag, $"GroundY updated. value={currentGroundY}, useTaskbarTopAsMinimumGround=false");
            return;
        }

        TestLog.Info(
            LogTag,
            $"GroundY updated. value={currentGroundY}, base={baseGroundY}, taskbar={taskbarTopY}, source={source}, screenW={Screen.width}, screenH={Screen.height}");
    }

    private static float GetTaskbarTopGroundYPixels(out string source)
    {
        source = "None";

#if UNITY_EDITOR
        var pseudoBars = FindObjectsByType<PseudoTaskbar>(FindObjectsSortMode.None);
        if (pseudoBars != null && pseudoBars.Length > 0)
        {
            float bestPseudo = 0f;
            for (int i = 0; i < pseudoBars.Length; i++)
            {
                var bar = pseudoBars[i];
                if (bar == null || !bar.IsEnabled)
                {
                    continue;
                }

                if (bar.TryGetScreenRect(out var rect))
                {
                    bestPseudo = Mathf.Max(bestPseudo, rect.yMax);
                }
            }

            if (bestPseudo > 0f)
            {
                source = "PseudoTaskbar";
                return Mathf.Clamp(bestPseudo, 0f, Screen.height);
            }
        }
#endif

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        float best = 0f;

        if (NativeWindowApi.TryGetTaskbarRect(out var taskbarRect))
        {
            float nativeRectY = Mathf.Ceil(taskbarRect.yMax);
            if (nativeRectY > 0f)
            {
                best = Mathf.Max(best, nativeRectY);
                source = "NativeTaskbarRect";
            }
        }

        if (WindowsAPI.TryGetWorkArea(out var workArea))
        {
            float dpiScale = Mathf.Max(0.01f, WindowsExplorerUtility.GetDPIScale());

            float rawY = Mathf.Ceil(Screen.height - workArea.bottom);
            if (rawY > best)
            {
                best = rawY;
                source = "WorkAreaRaw";
            }

            float scaledY = Mathf.Ceil(Screen.height - (workArea.bottom / dpiScale));
            if (scaledY > best)
            {
                best = scaledY;
                source = "WorkAreaScaled";
            }
        }

        return Mathf.Clamp(best, 0f, Screen.height);
#else
        return 0f;
#endif
    }
}
