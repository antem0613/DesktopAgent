using UnityEngine;
using UnityEngine.EventSystems;

public class DraggableListItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] Transform parent;
    private DraggableList manager;

    void Start()
    {
        // 自動的に親（ListManager）を探して取得する
        manager = GetComponentInParent<DraggableList>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (manager != null) manager.OnStartDrag(parent, eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (manager != null) manager.OnDrag(parent, eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (manager != null) manager.OnEndDrag(parent);
    }
}