using UnityEngine;
using UnityEngine.EventSystems;
using Kirurobo;
using UnityEngine.InputSystem;
using System;

//個々のキャラクターの動作を管理する。キャラクターオブジェクトのルートにアタッチされる
public class TestCompanionCharacter : MonoBehaviour
{
	private const string LogTag = nameof(TestCompanionCharacter);

	private enum GroundSurfaceType
	{
		None,
		Taskbar,
		Window,
		Fallback
	}

	private enum TaskbarAnimState
	{
		Stand,
		Sit,
		Lay,
		Walk
	}

	[Header("Drag Settings")]
	bool blockDragWhenPointerOverUI = false;
	UniWindowController uniWindowController;
	float dragPickRadiusPixels = 64f;
	float supportCoverageProbePixels = 1f;

	[Header("Fall Settings")]
    Camera targetCamera;
	TestWindowManager windowManager;
	float gravity = 25f;
	float maxFallSpeed = 60f;
	private float fallbackGroundY = 0f;
	[SerializeField] private float groundEpsilon = 0.01f;

	[Header("Animator Settings")]
	Animator targetAnimator;
	string standParamName = "Stand";
	string sitParamName = "Sit";
	string layParamName = "Lay";
	string walkParamName = "Walk";
	string fallingParamName = "Falling";
	string grabbedParamName = "IsGrabbed";

	[Header("Taskbar Random State Duration (seconds)")]
	Vector2 standDurationRange = new Vector2(2f, 5f);
	Vector2 sitDurationRange = new Vector2(2f, 5f);
	Vector2 layDurationRange = new Vector2(2f, 5f);

	[Header("Walk Settings")]
	Vector2 walkDistanceRange = new Vector2(0.3f, 1.5f);
	Vector2 walkSpeedRange = new Vector2(0.5f, 1.5f);

	private float _fallSpeed;
	private bool _isDragging;
	private float _dragScreenDepth;
	private Vector3 _dragWorldOffset;
	private bool _isGroundedOnWindow;
	private IntPtr _groundedWindowHandle;
	private Vector2 _groundedWindowScreenOffset;
	private bool _wasGroundedLastFrame;
	private GroundSurfaceType _lastGroundSurface = GroundSurfaceType.None;
	private IntPtr _lastGroundWindowHandle = IntPtr.Zero;
	private TaskbarAnimState _currentTaskbarState = TaskbarAnimState.Stand;
	private bool _hasTaskbarState;
	private float _taskbarNextStateChangeTime;
	private float _walkRemainingDistance;
	private float _walkDirectionSign;
	private float _walkSpeed;
	private bool _windowStateFixed;

    //Logs
    bool enableDragDebugLog = true;
	bool enablePositionTraceLog = true;
    float positionLogIntervalSeconds = 0.2f;
	float positionLogMinDistance = 0.02f;
	bool enableSupportDebugLog = true;
	private Vector3 _lastLoggedPosition;
	private bool _hasLoggedPosition;
	private float _nextPositionLogTime;

	private void Awake()
	{
		uniWindowController = UniWindowController.current;
		
		targetCamera = Camera.main;

		windowManager = TestWindowManager.Instance;

		targetAnimator = GetComponent<Animator>();

        //カメラ位置から地面のY座標を計算
		float distanceFromCamera = Vector3.Dot(transform.position - targetCamera.transform.position, targetCamera.transform.forward);
		if (distanceFromCamera <= targetCamera.nearClipPlane)
		{
			distanceFromCamera = targetCamera.nearClipPlane + 0.01f;
		}

		Vector3 worldBottom = targetCamera.ViewportToWorldPoint(new Vector3(0.5f, 0f, distanceFromCamera));
		fallbackGroundY = worldBottom.y;
	}

