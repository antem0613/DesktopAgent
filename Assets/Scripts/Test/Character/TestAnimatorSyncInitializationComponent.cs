using UnityEngine;
using System.Runtime.CompilerServices;

public class TestAnimatorSyncInitializationComponent : MonoBehaviour, ITestFeatureComponent
{
    private const string LogTag = nameof(TestAnimatorSyncInitializationComponent);

    [SerializeField] private bool runTest = true;
    [SerializeField] private TestCharacterSpawnComponent characterSpawnComponent;
    [SerializeField] private Animator directAnimator;
    [SerializeField] private bool applyDefaultCharacterAnimationController = true;
    [SerializeField] private CharacterManager.GroundAction initialGroundAction = CharacterManager.GroundAction.Idle;
    [SerializeField] private bool initialIsFalling;
    [SerializeField] private bool initialIsDragging;
    [SerializeField] private bool initialIsOnWindow;
    [SerializeField] private bool initialIsSittingWindow;
    [SerializeField] private bool initialShouldGoNext;
    [SerializeField] private bool autoInitializeUntilSuccess = true;
    [SerializeField] private bool waitForCharacterSpawnBeforeInitialize = true;

    private AnimatorSync _animatorSync;
    private bool _spawnWaitLogged;

    public bool IsTestEnabled => runTest;
    public bool IsInitialized { get; private set; }
    public Animator TargetAnimator { get; private set; }

    public void OnTestStart()
    {
        _spawnWaitLogged = false;
        InitializeAnimatorSync();
    }

    public void OnTestTick(float deltaTime)
    {
        if (!autoInitializeUntilSuccess || IsInitialized)
        {
            return;
        }

        InitializeAnimatorSync();
    }

    public void OnTestStop()
    {
        LogAnimatorFlags("stopped");
    }

    public bool InitializeAnimatorSync()
    {
        if (waitForCharacterSpawnBeforeInitialize && !IsCharacterSpawnCompleted())
        {
            if (!_spawnWaitLogged)
            {
                _spawnWaitLogged = true;
                TestLog.Info(LogTag, "Animator initialization is waiting for character spawn completion.");
            }

            IsInitialized = false;
            return false;
        }

        _spawnWaitLogged = false;
        IsInitialized = false;

        TargetAnimator = ResolveAnimator();
        if (TargetAnimator == null)
        {
            _animatorSync = null;

            return false;
        }

        if (applyDefaultCharacterAnimationController)
        {
            LoadVRM.UpdateAnimationController(TargetAnimator);
        }

        _animatorSync = new AnimatorSync(TargetAnimator);
        _animatorSync.SetNextGroundAction(initialGroundAction);
        _animatorSync.SetFalling(initialIsFalling);
        _animatorSync.SetDragging(initialIsDragging);
        _animatorSync.SetIsOnWindow(initialIsOnWindow);
        _animatorSync.SetIsSittingWindow(initialIsSittingWindow);
        _animatorSync.SetShouldGoNext(initialShouldGoNext);

        IsInitialized = true;
        LogAnimatorFlags($"initialized animator={TargetAnimator.name} groundAction={initialGroundAction}");
        return true;
    }

    public bool GetBool(string parameterName)
    {
        if (_animatorSync == null)
        {
            return false;
        }

        return _animatorSync.GetBool(parameterName);
    }

    private Animator ResolveAnimator()
    {
        if (characterSpawnComponent == null)
        {
            characterSpawnComponent = GetComponent<TestCharacterSpawnComponent>();
        }

        if (directAnimator != null)
        {
            return directAnimator;
        }

        directAnimator = GetComponent<Animator>();
        if (directAnimator != null)
        {
            return directAnimator;
        }

        if (characterSpawnComponent != null)
        {
            return characterSpawnComponent.ModelAnimator;
        }

        return null;
    }

    private bool IsCharacterSpawnCompleted()
    {
        if (characterSpawnComponent == null)
        {
            characterSpawnComponent = GetComponent<TestCharacterSpawnComponent>();
            if (characterSpawnComponent == null)
            {
                characterSpawnComponent = FindFirstObjectByType<TestCharacterSpawnComponent>();
            }
        }

        if (characterSpawnComponent == null)
        {
            return true;
        }

        return characterSpawnComponent.SpawnedModel != null;
    }

    private void LogAnimatorFlags(string message, [CallerMemberName] string caller = "unknown")
    {
        if (_animatorSync == null)
        {
            return;
        }

        TestLog.Info(
            LogTag,
            $"[{caller}] {message} | IsIdling={_animatorSync.GetBool(Constant.AnimatorIsIdling)} "
            + $"IsWalking={_animatorSync.GetBool(Constant.AnimatorIsWalking)} "
            + $"IsSitting={_animatorSync.GetBool(Constant.AnimatorIsSitting)} "
            + $"IsDragging={_animatorSync.GetBool(Constant.AnimatorIsDragging)} "
            + $"IsFalling={_animatorSync.GetBool(Constant.AnimatorIsFalling)} "
            + $"ShouldGoNext={_animatorSync.GetBool(Constant.AnimatorShouldGoNext)} "
            + $"IsOnWindow={_animatorSync.GetBool(Constant.AnimatorIsOnWindow)} "
            + $"IsSittingWindow={_animatorSync.GetBool(Constant.AnimatorIsSittingWindow)}");
    }
}