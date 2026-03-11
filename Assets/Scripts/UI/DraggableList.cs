using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(VerticalLayoutGroup))]
public class DraggableList : MonoBehaviour
{
    private GameObject placeholder;
    private Transform currentDraggableItem;
    private Transform originalParent;
    private int originalSiblingIndex;
    private Vector3 dragOffset;

    // ドラッグ開始（子から呼ばれる）
    public void OnStartDrag(Transform item, PointerEventData eventData)
    {
        currentDraggableItem = item;
        originalParent = transform;
        originalSiblingIndex = item.GetSiblingIndex();

        Vector3 mouseWorldPos;
        RectTransformUtility.ScreenPointToWorldPointInRectangle(
            item as RectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out mouseWorldPos
        );

        // 「アイテムの現在位置」と「マウス位置」の差分を保存
        dragOffset = new Vector3(item.position.x, item.position.y - mouseWorldPos.y, item.position.z - mouseWorldPos.z);

        // 1. プレースホルダー（隙間）を作成
        placeholder = new GameObject("Placeholder");
        placeholder.transform.SetParent(this.transform);
        placeholder.transform.SetSiblingIndex(originalSiblingIndex);

        // プレースホルダーのサイズをアイテムに合わせる
        LayoutElement le = placeholder.AddComponent<LayoutElement>();
        RectTransform itemRect = item.GetComponent<RectTransform>();
        le.preferredWidth = 0; // 横幅は詰めない（必要なら設定）
        le.preferredHeight = itemRect.rect.height; // 高さを合わせる
        le.flexibleWidth = 0;
        le.flexibleHeight = 0;

        // 2. ドラッグするアイテムを親から一時的に外す
        // (LayoutGroupの影響を受けないように、親の親などに移動)
        item.SetParent(this.transform.parent);

        // Raycastをブロックしないようにする（マウス下の判定のため）
        var group = item.GetComponent<CanvasGroup>();
        if (group == null) group = item.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
    }

    // ドラッグ中（子から呼ばれる）
    public void OnDrag(Transform item, PointerEventData eventData)
    {
        Vector3 mouseWorldPos;
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
            item as RectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out mouseWorldPos))
        {
            // マウス位置 + 最初に計算したズレ = 自然な位置
            item.position = new Vector3(dragOffset.x, mouseWorldPos.y + dragOffset.y, mouseWorldPos.z + dragOffset.z);
        }

        // プレースホルダーの移動判定
        int newIndex = placeholder.transform.GetSiblingIndex();

        // 子要素全走査して、Y座標で挿入位置を決める
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child == placeholder.transform) continue; // 自分（プレースホルダー）は無視

            // マウス位置（アイテム位置）が、その子の中心より上か下かで判定
            if (item.position.y > child.position.y)
            {
                newIndex = i;
                if (placeholder.transform.GetSiblingIndex() < newIndex)
                    newIndex--; // インデックス調整
                break;
            }
            // 一番下まで行ったら最後尾にする
            newIndex = transform.childCount;
        }

        placeholder.transform.SetSiblingIndex(newIndex);
    }

    // ドラッグ終了（子から呼ばれる）
    public void OnEndDrag(Transform item)
    {
        // 1. Raycastを戻す
        var group = item.GetComponent<CanvasGroup>();
        if (group != null) group.blocksRaycasts = true;

        // 2. アイテムを元の親（ListManager）に戻す
        item.SetParent(originalParent);

        // 3. プレースホルダーの位置にアイテムを入れる
        item.SetSiblingIndex(placeholder.transform.GetSiblingIndex());

        // 4. プレースホルダー削除
        Destroy(placeholder);

        currentDraggableItem = null;
    }
}