	private void Update()
	{
		// 1) ドラッグ中は物理挙動より入力追従を優先する。
		bool dragging = HandleMouseDrag();
		if (dragging)
		{
			ClearWindowSupportState();
			_fallSpeed = 0f;
			_wasGroundedLastFrame = false;
			_lastGroundSurface = GroundSurfaceType.None;
			_lastGroundWindowHandle = IntPtr.Zero;
			_windowStateFixed = false;
			_hasTaskbarState = false;
			SetGrabbedAnimationState(true);
			LogPositionIfNeeded("drag");
			return;
		}

		SetGrabbedAnimationState(false);

		// 2) 直前に接地していたウィンドウの移動に追従する。
		if (TryFollowGroundedWindowMotion())
		{
			// 接地中のウィンドウが動いた分だけキャラを追従させる。
			_fallSpeed = 0f;
			LogPositionIfNeeded("follow-window");
			return;
		}

		// 3) ウィンドウ/タスクバー/フォールバック地面から接地高さを決定する。
		float groundY = fallbackGroundY;
		bool hasWindowSupport = false;
		bool hasTaskbarSupport = false;
		TestWindowManager.WindowEntry supportWindow = default;
		if (TryGetWindowGroundY(out float windowGroundY, out supportWindow))
		{
			groundY = Mathf.Max(groundY, windowGroundY);
			hasWindowSupport = supportWindow.Handle != IntPtr.Zero;
		}

		// ウィンドウ支持がない場合は、必ずタスクバー支持も評価する。
		if (!hasWindowSupport && TryGetTaskbarGroundY(out float taskbarGroundY))
		{
			groundY = Mathf.Max(groundY, taskbarGroundY);
			hasTaskbarSupport = true;
			ClearWindowSupportState();
		}

		var position = transform.position;
		if (groundY > position.y + groundEpsilon)
		{
			// 地面が現在位置より上にある場合は無効値として扱い、上方向へスナップしない。
			groundY = fallbackGroundY;
		}

		if (position.y <= groundY + groundEpsilon)
		{
			HandleGroundedState(groundY, hasWindowSupport, hasTaskbarSupport, supportWindow, "grounded");
			return;
		}

		// 4) 非接地なら重力で落下し、次フレームで再評価する。
		_fallSpeed = Mathf.Min(maxFallSpeed, _fallSpeed + Mathf.Max(0f, gravity) * Time.deltaTime);
		// 重力で落下し、地面を突き抜けないように下限でクランプする。
		position.y -= _fallSpeed * Time.deltaTime;
		bool landedDuringFall = false;
		if (position.y <= groundY)
		{
			position.y = groundY;
			_fallSpeed = 0f;
			landedDuringFall = true;
		}

		transform.position = position;
		if (landedDuringFall)
		{
			// 落下フレーム内で地面に到達した場合は、その場で次行動を決定する。
			HandleGroundedState(groundY, hasWindowSupport, hasTaskbarSupport, supportWindow, "landed");
			return;
		}

		ClearWindowSupportState();
		_wasGroundedLastFrame = false;
		_lastGroundSurface = GroundSurfaceType.None;
		_lastGroundWindowHandle = IntPtr.Zero;
		_windowStateFixed = false;
		_hasTaskbarState = false;
		SetAnimatorState(stand: false, sit: false, lay: false, walk: false, falling: true);
		LogPositionIfNeeded("fall");
	}

	private void HandleGroundedState(float groundY, bool hasWindowSupport, bool hasTaskbarSupport, TestWindowManager.WindowEntry supportWindow, string logReason)
	{
		// 接地フレームで支持状態を更新し、その場で行動を決定する。
		Vector3 position = transform.position;
		position.y = groundY;
		transform.position = position;
		_fallSpeed = 0f;

		GroundSurfaceType currentSurface;
		if (hasWindowSupport)
		{
			currentSurface = GroundSurfaceType.Window;
		}
		else if (hasTaskbarSupport)
		{
			currentSurface = GroundSurfaceType.Taskbar;
		}
		else
		{
			currentSurface = GroundSurfaceType.Fallback;
		}

		bool landedThisFrame = !_wasGroundedLastFrame || _lastGroundSurface != currentSurface;
		if (hasWindowSupport && _lastGroundWindowHandle != supportWindow.Handle)
		{
			landedThisFrame = true;
		}

		if (hasWindowSupport)
		{
			CaptureWindowSupportState(supportWindow);
		}
		else
		{
			ClearWindowSupportState();
		}

		UpdateGroundedAnimationAndMovement(currentSurface, landedThisFrame, supportWindow);
		_wasGroundedLastFrame = true;
		_lastGroundSurface = currentSurface;
		if (hasWindowSupport)
		{
			_lastGroundWindowHandle = supportWindow.Handle;
		}
		else
		{
			_lastGroundWindowHandle = IntPtr.Zero;
		}

		LogPositionIfNeeded(logReason);
	}

