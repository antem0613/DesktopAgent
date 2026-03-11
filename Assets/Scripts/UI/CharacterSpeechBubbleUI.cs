using System.Collections;
using TMPro;
using Unity.Logging;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class CharacterSpeechBubbleUI : MonoBehaviour
{
    public enum BubbleScreenEdge
    {
        Left = 0,
        Right = 1,
        Top = 2,
        Bottom = 3,
    }

    [Header("Layout")]
    [SerializeField] private BubbleScreenEdge bubbleScreenEdge = BubbleScreenEdge.Left;
    [SerializeField] private Vector2 edgeOffset = new Vector2(36f, -140f);

    [Header("Auto Offset")]
    [SerializeField] private bool estimateOffsetFromCharacterSize = true;
    [SerializeField] private bool updateEstimatedOffsetContinuously = true;
    [SerializeField] private float estimatedOffsetUpdateIntervalSeconds = 0.2f;
    [SerializeField] private float estimatedHorizontalMarginPixels = 24f;
    [SerializeField] private float estimatedVerticalMarginPixels = 16f;
    [SerializeField] private Camera characterRenderCamera;
    [SerializeField] private string characterAnchorBoneName = nameof(HumanBodyBones.Head);
    [SerializeField] private Vector3 characterAnchorWorldOffset = Vector3.zero;

    [Header("Prefabs")]
    [SerializeField] private GameObject ttsBubblePrefab;
    [SerializeField] private GameObject thinkingBubblePrefab;

    [Header("Flip")]
    [SerializeField] private bool flipBackgroundOnHorizontalEdge = true;
    [SerializeField] private bool flipBackgroundOnVerticalEdge;

    [Header("Bubble Width")]
    [SerializeField] private bool autoSelectLeftRightByBubbleWidth = true;
    [SerializeField] private float horizontalScreenMarginPixels = 12f;
    [SerializeField] private float horizontalEdgeSwitchHysteresisPixels = 36f;

    private Coroutine _ttsHideCoroutine;
    private Vector3 _ttsBackgroundBaseScale = Vector3.one;
    private Vector3 _thinkingBackgroundBaseScale = Vector3.one;
    private bool _baseScaleCached;
    private bool _bubbleInstancesInitialized;
    private RectTransform _bubbleAnchorRoot;
    private GameObject _ttsBubbleObject;
    private TMP_Text _ttsBubbleText;
    private RectTransform _ttsBubbleBackground;
    private Vector3 _ttsTextBaseScale = Vector3.one;
    private GameObject _thinkingBubbleObject;
    private TMP_Text _thinkingBubbleText;
    private RectTransform _thinkingBubbleBackground;
    private Vector3 _thinkingTextBaseScale = Vector3.one;
    private float _nextEstimatedOffsetUpdateTime;
    private BubbleScreenEdge? _lastLoggedResolvedEdge;
    private BubbleScreenEdge _lastResolvedHorizontalEdge = BubbleScreenEdge.Left;

    private void Awake()
    {
        SyncPreferredHorizontalEdgeFromSetting();
        EnsureAnchorRoot();
        EnsureBubbleInstances();
        DeactivateUnmanagedBubbleChildren();
        CacheBaseScaleIfNeeded();
        ApplyEdgeLayout();
        HideTtsBubble();
        HideThinkingBubble();
        Log.Info($"[CharacterSpeechBubbleUI] Initialized on '{name}'.");
    }

    private void OnValidate()
    {
        SyncPreferredHorizontalEdgeFromSetting();
        EnsureAnchorRoot();
        CacheBaseScaleIfNeeded();
        ApplyEdgeLayout();
    }

    private void Update()
    {
        if (!estimateOffsetFromCharacterSize || !updateEstimatedOffsetContinuously)
        {
            return;
        }

        float now = Time.unscaledTime;
        if (now < _nextEstimatedOffsetUpdateTime)
        {
            return;
        }

        _nextEstimatedOffsetUpdateTime = now + Mathf.Max(0.02f, estimatedOffsetUpdateIntervalSeconds);
        ApplyEdgeLayout();
    }

    public void SetBubbleScreenEdge(BubbleScreenEdge edge)
    {
        bubbleScreenEdge = edge;
        SyncPreferredHorizontalEdgeFromSetting();
        ApplyEdgeLayout();
    }

    private void SyncPreferredHorizontalEdgeFromSetting()
    {
        if (bubbleScreenEdge == BubbleScreenEdge.Left || bubbleScreenEdge == BubbleScreenEdge.Right)
        {
            _lastResolvedHorizontalEdge = bubbleScreenEdge;
        }
    }

    public void ShowThinkingBubble(string text)
    {
        if (!EnsureBubbleInstances())
        {
            Log.Warning("[CharacterSpeechBubbleUI] ShowThinkingBubble skipped because bubble instances are not ready.");
            return;
        }

        if (_thinkingBubbleText != null)
        {
            _thinkingBubbleText.text = text ?? string.Empty;
        }

        if (_thinkingBubbleObject != null && !_thinkingBubbleObject.activeSelf)
        {
            _thinkingBubbleObject.SetActive(true);
        }

        ApplyEdgeLayout();
        Log.Info($"[CharacterSpeechBubbleUI] Thinking bubble shown (textLength={(_thinkingBubbleText != null ? _thinkingBubbleText.text.Length : 0)}).");
    }

    public void HideThinkingBubble()
    {
        if (!EnsureBubbleInstances())
        {
            return;
        }

        bool wasVisible = _thinkingBubbleObject != null && _thinkingBubbleObject.activeSelf;

        if (_thinkingBubbleText != null)
        {
            _thinkingBubbleText.text = string.Empty;
        }

        if (_thinkingBubbleObject != null && _thinkingBubbleObject.activeSelf)
        {
            _thinkingBubbleObject.SetActive(false);
        }

        if (wasVisible)
        {
            Log.Info("[CharacterSpeechBubbleUI] Thinking bubble hidden.");
        }
    }

    public void ShowTtsBubble(string text, float hideDelaySeconds)
    {
        if (!EnsureBubbleInstances())
        {
            Log.Warning("[CharacterSpeechBubbleUI] ShowTtsBubble skipped because bubble instances are not ready.");
            return;
        }

        if (_ttsBubbleText != null)
        {
            _ttsBubbleText.text = text ?? string.Empty;
        }

        if (_ttsBubbleObject != null && !_ttsBubbleObject.activeSelf)
        {
            _ttsBubbleObject.SetActive(true);
        }

        if (_ttsHideCoroutine != null)
        {
            StopCoroutine(_ttsHideCoroutine);
            _ttsHideCoroutine = null;
        }

        if (hideDelaySeconds > 0f)
        {
            _ttsHideCoroutine = StartCoroutine(HideTtsAfterDelay(hideDelaySeconds));
        }

        ApplyEdgeLayout();
        Log.Info($"[CharacterSpeechBubbleUI] TTS bubble shown (textLength={(_ttsBubbleText != null ? _ttsBubbleText.text.Length : 0)}, hideDelay={hideDelaySeconds:F2}s).");
    }

    public void HideTtsBubble()
    {
        if (!EnsureBubbleInstances())
        {
            return;
        }

        bool wasVisible = _ttsBubbleObject != null && _ttsBubbleObject.activeSelf;

        if (_ttsHideCoroutine != null)
        {
            StopCoroutine(_ttsHideCoroutine);
            _ttsHideCoroutine = null;
        }

        if (_ttsBubbleText != null)
        {
            _ttsBubbleText.text = string.Empty;
        }

        if (_ttsBubbleObject != null && _ttsBubbleObject.activeSelf)
        {
            _ttsBubbleObject.SetActive(false);
        }

        if (wasVisible)
        {
            Log.Info("[CharacterSpeechBubbleUI] TTS bubble hidden.");
        }
    }

    private IEnumerator HideTtsAfterDelay(float delaySeconds)
    {
        yield return new WaitForSeconds(delaySeconds);
        HideTtsBubble();
    }

    private void CacheBaseScaleIfNeeded()
    {
        if (_ttsBubbleBackground == null || _thinkingBubbleBackground == null)
        {
            if (!EnsureBubbleInstances())
            {
                return;
            }
        }

        if (_baseScaleCached)
        {
            return;
        }

        if (_ttsBubbleBackground != null)
        {
            _ttsBackgroundBaseScale = _ttsBubbleBackground.localScale;
        }

        if (_ttsBubbleText != null)
        {
            _ttsTextBaseScale = _ttsBubbleText.rectTransform.localScale;
        }

        if (_thinkingBubbleBackground != null)
        {
            _thinkingBackgroundBaseScale = _thinkingBubbleBackground.localScale;
        }

        if (_thinkingBubbleText != null)
        {
            _thinkingTextBaseScale = _thinkingBubbleText.rectTransform.localScale;
        }

        _baseScaleCached = true;
    }

    [ContextMenu("Rebuild Bubble Objects From Prefabs")]
    private void RebuildBubbleObjectsFromPrefabs()
    {
        Log.Info($"[CharacterSpeechBubbleUI] Rebuilding bubble instances from prefabs on '{name}'.");
        _bubbleInstancesInitialized = false;

        if (_ttsBubbleObject != null)
        {
            DestroyBubbleObject(_ttsBubbleObject);
            _ttsBubbleObject = null;
        }

        if (_thinkingBubbleObject != null)
        {
            DestroyBubbleObject(_thinkingBubbleObject);
            _thinkingBubbleObject = null;
        }

        _ttsBubbleText = null;
        _thinkingBubbleText = null;
        _ttsBubbleBackground = null;
        _thinkingBubbleBackground = null;
        _baseScaleCached = false;

        EnsureBubbleInstances(forceInstantiate: true);
        DeactivateUnmanagedBubbleChildren();
        CacheBaseScaleIfNeeded();
        ApplyEdgeLayout();
        HideTtsBubble();
        HideThinkingBubble();
    }

    private void DeactivateUnmanagedBubbleChildren()
    {
        if (!EnsureAnchorRoot())
        {
            return;
        }

        int deactivatedCount = 0;
        for (int i = 0; i < _bubbleAnchorRoot.childCount; i++)
        {
            Transform child = _bubbleAnchorRoot.GetChild(i);
            if (child == null)
            {
                continue;
            }

            GameObject childObject = child.gameObject;
            if (childObject == _ttsBubbleObject || childObject == _thinkingBubbleObject)
            {
                continue;
            }

            if (!childObject.activeSelf)
            {
                continue;
            }

            if (childObject.GetComponentInChildren<TMP_Text>(true) == null)
            {
                continue;
            }

            childObject.SetActive(false);
            deactivatedCount++;
        }

        if (deactivatedCount > 0)
        {
            Log.Warning($"[CharacterSpeechBubbleUI] Deactivated unmanaged bubble-like children: {deactivatedCount}");
        }
    }

    private void ApplyEdgeLayout()
    {
        if (!EnsureAnchorRoot())
        {
            return;
        }

        RefreshBubbleLayoutWidths();

        BubbleScreenEdge resolvedEdge = ResolveEdgeByCurrentBubbleWidth();
        Vector2 anchor;
        Vector2 pivot;
        Vector2 estimatedOffset = estimateOffsetFromCharacterSize ? EstimateOffsetFromCharacterSize(resolvedEdge) : Vector2.zero;
        Vector2 additionalOffset = GetAdditionalOffsetByEdge(resolvedEdge);
        Vector2 anchoredPos;

        switch (resolvedEdge)
        {
            case BubbleScreenEdge.Left:
                anchor = new Vector2(0f, 0.5f);
                pivot = new Vector2(0f, 0.5f);
                anchoredPos = estimatedOffset + additionalOffset;
                break;
            case BubbleScreenEdge.Top:
                anchor = new Vector2(0.5f, 1f);
                pivot = new Vector2(0.5f, 1f);
                anchoredPos = estimatedOffset + additionalOffset;
                break;
            case BubbleScreenEdge.Bottom:
                anchor = new Vector2(0.5f, 0f);
                pivot = new Vector2(0.5f, 0f);
                anchoredPos = estimatedOffset + additionalOffset;
                break;
            default:
                anchor = new Vector2(1f, 0.5f);
                pivot = new Vector2(1f, 0.5f);
                anchoredPos = estimatedOffset + additionalOffset;
                break;
        }

        _bubbleAnchorRoot.anchorMin = anchor;
        _bubbleAnchorRoot.anchorMax = anchor;
        _bubbleAnchorRoot.pivot = pivot;
        _bubbleAnchorRoot.anchoredPosition = anchoredPos;

        ApplyBackgroundFlip(resolvedEdge);
    }

    private void ApplyBackgroundFlip(BubbleScreenEdge resolvedEdge)
    {
        CacheBaseScaleIfNeeded();

        bool flipX = flipBackgroundOnHorizontalEdge && resolvedEdge == BubbleScreenEdge.Left;
        bool flipY = flipBackgroundOnVerticalEdge && resolvedEdge == BubbleScreenEdge.Bottom;

        ApplyFlipToBackground(_ttsBubbleBackground, _ttsBackgroundBaseScale, flipX, flipY);
        ApplyFlipToBackground(_thinkingBubbleBackground, _thinkingBackgroundBaseScale, flipX, flipY);
        ApplyCounterFlipToText(_ttsBubbleText, _ttsTextBaseScale, flipX, flipY);
        ApplyCounterFlipToText(_thinkingBubbleText, _thinkingTextBaseScale, flipX, flipY);
    }

    private static void ApplyFlipToBackground(RectTransform background, Vector3 baseScale, bool flipX, bool flipY)
    {
        if (background == null)
        {
            return;
        }

        float sx = Mathf.Abs(baseScale.x) * (flipX ? -1f : 1f);
        float sy = Mathf.Abs(baseScale.y) * (flipY ? -1f : 1f);
        float sz = baseScale.z;
        background.localScale = new Vector3(sx, sy, sz);
    }

    private static void ApplyCounterFlipToText(TMP_Text text, Vector3 baseScale, bool parentFlipX, bool parentFlipY)
    {
        if (text == null)
        {
            return;
        }

        var rect = text.rectTransform;
        float sx = Mathf.Abs(baseScale.x) * (parentFlipX ? -1f : 1f);
        float sy = Mathf.Abs(baseScale.y) * (parentFlipY ? -1f : 1f);
        float sz = baseScale.z;
        rect.localScale = new Vector3(sx, sy, sz);
    }

    private bool EnsureBubbleInstances(bool forceInstantiate = false)
    {
        if (_bubbleInstancesInitialized && !forceInstantiate)
        {
            return true;
        }

        if (!Application.isPlaying)
        {
            return false;
        }

        if (!EnsureAnchorRoot())
        {
            return false;
        }

        if (_ttsBubbleObject == null || forceInstantiate)
        {
            if (_ttsBubbleObject != null && forceInstantiate)
            {
                DestroyBubbleObject(_ttsBubbleObject);
                _ttsBubbleObject = null;
            }

            _ttsBubbleObject = InstantiateBubblePrefab(ttsBubblePrefab, "TtsBubble");
            if (_ttsBubbleObject == null)
            {
                Log.Error("[CharacterSpeechBubbleUI] Failed to instantiate TTS bubble prefab.");
                return false;
            }
        }

        if (_thinkingBubbleObject == null || forceInstantiate)
        {
            if (_thinkingBubbleObject != null && forceInstantiate)
            {
                DestroyBubbleObject(_thinkingBubbleObject);
                _thinkingBubbleObject = null;
            }

            _thinkingBubbleObject = InstantiateBubblePrefab(thinkingBubblePrefab, "ThinkingBubble");
            if (_thinkingBubbleObject == null)
            {
                Log.Error("[CharacterSpeechBubbleUI] Failed to instantiate thinking bubble prefab.");
                return false;
            }
        }

        ResolveBubbleReferencesFromObject(_ttsBubbleObject, ref _ttsBubbleText, ref _ttsBubbleBackground);
        ResolveBubbleReferencesFromObject(_thinkingBubbleObject, ref _thinkingBubbleText, ref _thinkingBubbleBackground);

        // Ensure prefabs do not remain visible by their initial active state.
        if (_ttsBubbleObject != null)
        {
            _ttsBubbleObject.SetActive(false);
        }

        if (_thinkingBubbleObject != null)
        {
            _thinkingBubbleObject.SetActive(false);
        }

        _bubbleInstancesInitialized = true;
        Log.Info($"[CharacterSpeechBubbleUI] Bubble instances initialized (tts='{_ttsBubbleObject?.name}', thinking='{_thinkingBubbleObject?.name}').");
        return true;
    }

    private GameObject InstantiateBubblePrefab(GameObject prefab, string fallbackName)
    {
        if (prefab == null || !EnsureAnchorRoot())
        {
            Log.Error($"[CharacterSpeechBubbleUI] {fallbackName} prefab is not assigned.");
            return null;
        }

        var instance = Instantiate(prefab, _bubbleAnchorRoot);
        instance.name = string.IsNullOrWhiteSpace(prefab.name) ? fallbackName : prefab.name;

        var rectTransform = instance.transform as RectTransform;
        if (rectTransform != null)
        {
            rectTransform.anchoredPosition3D = Vector3.zero;
            rectTransform.localRotation = Quaternion.identity;
            rectTransform.localScale = Vector3.one;
        }

        Log.Info($"[CharacterSpeechBubbleUI] Instantiated bubble prefab '{instance.name}'.");

        return instance;
    }

    private static void ResolveBubbleReferencesFromObject(GameObject bubbleObject, ref TMP_Text bubbleText, ref RectTransform bubbleBackground)
    {
        if (bubbleObject == null)
        {
            return;
        }

        if (bubbleText == null)
        {
            bubbleText = bubbleObject.GetComponentInChildren<TMP_Text>(true);
        }

        if (bubbleBackground == null)
        {
            bubbleBackground = bubbleObject.GetComponent<RectTransform>();
        }

        if (bubbleBackground == null)
        {
            bubbleBackground = bubbleObject.GetComponentInChildren<RectTransform>(true);
        }
    }

    private static void DestroyBubbleObject(GameObject bubbleObject)
    {
        if (bubbleObject == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(bubbleObject);
        }
        else
        {
            DestroyImmediate(bubbleObject);
        }
    }

    private bool EnsureAnchorRoot()
    {
        if (_bubbleAnchorRoot == null)
        {
            _bubbleAnchorRoot = GetComponent<RectTransform>();
        }

        return _bubbleAnchorRoot != null;
    }

    private Vector2 GetAdditionalOffsetByEdge(BubbleScreenEdge edge)
    {
        switch (edge)
        {
            case BubbleScreenEdge.Left:
                return new Vector2(Mathf.Abs(edgeOffset.x), edgeOffset.y);
            case BubbleScreenEdge.Top:
                return new Vector2(edgeOffset.x, -Mathf.Abs(edgeOffset.y));
            case BubbleScreenEdge.Bottom:
                return new Vector2(edgeOffset.x, Mathf.Abs(edgeOffset.y));
            default:
                return new Vector2(-Mathf.Abs(edgeOffset.x), edgeOffset.y);
        }
    }

    private Vector2 EstimateOffsetFromCharacterSize(BubbleScreenEdge edge)
    {
        if (!Application.isPlaying)
        {
            return Vector2.zero;
        }

        if (!TryGetCharacterScreenRect(out Rect characterRect, out Vector2 characterAnchorScreenPoint))
        {
            return Vector2.zero;
        }

        float screenWidth = Mathf.Max(1f, Screen.width);
        float screenHeight = Mathf.Max(1f, Screen.height);
        float xFromCenter = characterAnchorScreenPoint.x - (screenWidth * 0.5f);
        float yFromCenter = characterAnchorScreenPoint.y - (screenHeight * 0.5f);

        switch (edge)
        {
            case BubbleScreenEdge.Left:
            {
                float x = Mathf.Max(0f, characterRect.xMin + Mathf.Max(0f, estimatedHorizontalMarginPixels));
                float y = yFromCenter + estimatedVerticalMarginPixels;
                return new Vector2(x, y);
            }
            case BubbleScreenEdge.Top:
            {
                float y = -Mathf.Max(0f, (screenHeight - characterRect.yMax) + Mathf.Max(0f, estimatedVerticalMarginPixels));
                return new Vector2(xFromCenter, y);
            }
            case BubbleScreenEdge.Bottom:
            {
                float y = Mathf.Max(0f, characterRect.yMin + Mathf.Max(0f, estimatedVerticalMarginPixels));
                return new Vector2(xFromCenter, y);
            }
            default:
            {
                float x = -Mathf.Max(0f, (screenWidth - characterRect.xMax) + Mathf.Max(0f, estimatedHorizontalMarginPixels));
                float y = yFromCenter + estimatedVerticalMarginPixels;
                return new Vector2(x, y);
            }
        }
    }

    private BubbleScreenEdge ResolveEdgeByCurrentBubbleWidth()
    {
        if (!Application.isPlaying)
        {
            return bubbleScreenEdge;
        }

        if (!autoSelectLeftRightByBubbleWidth)
        {
            return bubbleScreenEdge;
        }

        if (bubbleScreenEdge != BubbleScreenEdge.Left && bubbleScreenEdge != BubbleScreenEdge.Right)
        {
            return bubbleScreenEdge;
        }

        if (!TryGetCharacterScreenRect(out _, out Vector2 anchorScreenPoint))
        {
            return bubbleScreenEdge;
        }

        float bubbleWidth = GetCurrentBubbleWidthPixels();
        if (bubbleWidth <= 0f)
        {
            return bubbleScreenEdge;
        }

        float requiredWidth = bubbleWidth + Mathf.Abs(edgeOffset.x) + Mathf.Max(0f, horizontalScreenMarginPixels);
        float leftSpace = anchorScreenPoint.x;
        float rightSpace = Mathf.Max(0f, Screen.width - anchorScreenPoint.x);

        bool canPlaceRight = rightSpace >= requiredWidth;
        bool canPlaceLeft = leftSpace >= requiredWidth;

        BubbleScreenEdge preferred = _lastResolvedHorizontalEdge;
        if (preferred != BubbleScreenEdge.Left && preferred != BubbleScreenEdge.Right)
        {
            preferred = bubbleScreenEdge == BubbleScreenEdge.Left ? BubbleScreenEdge.Left : BubbleScreenEdge.Right;
        }

        if (canPlaceRight && !canPlaceLeft)
        {
            _lastResolvedHorizontalEdge = BubbleScreenEdge.Right;
            return BubbleScreenEdge.Right;
        }

        if (canPlaceLeft && !canPlaceRight)
        {
            _lastResolvedHorizontalEdge = BubbleScreenEdge.Left;
            return BubbleScreenEdge.Left;
        }

        if (canPlaceLeft && canPlaceRight)
        {
            _lastResolvedHorizontalEdge = preferred;
            return preferred;
        }

        float diff = rightSpace - leftSpace;
        float hysteresis = Mathf.Max(0f, horizontalEdgeSwitchHysteresisPixels);

        if (preferred == BubbleScreenEdge.Right)
        {
            if (diff <= -hysteresis)
            {
                _lastResolvedHorizontalEdge = BubbleScreenEdge.Left;
                return BubbleScreenEdge.Left;
            }

            _lastResolvedHorizontalEdge = BubbleScreenEdge.Right;
            return BubbleScreenEdge.Right;
        }

        if (diff >= hysteresis)
        {
            _lastResolvedHorizontalEdge = BubbleScreenEdge.Right;
            return BubbleScreenEdge.Right;
        }

        _lastResolvedHorizontalEdge = BubbleScreenEdge.Left;
        return BubbleScreenEdge.Left;
    }

    private float GetCurrentBubbleWidthPixels()
    {
        if (_ttsBubbleObject != null && _ttsBubbleObject.activeSelf && _ttsBubbleBackground != null)
        {
            return _ttsBubbleBackground.rect.width;
        }

        if (_thinkingBubbleObject != null && _thinkingBubbleObject.activeSelf && _thinkingBubbleBackground != null)
        {
            return _thinkingBubbleBackground.rect.width;
        }

        float ttsWidth = _ttsBubbleBackground != null ? _ttsBubbleBackground.rect.width : 0f;
        float thinkingWidth = _thinkingBubbleBackground != null ? _thinkingBubbleBackground.rect.width : 0f;
        return Mathf.Max(ttsWidth, thinkingWidth);
    }

    private void RefreshBubbleLayoutWidths()
    {
        if (_ttsBubbleBackground != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_ttsBubbleBackground);
        }

        if (_thinkingBubbleBackground != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_thinkingBubbleBackground);
        }
    }

    private bool TryGetCharacterScreenRect(out Rect screenRect, out Vector2 anchorScreenPoint)
    {
        screenRect = default;
        anchorScreenPoint = default;

        if (!Application.isPlaying)
        {
            return false;
        }

        var characterManager = CharacterManager.Instance;
        if (characterManager == null || characterManager.ModelContainer == null)
        {
            return false;
        }

        var renderers = characterManager.ModelContainer.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
        {
            return false;
        }

        bool hasBounds = false;
        Bounds worldBounds = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            if (!hasBounds)
            {
                worldBounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                worldBounds.Encapsulate(renderer.bounds);
            }
        }

        if (!hasBounds)
        {
            return false;
        }

        Camera camera = characterRenderCamera != null ? characterRenderCamera : Camera.main;
        if (camera == null)
        {
            return false;
        }

        if (!TryGetCharacterAnchorWorldPosition(characterManager.ModelContainer, out Vector3 anchorWorldPosition))
        {
            return false;
        }

        Vector3 anchorScreen = camera.WorldToScreenPoint(anchorWorldPosition);
        if (anchorScreen.z <= 0f)
        {
            return false;
        }
        anchorScreenPoint = anchorScreen;

        Vector3 center = worldBounds.center;
        Vector3 extents = worldBounds.extents;

        Vector3[] corners =
        {
            center + new Vector3(-extents.x, -extents.y, -extents.z),
            center + new Vector3(-extents.x, -extents.y, extents.z),
            center + new Vector3(-extents.x, extents.y, -extents.z),
            center + new Vector3(-extents.x, extents.y, extents.z),
            center + new Vector3(extents.x, -extents.y, -extents.z),
            center + new Vector3(extents.x, -extents.y, extents.z),
            center + new Vector3(extents.x, extents.y, -extents.z),
            center + new Vector3(extents.x, extents.y, extents.z),
        };

        bool hasPoint = false;
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 screenPoint = camera.WorldToScreenPoint(corners[i]);
            if (screenPoint.z <= 0f)
            {
                continue;
            }

            hasPoint = true;
            minX = Mathf.Min(minX, screenPoint.x);
            minY = Mathf.Min(minY, screenPoint.y);
            maxX = Mathf.Max(maxX, screenPoint.x);
            maxY = Mathf.Max(maxY, screenPoint.y);
        }

        if (!hasPoint)
        {
            return false;
        }

        float screenWidth = Mathf.Max(1f, Screen.width);
        float screenHeight = Mathf.Max(1f, Screen.height);
        minX = Mathf.Clamp(minX, 0f, screenWidth);
        maxX = Mathf.Clamp(maxX, 0f, screenWidth);
        minY = Mathf.Clamp(minY, 0f, screenHeight);
        maxY = Mathf.Clamp(maxY, 0f, screenHeight);

        if (maxX <= minX || maxY <= minY)
        {
            return false;
        }

        screenRect = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return true;
    }

    private bool TryGetCharacterAnchorWorldPosition(GameObject modelContainer, out Vector3 worldPosition)
    {
        worldPosition = Vector3.zero;

        if (modelContainer == null)
        {
            return false;
        }

        Transform anchorTransform = null;
        if (!string.IsNullOrWhiteSpace(characterAnchorBoneName))
        {
            string anchorName = characterAnchorBoneName.Trim();

            // Prefer Humanoid bone resolution because CharacterManager guarantees Animator setup.
            var animator = modelContainer.GetComponentInChildren<Animator>();
            if (animator != null
                && animator.isHuman
                && System.Enum.TryParse(anchorName, true, out HumanBodyBones humanoidBone)
                && humanoidBone != HumanBodyBones.LastBone)
            {
                anchorTransform = animator.GetBoneTransform(humanoidBone);
            }

            if (anchorTransform == null)
            {
                anchorTransform = FindChildRecursive(modelContainer.transform, anchorName);
            }
        }

        if (anchorTransform == null)
        {
            // Fallback: root transform of the character model under ModelContainer.
            anchorTransform = modelContainer.transform.childCount > 0
                ? modelContainer.transform.GetChild(0)
                : modelContainer.transform;
        }

        worldPosition = anchorTransform.position + characterAnchorWorldOffset;
        return true;
    }

    private static Transform FindChildRecursive(Transform parent, string targetName)
    {
        if (parent == null || string.IsNullOrWhiteSpace(targetName))
        {
            return null;
        }

        if (string.Equals(parent.name, targetName, System.StringComparison.Ordinal))
        {
            return parent;
        }

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            Transform found = FindChildRecursive(child, targetName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
