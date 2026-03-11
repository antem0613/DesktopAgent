using UnityEngine;

public class TestRespawnByKeyComponent : MonoBehaviour, ITestFeatureComponent
{
    private const string LogTag = nameof(TestRespawnByKeyComponent);

    [SerializeField] private bool runTest = true;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Transform targetTransform;
    [SerializeField] private TestGroundHeightSource groundHeightSource;
    [SerializeField] private TestFallPhysicsComponent fallPhysicsComponent;
    [SerializeField] private TestCharacterSpawnComponent characterSpawnComponent;
    [SerializeField] private bool respawnOnStart = true;
    [SerializeField] private KeyCode respawnKey = KeyCode.R;
    [SerializeField] private float respawnHeightPixels = 240f;
    [SerializeField] private bool resetFallSpeedOnRespawn = true;
    private bool _missingReferenceWarningLogged;
    private bool _cameraResolvedLogged;
    private bool _targetResolvedLogged;
    private bool _fallComponentResolvedLogged;
    private bool _groundSourceResolvedLogged;

    public bool IsTestEnabled => runTest;

    public void OnTestStart()
    {
        _missingReferenceWarningLogged = false;
        _cameraResolvedLogged = false;
        _targetResolvedLogged = false;
        _fallComponentResolvedLogged = false;
        _groundSourceResolvedLogged = false;
        TestLog.Info(LogTag, $"Started. respawnKey={respawnKey}, respawnHeight={respawnHeightPixels}");

        if (respawnOnStart)
        {
            Respawn();
        }
    }

    public void OnTestTick(float deltaTime)
    {
        if (Input.GetKeyDown(respawnKey))
        {
            Respawn();
        }
    }

    public void OnTestStop()
    {
        TestLog.Info(LogTag, "Stopped.");
    }

    public bool Respawn()
    {
        if (!TryResolveReferences())
        {
            if (!_missingReferenceWarningLogged)
            {
                _missingReferenceWarningLogged = true;
            }
            return false;
        }

        _missingReferenceWarningLogged = false;

        if (!ScreenSpaceTransformUtility.TryGetScreenPosition(targetCamera, targetTransform, out var screenPosition))
        {
            TestLog.Warning(LogTag, "Failed to read current screen position.");
            return false;
        }

        float groundY = groundHeightSource != null ? groundHeightSource.GroundY : 0f;
        screenPosition.y = groundY + Mathf.Max(0f, respawnHeightPixels);

        bool moved = ScreenSpaceTransformUtility.TrySetScreenPosition(targetCamera, targetTransform, screenPosition, screenPosition.z);
        if (moved && resetFallSpeedOnRespawn && fallPhysicsComponent != null)
        {
            fallPhysicsComponent.ResetFallState(0f, false);
        }

        if (moved)
        {
            TestLog.Info(LogTag, $"Respawn succeeded. targetY={screenPosition.y}");
        }
        else
        {
            TestLog.Warning(LogTag, "Respawn failed when applying screen position.");
        }

        return moved;
    }

    private bool TryResolveReferences()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
            if (targetCamera != null && !_cameraResolvedLogged)
            {
                _cameraResolvedLogged = true;
                TestLog.Info(LogTag, $"Camera resolved. name={targetCamera.name}");
            }
        }

        if (fallPhysicsComponent == null)
        {
            fallPhysicsComponent = GetComponent<TestFallPhysicsComponent>();
            if (fallPhysicsComponent != null && !_fallComponentResolvedLogged)
            {
                _fallComponentResolvedLogged = true;
                TestLog.Info(LogTag, "FallPhysics component resolved from same object.");
            }
        }

        if (groundHeightSource == null)
        {
            groundHeightSource = GetComponent<TestGroundHeightSource>();
            if (groundHeightSource != null && !_groundSourceResolvedLogged)
            {
                _groundSourceResolvedLogged = true;
                TestLog.Info(LogTag, "Ground height source resolved from same object.");
            }
        }

        if (characterSpawnComponent == null)
        {
            characterSpawnComponent = GetComponent<TestCharacterSpawnComponent>();
        }

        if (targetTransform == null && characterSpawnComponent != null && characterSpawnComponent.ModelContainer != null)
        {
            targetTransform = characterSpawnComponent.ModelContainer.transform;
            if (!_targetResolvedLogged)
            {
                _targetResolvedLogged = true;
                TestLog.Info(LogTag, "Target transform resolved from TestCharacterSpawnComponent.ModelContainer.");
            }
        }

        return targetCamera != null && targetTransform != null;
    }
}