	private void UpdateGroundedAnimationAndMovement(GroundSurfaceType currentSurface, bool landedThisFrame, TestWindowManager.WindowEntry supportWindow)
	{
		if (targetAnimator == null)
		{
			return;
		}

		if (currentSurface == GroundSurfaceType.Window)
		{
			// ウィンドウ上は着地時のみ Sit/Lay を決定し、その後は固定する。
			if (landedThisFrame || !_windowStateFixed)
			{
				bool sit = UnityEngine.Random.value < 0.5f;
				SetAnimatorState(stand: false, sit: sit, lay: !sit, walk: false, falling: false);
				_windowStateFixed = true;
			}

			_hasTaskbarState = false;
			return;
		}

		_windowStateFixed = false;
		if (currentSurface != GroundSurfaceType.Taskbar)
		{
			_hasTaskbarState = false;
			SetAnimatorState(stand: true, sit: false, lay: false, walk: false, falling: false);
			return;
		}

		if (!_hasTaskbarState || landedThisFrame)
		{
			SelectNextTaskbarState(allowCurrentState: false);
			_hasTaskbarState = true;
		}

		if (_currentTaskbarState == TaskbarAnimState.Walk)
		{
			UpdateWalkMovementOnTaskbar();
			if (_walkRemainingDistance <= 0f)
			{
				SelectNextTaskbarState(allowCurrentState: false);
			}
			return;
		}

		if (Time.time >= _taskbarNextStateChangeTime)
		{
			SelectNextTaskbarState(allowCurrentState: false);
		}
	}

	private void SelectNextTaskbarState(bool allowCurrentState)
	{
		TaskbarAnimState[] candidates =
		{
			TaskbarAnimState.Stand,
			TaskbarAnimState.Sit,
			TaskbarAnimState.Lay,
			TaskbarAnimState.Walk,
		};

		TaskbarAnimState nextState = _currentTaskbarState;
		for (int guard = 0; guard < 16; guard++)
		{
			nextState = candidates[UnityEngine.Random.Range(0, candidates.Length)];
			if (allowCurrentState || !_hasTaskbarState || nextState != _currentTaskbarState)
			{
				break;
			}
		}

		_currentTaskbarState = nextState;
		ApplyTaskbarAnimState(nextState);
	}

	private void ApplyTaskbarAnimState(TaskbarAnimState state)
	{
		switch (state)
		{
			case TaskbarAnimState.Stand:
				SetAnimatorState(stand: true, sit: false, lay: false, walk: false, falling: false);
				_taskbarNextStateChangeTime = Time.time + RandomRangeSafe(standDurationRange);
				break;
			case TaskbarAnimState.Sit:
				SetAnimatorState(stand: false, sit: true, lay: false, walk: false, falling: false);
				_taskbarNextStateChangeTime = Time.time + RandomRangeSafe(sitDurationRange);
				break;
			case TaskbarAnimState.Lay:
				SetAnimatorState(stand: false, sit: false, lay: true, walk: false, falling: false);
				_taskbarNextStateChangeTime = Time.time + RandomRangeSafe(layDurationRange);
				break;
			case TaskbarAnimState.Walk:
				SetAnimatorState(stand: false, sit: false, lay: false, walk: true, falling: false);
				_walkRemainingDistance = RandomRangeSafe(walkDistanceRange);
				_walkSpeed = RandomRangeSafe(walkSpeedRange);
				_walkDirectionSign = UnityEngine.Random.value < 0.5f ? -1f : 1f;
				break;
		}
	}

	private void UpdateWalkMovementOnTaskbar()
	{
		if (targetCamera == null || _walkRemainingDistance <= 0f)
		{
			return;
		}

		float step = Mathf.Min(_walkRemainingDistance, Mathf.Max(0f, _walkSpeed) * Time.deltaTime);
		if (step <= 0f)
		{
			_walkRemainingDistance = 0f;
			return;
		}

		Vector3 current = transform.position;
		Vector3 target = current + Vector3.right * (_walkDirectionSign * step);
		target = ClampWorldPositionToScreen(target);

		float moved = Mathf.Abs(target.x - current.x);
		transform.position = target;
		if (moved <= 0.0001f)
		{
			_walkRemainingDistance = 0f;
			return;
		}

		_walkRemainingDistance -= moved;
	}

	private Vector3 ClampWorldPositionToScreen(Vector3 worldPosition)
	{
		if (targetCamera == null)
		{
			return worldPosition;
		}

		Vector3 screen = targetCamera.WorldToScreenPoint(worldPosition);
		if (screen.z <= 0f)
		{
			return transform.position;
		}

		screen.x = Mathf.Clamp(screen.x, 0f, Screen.width);
		screen.y = Mathf.Clamp(screen.y, 0f, Screen.height);
		return targetCamera.ScreenToWorldPoint(screen);
	}

