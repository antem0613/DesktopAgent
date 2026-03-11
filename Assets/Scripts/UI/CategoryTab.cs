using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class CategoryTab : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] GameObject SelectBar, PanelShadow, ContentPanel;
    public Button button;
    [HideInInspector] public bool isSelected = false;

    public void Initialize()
    {
        button.onClick.AddListener(OnClicked);
        SelectBar.SetActive(false);
        PanelShadow.SetActive(false);
        ContentPanel.SetActive(false);
    }

    void Update()
    {
        if(!isSelected && SelectBar.activeSelf)
        {
            SelectBar.SetActive(false);
            PanelShadow.SetActive(false);
            ContentPanel.SetActive(false);
        }
    }

    public void OnClicked()
    {
        isSelected = true;
        SelectBar.SetActive(true);
        PanelShadow.SetActive(true);
        ContentPanel.SetActive(true);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (isSelected) return;
        PanelShadow.SetActive(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (isSelected) return;
        PanelShadow.SetActive(false);
    }
}
