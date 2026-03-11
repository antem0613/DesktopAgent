using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(TextMeshProUGUI))]
public class BoundedText : MonoBehaviour, ILayoutElement
{
    [Tooltip("最大幅")]
    [SerializeField] float maxWidth = 500f;

    [SerializeField] TextMeshProUGUI targetText;

    private void Awake()
    {
        if (targetText == null)
        {
            targetText = GetComponent<TextMeshProUGUI>();
        }
    }
    public void CalculateLayoutInputHorizontal() { }
    public void CalculateLayoutInputVertical() { }

    public float minWidth => 0f;

    public float preferredWidth
    {
        get
        {
            float raw = targetText.GetPreferredValues(float.PositiveInfinity, float.PositiveInfinity).x;
            return Mathf.Min(raw, maxWidth);
        }
    }

    public float flexibleWidth => -1f;
    public float minHeight => 0f;

    public float preferredHeight
    {
        get
        {
            var size = targetText.GetPreferredValues(maxWidth, float.PositiveInfinity);
            return size.y;
        }
    }

    public float flexibleHeight => -1f;

    public int layoutPriority => 1;
}