	private float RandomRangeSafe(Vector2 range)
	{
		float min = Mathf.Min(range.x, range.y);
		float max = Mathf.Max(range.x, range.y);
		if (Mathf.Approximately(min, max))
		{
			return Mathf.Max(0f, min);
		}

		return Mathf.Max(0f, UnityEngine.Random.Range(min, max));
	}

	private void SetGrabbedAnimationState(bool grabbed)
	{
		if (targetAnimator == null)
		{
			return;
		}

		if (grabbed)
		{
			SetAnimatorState(stand: false, sit: false, lay: false, walk: false, falling: false);
		}

		targetAnimator.SetBool(grabbedParamName, grabbed);
	}

	private void SetAnimatorState(bool stand, bool sit, bool lay, bool walk, bool falling)
	{
		if (targetAnimator == null)
		{
			return;
		}

		targetAnimator.SetBool(standParamName, stand);
		targetAnimator.SetBool(sitParamName, sit);
		targetAnimator.SetBool(layParamName, lay);
		targetAnimator.SetBool(walkParamName, walk);
		targetAnimator.SetBool(fallingParamName, falling);
	}

	private bool TryFollowGroundedWindowMotion()
	{
		// 接地中ウィンドウのスクリーン相対オフセットを維持して追従する。
		if (!_isGroundedOnWindow || _groundedWindowHandle == IntPtr.Zero)
		{
			return false;
		}

		if (windowManager == null || targetCamera == null)
		{
			ClearWindowSupportState();
			return false;
		}

		if (!windowManager.TryGetWindowEntry(_groundedWindowHandle, out var supportWindow))
		{
			ClearWindowSupportState();
			return false;
		}

		Vector3 currentScreen = targetCamera.WorldToScreenPoint(transform.position);
		if (currentScreen.z <= 0f)
		{
			ClearWindowSupportState();
			return false;
		}

		float supportScreenX = supportWindow.Rect.xMin + _groundedWindowScreenOffset.x;
		float supportScreenY = supportWindow.Rect.yMax + _groundedWindowScreenOffset.y;
		float probeY = supportWindow.Rect.yMax - Mathf.Max(0.01f, supportCoverageProbePixels);
		// 足元直下のプローブ点が同じ最前面ウィンドウ上にあるかで支持継続を判定。
		LogSupport($"SupportCheck currentScreen={currentScreen}, supportScreen=({supportScreenX:0.###}, {supportScreenY:0.###}), probeY={probeY:0.###}, offset={_groundedWindowScreenOffset}, supportHandle=0x{_groundedWindowHandle.ToInt64():X}, supportRect={supportWindow.Rect}");
		bool expectedContainsProbe = supportScreenX >= supportWindow.Rect.xMin
			&& supportScreenX <= supportWindow.Rect.xMax
			&& probeY >= supportWindow.Rect.yMin
			&& probeY <= supportWindow.Rect.yMax;

		if (!expectedContainsProbe)
		{
			LogSupport($"Window support lost: support point left expected window. handle={windowManager.GetWindowDebugLabel(_groundedWindowHandle)}, x={supportScreenX:0.###}, probeY={probeY:0.###}, rect={supportWindow.Rect}");
			ClearWindowSupportState();
			return false;
		}

		if (windowManager.TryGetTopWindowAtPoint(supportScreenX, probeY, out var topWindowAtProbe))
		{
			LogSupport($"SupportProbe topWindow={windowManager.GetWindowDebugLabel(topWindowAtProbe.Handle)}, topRect={topWindowAtProbe.Rect}, sample=({supportScreenX:0.###}, {probeY:0.###})");
			if (topWindowAtProbe.Handle != _groundedWindowHandle)
			{
				LogSupport($"Window support lost: landing point covered. expected={windowManager.GetWindowDebugLabel(_groundedWindowHandle)}, actual={windowManager.GetWindowDebugLabel(topWindowAtProbe.Handle)}, x={supportScreenX:0.###}, probeY={probeY:0.###}, expectedRect={supportWindow.Rect}, actualRect={topWindowAtProbe.Rect}");
				ClearWindowSupportState();
				return false;
			}
		}
		else
		{
			LogSupport($"Window support lost: no window at landing point. expected={windowManager.GetWindowDebugLabel(_groundedWindowHandle)}, x={supportScreenX:0.###}, probeY={probeY:0.###}");
			ClearWindowSupportState();
			return false;
		}

		Vector3 targetScreen = new Vector3(
			supportScreenX,
			supportScreenY,
			currentScreen.z);

		// 追従時は深度を維持してスクリーン座標→ワールド座標へ戻す。
		Vector3 targetWorld = targetCamera.ScreenToWorldPoint(targetScreen);
		transform.position = targetWorld;
		return true;
	}

