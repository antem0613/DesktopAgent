using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class DraggableUI : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler
{

    private Vector2 prevPos; //保存しておく初期position
    private RectTransform rectTransform; // 移動したいオブジェクトのRectTransform
    private RectTransform parentRectTransform; // 移動したいオブジェクトの親(Panel)のRectTransform

    [Header("Drag Range")]
    [Tooltip("ドラッグ開始を許可する範囲（タイトルバー等）。未指定ならこのオブジェクト全体")]
    [SerializeField] private RectTransform dragArea;

    private bool isDragAllowed;
    private Vector2 dragOffset;


    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        parentRectTransform = rectTransform.parent as RectTransform;
    }


    // ドラッグ開始時の処理
    public void OnBeginDrag(PointerEventData eventData)
    {
        isDragAllowed = IsInDragArea(eventData);
        if (!isDragAllowed)
        {
            return;
        }

        if (eventData != null)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRectTransform,
                eventData.position,
                eventData.pressEventCamera,
                out var localPosition);
            dragOffset = rectTransform.anchoredPosition - localPosition;
        }

        // ドラッグ前の位置を記憶しておく
        // RectTransformの場合はpositionではなくanchoredPositionを使う
        prevPos = rectTransform.anchoredPosition;

    }

    // ドラッグ中の処理
    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragAllowed)
        {
            return;
        }

        // eventData.positionから、親に従うlocalPositionへの変換を行う
        // オブジェクトの位置をlocalPositionに変更する

        Vector2 localPosition = GetLocalPosition(eventData);
        rectTransform.anchoredPosition = localPosition + dragOffset;
    }

    // ドラッグ終了時の処理
    public void OnEndDrag(PointerEventData eventData)
    {
        isDragAllowed = false;

    }

    // ScreenPositionからlocalPositionへの変換関数
    private Vector2 GetLocalPosition(PointerEventData eventData)
    {
        Vector2 result = Vector2.zero;

        if (eventData == null)
        {
            return result;
        }

        // screenPositionを親の座標系(parentRectTransform)に対応するよう変換する.
        // Screen Space - Camera / World Space Canvas では eventData.pressEventCamera が必要。
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRectTransform, eventData.position,
            eventData.pressEventCamera, out result);

        return result;
    }

    private bool IsInDragArea(PointerEventData eventData)
    {
        if (eventData == null)
        {
            return false;
        }

        var area = dragArea != null ? dragArea : rectTransform;
        return RectTransformUtility.RectangleContainsScreenPoint(area, eventData.position, eventData.pressEventCamera);
    }

}

