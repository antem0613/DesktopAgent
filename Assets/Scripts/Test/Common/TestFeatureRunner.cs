using System.Collections.Generic;
using UnityEngine;

public class TestFeatureRunner : MonoBehaviour
{
    private const string LogTag = nameof(TestFeatureRunner);
    private const string ExternalUiProcessRoleArgument = "--external-ui-process";

    [SerializeField] private bool autoCollectFromSameObject = true;
    [SerializeField] private bool runInExternalUiProcess = false;
    [SerializeField] private List<MonoBehaviour> featureBehaviours = new();
    [SerializeField] private TestFeatureSwitchboard featureSwitchboard;

    private readonly List<ITestFeatureComponent> _features = new();
    private bool _isExternalUiProcess;

    private bool ShouldSkipInExternalUiProcess => _isExternalUiProcess && !runInExternalUiProcess;

    private void Awake()
    {
        _isExternalUiProcess = IsExternalUiProcess();

        if (featureSwitchboard == null)
        {
            featureSwitchboard = GetComponent<TestFeatureSwitchboard>();
        }

        RebuildFeatureCache();
        TestLog.Info(LogTag, $"Feature cache rebuilt. count={_features.Count}");
    }

    private void Start()
    {
        if (ShouldSkipInExternalUiProcess)
        {
            TestLog.Info(LogTag, "Start skipped in external-ui process.");
            return;
        }

        int startedCount = 0;
        for (int i = 0; i < _features.Count; i++)
        {
            if (IsRuntimeEnabled(_features[i]))
            {
                _features[i].OnTestStart();
                startedCount++;
            }
        }

        TestLog.Info(LogTag, $"Test start completed. enabledFeatures={startedCount}");
    }

    private void Update()
    {
        if (ShouldSkipInExternalUiProcess)
        {
            return;
        }

        float deltaTime = Time.deltaTime;
        for (int i = 0; i < _features.Count; i++)
        {
            if (IsRuntimeEnabled(_features[i]))
            {
                _features[i].OnTestTick(deltaTime);
            }
        }
    }

    private void OnDisable()
    {
        if (ShouldSkipInExternalUiProcess)
        {
            return;
        }

        for (int i = 0; i < _features.Count; i++)
        {
            _features[i].OnTestStop();
        }

        TestLog.Info(LogTag, "Test stop completed.");
    }

    public void RebuildFeatureCache()
    {
        _features.Clear();
        var visited = new HashSet<ITestFeatureComponent>();

        if (featureBehaviours != null)
        {
            for (int i = 0; i < featureBehaviours.Count; i++)
            {
                AddFeature(featureBehaviours[i], visited);
            }
        }

        if (_features.Count == 0 && autoCollectFromSameObject)
        {
            var components = GetComponents<MonoBehaviour>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == this)
                {
                    continue;
                }

                AddFeature(components[i], visited);
            }
        }
    }

    private void AddFeature(MonoBehaviour behaviour, HashSet<ITestFeatureComponent> visited)
    {
        if (behaviour is not ITestFeatureComponent feature)
        {
            return;
        }

        if (!visited.Add(feature))
        {
            return;
        }

        _features.Add(feature);
    }

    private static bool IsRuntimeEnabledInternal(ITestFeatureComponent feature)
    {
        return IsRuntimeEnabledInternal(feature, null);
    }

    private static bool IsRuntimeEnabledInternal(ITestFeatureComponent feature, TestFeatureSwitchboard switchboard)
    {
        if (!feature.IsTestEnabled)
        {
            return false;
        }

        if (switchboard != null && !switchboard.IsFeatureEnabled(feature))
        {
            return false;
        }

        if (feature is Behaviour behaviour)
        {
            return behaviour.isActiveAndEnabled;
        }

        return true;
    }

    private bool IsRuntimeEnabled(ITestFeatureComponent feature)
    {
        return IsRuntimeEnabledInternal(feature, featureSwitchboard);
    }

    private static bool IsExternalUiProcess()
    {
        try
        {
            var args = System.Environment.GetCommandLineArgs();
            if (args == null)
            {
                return false;
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], ExternalUiProcessRoleArgument, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }
}