	private void CaptureWindowSupportState(TestWindowManager.WindowEntry supportWindow)
	{
		// 接地点をウィンドウ左上基準のスクリーンオフセットとして保存する。
		if (supportWindow.Handle == IntPtr.Zero || targetCamera == null)
		{
			ClearWindowSupportState();
			return;
		}

		Vector3 currentScreen = targetCamera.WorldToScreenPoint(transform.position);
		_groundedWindowScreenOffset = new Vector2(
			currentScreen.x - supportWindow.Rect.xMin,
			currentScreen.y - supportWindow.Rect.yMax);
		_groundedWindowHandle = supportWindow.Handle;
		_isGroundedOnWindow = true;
		LogSupport($"SupportCaptured handle={windowManager.GetWindowDebugLabel(_groundedWindowHandle)}, currentScreen={currentScreen}, offset={_groundedWindowScreenOffset}, supportRect={supportWindow.Rect}");
	}

	private void ClearWindowSupportState()
	{
		// 支持対象が変わった/失われたときに追従情報を初期化する。
		_isGroundedOnWindow = false;
		_groundedWindowHandle = IntPtr.Zero;
		_groundedWindowScreenOffset = Vector2.zero;
	}

	private void LogPositionIfNeeded(string reason)
	{
		// 間引き(時間/移動量)条件を満たしたときだけ位置トレースを出力する。
		if (!enablePositionTraceLog)
		{
			return;
		}

		float interval = Mathf.Max(0f, positionLogIntervalSeconds);
		if (Time.unscaledTime < _nextPositionLogTime)
		{
			return;
		}

		Vector3 currentPosition = transform.position;
		if (_hasLoggedPosition)
		{
			float minDistance = Mathf.Max(0f, positionLogMinDistance);
			if ((currentPosition - _lastLoggedPosition).sqrMagnitude < minDistance * minDistance)
			{
				return;
			}
		}

		Vector3 screenPosition = Vector3.zero;
		if (targetCamera != null)
		{
			screenPosition = targetCamera.WorldToScreenPoint(currentPosition);
		}

		Debug.Log($"[{LogTag}] PositionTrace reason={reason}, world={currentPosition}, screen={screenPosition}, fallSpeed={_fallSpeed:0.###}, dragging={_isDragging}", this);
		_hasLoggedPosition = true;
		_lastLoggedPosition = currentPosition;
		_nextPositionLogTime = Time.unscaledTime + interval;
	}

