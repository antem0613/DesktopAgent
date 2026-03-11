using System.Collections.Generic;
using UniVRM10;
using UnityEngine;

/// <summary>
/// VRMの表情更新をまとめて管理するコンポーネントです（インスペクター操作と実行時オーバーライド）。
/// </summary>
public sealed class ExpressionController : MonoBehaviour
{
    /// <summary>
    /// 表情パラメータをまとめた構造体です（Customは含みません）。
    /// </summary>
    public struct ExpressionParameters
    {
        public float Happy;
        public float Angry;
        public float Sad;
        public float Relaxed;
        public float Surprised;
        public float Neutral;
        public float Aa;
        public float Ih;
        public float Ou;
        public float Ee;
        public float Oh;
        public float Blink;
        public float BlinkLeft;
        public float BlinkRight;
        public float LookUp;
        public float LookDown;
        public float LookLeft;
        public float LookRight;
    }

    [SerializeField]
    private CharacterManager characterManager;

    [SerializeField]
    private bool autoApply = true;

    [SerializeField]
    private bool clearOthersBeforeApply = false;

    [SerializeField]
    private bool useInspectorWeights = true;

    [Header("Emotion")]
    [Range(0f, 1f)]
    [SerializeField] private float happy = 0f;
    [Range(0f, 1f)]
    [SerializeField] private float angry = 0f;
    [Range(0f, 1f)]
    [SerializeField] private float sad = 0f;
    [Range(0f, 1f)]
    [SerializeField] private float relaxed = 0f;
    [Range(0f, 1f)]
    [SerializeField] private float surprised = 0f;
    [Range(0f, 1f)]
    [SerializeField] private float neutral = 0f;

    [Header("Mouth")]
    [Range(0f, 1f)]
    [SerializeField] private float aa = 0f;
    [Range(0f, 1f)]
    [SerializeField] private float ih = 0f;
    [Range(0f, 1f)]
    [SerializeField] private float ou = 0f;
    [Range(0f, 1f)]
    [SerializeField] private float ee = 0f;
    [Range(0f, 1f)]
    [SerializeField] private float oh = 0f;

    [Header("Blink")]
    [Range(0f, 1f)]
    [SerializeField] private float blink = 0f;
    [Range(0f, 1f)]
    [SerializeField] private float blinkLeft = 0f;
    [Range(0f, 1f)]
    [SerializeField] private float blinkRight = 0f;

    [Header("Look At")]
    [Range(0f, 1f)]
    [SerializeField] private float lookUp = 0f;
    [Range(0f, 1f)]
    [SerializeField] private float lookDown = 0f;
    [Range(0f, 1f)]
    [SerializeField] private float lookLeft = 0f;
    [Range(0f, 1f)]
    [SerializeField] private float lookRight = 0f;

    [Header("Auto Blink")]
    [SerializeField] private bool autoBlinkEnabled = true;
    [SerializeField] private bool autoBlinkOverridesManual = true;
    [SerializeField] private bool useUnscaledTime = true;
    [SerializeField] private float minIntervalSeconds = 2.5f;
    [SerializeField] private float maxIntervalSeconds = 6.0f;
    [SerializeField] private float closeDurationSeconds = 0.05f;
    [SerializeField] private float holdClosedSeconds = 0.02f;
    [SerializeField] private float openDurationSeconds = 0.08f;

    // LLM/Animator など外部入力のオーバーライド値。
    private readonly Dictionary<ExpressionPreset, float> presetOverrides = new Dictionary<ExpressionPreset, float>();
    private readonly Dictionary<string, float> customOverrides = new Dictionary<string, float>(System.StringComparer.OrdinalIgnoreCase);

    private float nextBlinkTime;
    private float blinkStartTime;
    private bool isBlinking;
    private float currentAutoBlinkWeight;

    /// <summary>
    /// 次の瞬きタイミングを初期化します。
    /// </summary>
    private void Start()
    {
        ScheduleNextBlink(GetNow());
    }

    /// <summary>
    /// 自動瞬きを更新し、必要なら表情を適用します。
    /// </summary>
    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        float now = GetNow();
        UpdateAutoBlink(now);

        if (!autoApply)
        {
            return;
        }

