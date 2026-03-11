using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// クリックしたウィンドウを最前面にし、選択状態を管理して見た目(色)を変更します。
/// - アタッチ先: ウィンドウPanelのルート推奨
/// </summary>
[DisallowMultipleComponent]
public sealed class SelectableWindow : MonoBehaviour, IPointerDownHandler
{
    [Header("Behavior")]
    [SerializeField] private bool bringToFrontOnClick = true;

    [Header("Selection Visual")]
    [Tooltip("色を変更したいImage。未指定なら自身/子から最初のImageを探します")]
    [SerializeField] private Image targetImage;

    [SerializeField] private Color selectedColor = new Color(0.2f, 0.6f, 1f, 1f);

    private Color _normalColor;
    private bool _hasNormalColor;

    private static SelectableWindow _current;

    private void Awake()
    {
        EnsureTargetImage();
        CacheNormalColor();
        ApplyVisual(isSelected: _current == this);
    }

    private void OnEnable()
    {
        EnsureTargetImage();
        CacheNormalColor();
        ApplyVisual(isSelected: _current == this);
    }

    private void OnDisable()
    {
        if (_current == this)
        {
            _current = null;
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        Focus();
    }

    public void Focus()
    {
        if (bringToFrontOnClick)
        {
            transform.SetAsLastSibling();
        }

        SelectThis();
    }

    private void SelectThis()
    {
        if (_current == this)
        {
            ApplyVisual(isSelected: true);
            return;
        }

        var prev = _current;
        _current = this;

        if (prev != null)
        {
            prev.ApplyVisual(isSelected: false);
        }

        ApplyVisual(isSelected: true);
    }

    private void ApplyVisual(bool isSelected)
    {
        if (targetImage == null)
        {
            return;
        }

        if (!_hasNormalColor)
        {
            _normalColor = targetImage.color;
            _hasNormalColor = true;
        }

        targetImage.color = isSelected ? selectedColor : _normalColor;
    }

    private void EnsureTargetImage()
    {
        if (targetImage != null)
        {
            return;
        }

        targetImage = GetComponent<Image>();
        if (targetImage != null)
        {
            return;
        }

        targetImage = GetComponentInChildren<Image>(true);
    }

    private void CacheNormalColor()
    {
        if (targetImage == null || _hasNormalColor)
        {
            return;
        }

        _normalColor = targetImage.color;
        _hasNormalColor = true;
    }
}