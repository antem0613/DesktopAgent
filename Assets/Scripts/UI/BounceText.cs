using UnityEngine;
using TMPro;
using System.Collections;

[RequireComponent(typeof(TMP_Text))]
public class BounceText: MonoBehaviour
{
    public float jumpSpeed = 8.0f;   // 跳ねる速さ
    public float jumpHeight = 15.0f;  // 跳ねる高さ
    public float delayPerChar = 0.5f; // 文字ごとの時間差
    TMP_Text textComponent;

    void Start()
    {
        // テキストコンポーネントを取得
        textComponent = GetComponent<TMP_Text>();
    }

    void Update()
    {
        textComponent.ForceMeshUpdate();
        var textInfo = textComponent.textInfo;

        for (int i = 0; i < textInfo.characterCount; i++)
        {
            if(!gameObject.activeSelf) return;

            var charInfo = textInfo.characterInfo[i];
            if (!charInfo.isVisible) continue;

            // 「Loading」の後の「...」をひとまとめ（同じグループ）にする
            // 0~6文字目(Loading)は個別、7文字目以降(...)はすべて「7」として扱う
            int groupIndex = (i < 7) ? i : 7;

            // Sinの値を0以上(Mathf.Max)にすることで、地面に潜り込まず「跳ねる」動きにする
            float wave = Mathf.Sin(Time.time * jumpSpeed - groupIndex * delayPerChar);
            float offsetY = Mathf.Max(0, wave) * jumpHeight;

            // 頂点データの書き換え
            int materialIndex = charInfo.materialReferenceIndex;
            int vertexIndex = charInfo.vertexIndex;
            Vector3[] vertices = textInfo.meshInfo[materialIndex].vertices;

            for (int j = 0; j < 4; j++)
            {
                vertices[vertexIndex + j].y += offsetY;
            }
        }

        for (int i = 0; i < textInfo.meshInfo.Length; i++)
        {
            textInfo.meshInfo[i].mesh.vertices = textInfo.meshInfo[i].vertices;
            textComponent.UpdateGeometry(textInfo.meshInfo[i].mesh, i);
        }
    }
}