        ApplyExpressions();
    }

    /// <summary>
    /// 表情ウェイト（インスペクター + オーバーライド）をキャラクターへ適用します。
    /// </summary>
    [ContextMenu("Apply Expressions")]
    public void ApplyExpressions()
    {
        var target = characterManager != null ? characterManager : CharacterManager.Instance;
        if (target == null)
        {
            return;
        }

        if (clearOthersBeforeApply)
        {
            //target.TryClearAllExpressions();
        }

        ApplyPreset(target, ExpressionPreset.happy, GetPresetWeight(ExpressionPreset.happy, happy));
        ApplyPreset(target, ExpressionPreset.angry, GetPresetWeight(ExpressionPreset.angry, angry));
        ApplyPreset(target, ExpressionPreset.sad, GetPresetWeight(ExpressionPreset.sad, sad));
        ApplyPreset(target, ExpressionPreset.relaxed, GetPresetWeight(ExpressionPreset.relaxed, relaxed));
        ApplyPreset(target, ExpressionPreset.surprised, GetPresetWeight(ExpressionPreset.surprised, surprised));
        ApplyPreset(target, ExpressionPreset.neutral, GetPresetWeight(ExpressionPreset.neutral, neutral));

        ApplyPreset(target, ExpressionPreset.blinkLeft, GetPresetWeight(ExpressionPreset.blinkLeft, blinkLeft));
        ApplyPreset(target, ExpressionPreset.blinkRight, GetPresetWeight(ExpressionPreset.blinkRight, blinkRight));

        ApplyPreset(target, ExpressionPreset.lookUp, GetPresetWeight(ExpressionPreset.lookUp, lookUp));
        ApplyPreset(target, ExpressionPreset.lookDown, GetPresetWeight(ExpressionPreset.lookDown, lookDown));
        ApplyPreset(target, ExpressionPreset.lookLeft, GetPresetWeight(ExpressionPreset.lookLeft, lookLeft));
        ApplyPreset(target, ExpressionPreset.lookRight, GetPresetWeight(ExpressionPreset.lookRight, lookRight));

        float blinkWeight = GetPresetWeight(ExpressionPreset.blink, blink);
        if (autoBlinkEnabled)
        {
            if (autoBlinkOverridesManual)
            {
                blinkWeight = Mathf.Max(blinkWeight, currentAutoBlinkWeight);
            }
            else
            {
                blinkWeight = Mathf.Clamp01(blinkWeight + currentAutoBlinkWeight);
            }
        }
        ApplyPreset(target, ExpressionPreset.blink, blinkWeight);

        foreach (var entry in customOverrides)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                continue;
            }

            //target.TrySetExpression(entry.Key, Mathf.Clamp01(entry.Value));
        }
    }

    /// <summary>
    /// プリセット表情のオーバーライド値を設定します。
    /// </summary>
    public void SetPresetWeight(ExpressionPreset preset, float weight, bool applyImmediately = false)
    {
        presetOverrides[preset] = Mathf.Clamp01(weight);
        if (applyImmediately)
        {
            ApplyExpressions();
        }
    }

    /// <summary>
    /// プリセットのオーバーライドを解除します。
    /// </summary>
    public void ClearPresetOverride(ExpressionPreset preset)
    {
        presetOverrides.Remove(preset);
    }

    /// <summary>
    /// カスタム表情のオーバーライド値を名前で設定します。
    /// </summary>
    public void SetCustomExpressionWeight(string expressionName, float weight, bool applyImmediately = false)
    {
        if (string.IsNullOrWhiteSpace(expressionName))
        {
            return;
        }

        customOverrides[expressionName] = Mathf.Clamp01(weight);
        if (applyImmediately)
        {
            ApplyExpressions();
        }
    }

    /// <summary>
    /// カスタム表情のオーバーライドを解除します。
    /// </summary>
    public void ClearCustomOverride(string expressionName)
    {
        if (string.IsNullOrWhiteSpace(expressionName))
        {
            return;
        }

        customOverrides.Remove(expressionName);
    }

    /// <summary>
    /// すべてのオーバーライドを解除します。
    /// </summary>
    public void ClearAllOverrides()
    {
        presetOverrides.Clear();
        customOverrides.Clear();
    }

    /// <summary>
    /// インスペクターの表情パラメータをまとめて取得します。
    /// </summary>
    public ExpressionParameters GetInspectorParameters()
    {
        return new ExpressionParameters
        {
            Happy = happy,
            Angry = angry,
            Sad = sad,
            Relaxed = relaxed,
            Surprised = surprised,
            Neutral = neutral,
            Aa = aa,
            Ih = ih,
            Ou = ou,
            Ee = ee,
            Oh = oh,
            Blink = blink,
            BlinkLeft = blinkLeft,
            BlinkRight = blinkRight,
            LookUp = lookUp,
            LookDown = lookDown,
            LookLeft = lookLeft,
            LookRight = lookRight,
        };
    }

    /// <summary>
    /// インスペクターの表情パラメータをまとめて設定します。
    /// </summary>
    public void SetInspectorParameters(ExpressionParameters parameters, bool applyImmediately = false)
    {
        happy = Mathf.Clamp01(parameters.Happy);
        angry = Mathf.Clamp01(parameters.Angry);
        sad = Mathf.Clamp01(parameters.Sad);
        relaxed = Mathf.Clamp01(parameters.Relaxed);
        surprised = Mathf.Clamp01(parameters.Surprised);
        neutral = Mathf.Clamp01(parameters.Neutral);

        aa = Mathf.Clamp01(parameters.Aa);
        ih = Mathf.Clamp01(parameters.Ih);
        ou = Mathf.Clamp01(parameters.Ou);
        ee = Mathf.Clamp01(parameters.Ee);
        oh = Mathf.Clamp01(parameters.Oh);

        blink = Mathf.Clamp01(parameters.Blink);
        blinkLeft = Mathf.Clamp01(parameters.BlinkLeft);
        blinkRight = Mathf.Clamp01(parameters.BlinkRight);

        lookUp = Mathf.Clamp01(parameters.LookUp);
        lookDown = Mathf.Clamp01(parameters.LookDown);
        lookLeft = Mathf.Clamp01(parameters.LookLeft);
        lookRight = Mathf.Clamp01(parameters.LookRight);

        if (applyImmediately)
        {
            ApplyExpressions();
        }
    }

    // 自動瞬きは Blink のみ更新し、適用時に合成します。
    private void UpdateAutoBlink(float now)
    {
        if (!autoBlinkEnabled)
        {
            currentAutoBlinkWeight = 0f;
            return;
        }

        if (!isBlinking)
        {
            if (now >= nextBlinkTime)
            {
                isBlinking = true;
                blinkStartTime = now;
            }
            currentAutoBlinkWeight = 0f;
            return;
        }

        float elapsed = now - blinkStartTime;
        currentAutoBlinkWeight = CalculateBlinkWeight(elapsed);

        float totalDuration = closeDurationSeconds + holdClosedSeconds + openDurationSeconds;
        if (elapsed >= totalDuration)
        {
            isBlinking = false;
            currentAutoBlinkWeight = 0f;
            ScheduleNextBlink(now);
        }
    }

    /// <summary>
    /// 閉じる/保持/開くの時間から瞬きウェイトを計算します。
    /// </summary>
    private float CalculateBlinkWeight(float elapsed)
    {
        if (elapsed <= 0f)
        {
            return 0f;
        }

        float closeDuration = Mathf.Max(0.001f, closeDurationSeconds);
        if (elapsed <= closeDuration)
        {
            return Mathf.Clamp01(elapsed / closeDuration);
        }

        float holdEnd = closeDuration + Mathf.Max(0f, holdClosedSeconds);
        if (elapsed <= holdEnd)
        {
            return 1f;
        }

        float openDuration = Mathf.Max(0.001f, openDurationSeconds);
        float openElapsed = elapsed - holdEnd;
        if (openElapsed <= openDuration)
        {
            float t = 1f - (openElapsed / openDuration);
            return Mathf.Clamp01(t);
        }

        return 0f;
    }

    /// <summary>
    /// ランダム間隔で次の瞬きを予約します。
    /// </summary>
    private void ScheduleNextBlink(float now)
    {
        float min = Mathf.Max(0.1f, minIntervalSeconds);
        float max = Mathf.Max(min, maxIntervalSeconds);
        nextBlinkTime = now + Random.Range(min, max);
    }

    // インスペクター値は無効化やプリセット単位の上書きが可能です。
    private float GetPresetWeight(ExpressionPreset preset, float inspectorValue)
    {
        float weight = useInspectorWeights ? inspectorValue : 0f;
        if (presetOverrides.TryGetValue(preset, out var overrideValue))
        {
            weight = overrideValue;
        }

        return Mathf.Clamp01(weight);
    }

    /// <summary>
    /// 単一プリセットのウェイトを適用します。
    /// </summary>
    private static void ApplyPreset(CharacterManager target, ExpressionPreset preset, float weight)
    {
        //target.TrySetExpressionPreset(preset, Mathf.Clamp01(weight));
    }

    /// <summary>
    /// 自動瞬きで使う時刻ソースを取得します。
    /// </summary>
    private float GetNow()
    {
        return useUnscaledTime ? Time.unscaledTime : Time.time;
    }
}
