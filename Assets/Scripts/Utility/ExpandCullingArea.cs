using UnityEngine;

[RequireComponent(typeof(Camera))]
public class ExpandCullingArea : MonoBehaviour
{
    // カリング判定を拡大する倍率（1.0が標準。数値を上げると判定が広がる）
    // 見切れが解消されるまでこの数値を上げてください
    [SerializeField] private float cullingFieldOfViewMultiplier = 2.0f;

    private Camera _camera;
    private float _lastFov;
    private float _lastAspect;

    void Start()
    {
        _camera = GetComponent<Camera>();
        UpdateCullingMatrix();
    }

    void LateUpdate()
    {
        // FOVやアスペクト比が変わった時だけ更新（負荷対策）
        if (!Mathf.Approximately(_lastFov, _camera.fieldOfView) ||
            !Mathf.Approximately(_lastAspect, _camera.aspect))
        {
            UpdateCullingMatrix();
        }
    }

    void UpdateCullingMatrix()
    {
        _camera.ResetCullingMatrix();

        // 現在のプロジェクション行列を取得
        Matrix4x4 p = _camera.projectionMatrix;

        // 判定用のFOVを擬似的に広げる計算
        // ※ここでは単純にプロジェクション行列のスケールをいじることで視野を広く見せています
        // （m00とm11がFOVとアスペクト比に関係します）
        float scale = 1.0f / cullingFieldOfViewMultiplier;
        p.m00 *= scale;
        p.m11 *= scale;

        // 書き換えた行列をカリング行列としてセット
        _camera.cullingMatrix = p;

        _lastFov = _camera.fieldOfView;
        _lastAspect = _camera.aspect;
    }

    // エディタでの確認用：カリング行列をリセット
    void OnDisable()
    {
        if (_camera != null) _camera.ResetCullingMatrix();
    }
}