	private bool HandleMouseDrag()
	{
		if (targetCamera == null)
		{
			LogDrag("Drag skipped: targetCamera is null.");
			return false;
		}

		if (uniWindowController == null)
		{
			LogDrag("Drag skipped: UniWindowController is null.");
			return false;
		}

		if (!TryGetMouseState(out Vector2 pointerPosition, out bool isPressed, out bool pressedThisFrame))
		{
			LogDrag("Drag skipped: Mouse.current is null.");
			return false;
		}

		if (_isDragging)
		{
			if (!isPressed)
			{
				_isDragging = false;
				LogDrag("Drag ended: left button released.");
				return false;
			}

			// 開始時に保持した深度とオフセットで、見た目位置を維持したまま追従する。
			Vector3 mouseWorld = targetCamera.ScreenToWorldPoint(new Vector3(pointerPosition.x, pointerPosition.y, _dragScreenDepth));
			transform.position = mouseWorld + _dragWorldOffset;
			return true;
		}

		if (!pressedThisFrame)
		{
			return false;
		}

		if (blockDragWhenPointerOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
		{
			LogDrag($"Drag start denied: pointer over UI. pointer={pointerPosition}");
			return false;
		}

		if (!IsPointerOnCharacter(new Vector3(pointerPosition.x, pointerPosition.y, 0f)))
		{
			LogDrag($"Drag start denied: pointer is not on character. pointer={pointerPosition}");
			return false;
		}

		Vector3 screenPosition = targetCamera.WorldToScreenPoint(transform.position);
		if (screenPosition.z <= 0f)
		{
			LogDrag($"Drag start denied: character is behind camera. z={screenPosition.z}");
			return false;
		}

		// ドラッグ開始時の深度を固定し、透視投影でもポインタ追従を安定させる。
		_dragScreenDepth = screenPosition.z;
		Vector3 pointerWorld = targetCamera.ScreenToWorldPoint(new Vector3(pointerPosition.x, pointerPosition.y, _dragScreenDepth));
		_dragWorldOffset = transform.position - pointerWorld;
		_isDragging = true;
		LogDrag($"Drag started: pointer={pointerPosition}, depth={_dragScreenDepth:F3}, offset={_dragWorldOffset}");
		return true;
	}

	private static bool TryGetMouseState(out Vector2 pointerPosition, out bool isPressed, out bool pressedThisFrame)
	{
		// Input System のマウス状態を1か所で取得する。
		pointerPosition = Vector2.zero;
		isPressed = false;
		pressedThisFrame = false;

		Mouse mouse = Mouse.current;
		if (mouse == null)
		{
			return false;
		}

		pointerPosition = mouse.position.ReadValue();
		isPressed = mouse.leftButton.isPressed;
		pressedThisFrame = mouse.leftButton.wasPressedThisFrame;
		return true;
	}

	private bool IsPointerOnCharacter(Vector3 screenPosition)
	{
		// まずコライダ判定、外れた場合はスクリーン半径でフォールバックする。
		Ray ray = targetCamera.ScreenPointToRay(screenPosition);
		if (Physics.Raycast(ray, out RaycastHit hit))
		{
			if (hit.transform == transform || hit.transform.IsChildOf(transform))
			{
				LogDrag($"Hit by collider: {hit.transform.name}");
				return true;
			}

			LogDrag($"Raycast hit another object: {hit.transform.name}");
		}

		Vector3 characterScreenPosition = targetCamera.WorldToScreenPoint(transform.position);
		if (characterScreenPosition.z <= 0f)
		{
			return false;
		}

		float radius = Mathf.Max(1f, dragPickRadiusPixels);
		float distanceSqr = (new Vector2(screenPosition.x, screenPosition.y) - new Vector2(characterScreenPosition.x, characterScreenPosition.y)).sqrMagnitude;
		bool inRadius = distanceSqr <= radius * radius;
		LogDrag($"Radius check: inRadius={inRadius}, distance={Mathf.Sqrt(distanceSqr):F2}, radius={radius:F2}");
		return inRadius;
	}

	private void LogDrag(string message)
	{
		// ドラッグ関連の診断ログ。
		if (!enableDragDebugLog)
		{
			return;
		}

		Debug.Log($"[{LogTag}] {message}", this);
	}

	private void LogSupport(string message)
	{
		// 接地サポート判定の診断ログ。
		if (!enableSupportDebugLog)
		{
			return;
		}

		Debug.Log($"[{LogTag}] {message}", this);
	}

	private bool TryGetTaskbarGroundY(out float groundY)
	{
		groundY = 0f;

		if (targetCamera == null)
		{
			return false;
		}

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
		if (!WindowsAPI.TryGetTaskbarRect(out var taskbarRect))
		{
			return false;
		}

		Vector3 screenPosition = targetCamera.WorldToScreenPoint(transform.position);
		if (screenPosition.z <= 0f)
		{
			return false;
		}

		float taskbarTopY = Mathf.Clamp(taskbarRect.yMax, 0f, Screen.height);

		// 現在のスクリーン深度を維持して変換し、Orthoperspectiveでも安定して接地Yを得る。
		Vector3 worldAtTaskbarHeight = targetCamera.ScreenToWorldPoint(new Vector3(screenPosition.x, taskbarTopY, screenPosition.z));
		// 同じ深度面での高さのみを接地候補に使う。
		groundY = worldAtTaskbarHeight.y;
		return true;
#else
		return false;
#endif
	}

	private bool TryGetWindowGroundY(out float groundY, out TestWindowManager.WindowEntry supportWindow)
	{
		groundY = 0f;
		supportWindow = default;

		if (windowManager == null || targetCamera == null)
		{
			return false;
		}

		if (!windowManager.TryGetLandingWorldPosition(targetCamera, transform.position, out Vector3 landingWorldPosition, out supportWindow))
		{
			return false;
		}

		// 下方向の接地のみ有効。上方向へのスナップは許可しない。
		if (landingWorldPosition.y > transform.position.y + groundEpsilon)
		{
			LogDrag($"Window ground ignored: above current position. currentY={transform.position.y:0.###}, landingY={landingWorldPosition.y:0.###}");
			return false;
		}

		groundY = landingWorldPosition.y;
		return true;
	}
}
