using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 던그리드·스컬 계열 2D 횡스크롤 카메라.
///
/// 세 가지를 지킨다:
///  1. 점프해도 화면이 출렁이지 않는다 — 세로는 "발이 닿은 높이"를 따라간다.
///  2. 미세한 움직임에는 반응하지 않는다 — 데드존 밖으로 나가야 따라간다.
///  3. 방 경계 밖은 절대 보이지 않는다 — 화면 절반을 뺀 영역으로 클램프한다.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("따라가는 속도")]
    [Tooltip("가로 추적 부드러움. 작을수록 빠릿")]
    [SerializeField]
    private float horizontalSmoothTime = 0.18f;

    [Tooltip("세로 추적 부드러움. 가로보다 느려야 점프 시 안정적으로 보인다")]
    [SerializeField]
    private float verticalSmoothTime = 0.32f;

    [Header("데드존 (이 범위 안에서는 카메라가 안 움직임)")]
    [Tooltip("가로 데드존 폭(월드 유닛). 0이면 항상 따라감")]
    [SerializeField]
    private float deadzoneWidth = 2.4f;

    [Tooltip("세로 데드존 높이(월드 유닛). 점프 높이보다 살짝 크게 잡으면 출렁임이 사라진다")]
    [SerializeField]
    private float deadzoneHeight = 3.2f;

    [Header("세로 추적 방식")]
    [Tooltip("켜면 접지했을 때만 세로 목표를 갱신한다 (스컬·던그리드 방식)")]
    [SerializeField]
    private bool followGroundedYOnly = true;

    [Header("Offset")]
    [Tooltip("플레이어 기준 카메라 가로 오프셋. 보통 0")]
    [SerializeField]
    private float horizontalOffset = 0f;

    [Tooltip("플레이어보다 위를 보는 정도. 양수면 플레이어가 화면 아래쪽에 위치")]
    [SerializeField]
    private float verticalOffset = 1.2f;

    [Header("Lookahead")]
    [Tooltip("바라보는 방향으로 미리 보는 거리")]
    [SerializeField]
    private float lookAheadDistance = 2.2f;

    [Tooltip("룩어헤드 전환 속도")]
    [SerializeField]
    private float lookAheadSpeed = 2.5f;

    [Header("Camera")]
    [SerializeField]
    private float orthographicSize = 6f;
    [SerializeField]
    private float cameraZ = -10f;

    [Header("Bounds")]
    [Tooltip("방 경계 밖이 보이지 않도록 클램프")]
    [SerializeField]
    private bool useBounds = false;

    [Tooltip("CameraBounds 폴리곤이 없는 방에서 타일맵으로 경계를 자동 추정")]
    [SerializeField]
    private bool autoDetectBoundsFromTilemap = true;

    [Tooltip("경계를 안쪽으로 더 좁힘. 타일 가장자리 틈이 보일 때 올린다")]
    [SerializeField]
    private float boundsPadding = 0f;

    [SerializeField]
    private float minX = -100f;
    [SerializeField]
    private float maxX = 100f;
    [SerializeField]
    private float minY = -100f;
    [SerializeField]
    private float maxY = 100f;

    [Header("Shake")]
    [Tooltip("피격 등에서 흔들리는 기본 세기")]
    [SerializeField]
    private float shakeMagnitude = 0.25f;

    [Tooltip("기본 지속 시간(초)")]
    [SerializeField]
    private float shakeDuration = 0.18f;

    private float _velX,
        _velY;
    private float _currentLookAhead = 0f;
    private float _lastFacingDir = 1f;
    private Camera _cam;
    private PlayerMovement _playerMovement;

    // 데드존이 밀고 다니는 논리적 초점. 카메라는 이 지점을 부드럽게 쫓아간다.
    private Vector2 _focus;
    private bool _focusInitialized;

    private float _shakeTimeLeft;
    private float _shakeTotal;
    private float _shakeMag;
    private Vector2 _shakeOffset;

    public static CameraFollow Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    void Awake()
    {
        Instance = this;
        _cam = GetComponent<Camera>();
        ApplyCameraSettings();

        var pos = transform.position;
        pos.z = cameraZ;
        transform.position = pos;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Start()
    {
        TryAcquireTarget();
        RefreshBounds();

        if (target != null)
            SnapToTarget();
    }

    void LateUpdate()
    {
        if (!TryAcquireTarget())
            return;

        UpdateLookAhead();
        UpdateFocus();
        UpdateShake();

        Vector2 clamped = ClampToBounds(_focus);
        float x = Mathf.SmoothDamp(
            transform.position.x,
            clamped.x,
            ref _velX,
            horizontalSmoothTime
        );
        float y = Mathf.SmoothDamp(transform.position.y, clamped.y, ref _velY, verticalSmoothTime);

        transform.position = new Vector3(x + _shakeOffset.x, y + _shakeOffset.y, cameraZ);
    }

    // ── 초점 갱신 ──────────────────────────────────────────

    void UpdateLookAhead()
    {
        float facingDir = GetFacingDirection();
        if (facingDir != 0f)
            _lastFacingDir = facingDir;

        _currentLookAhead = Mathf.Lerp(
            _currentLookAhead,
            _lastFacingDir * lookAheadDistance,
            Time.deltaTime * lookAheadSpeed
        );
    }

    /// <summary>데드존 밖으로 나간 만큼만 초점을 끌고 간다.</summary>
    void UpdateFocus()
    {
        float desiredX = target.position.x + horizontalOffset + _currentLookAhead;
        float desiredY = target.position.y + verticalOffset;

        if (!_focusInitialized)
        {
            _focus = new Vector2(desiredX, desiredY);
            _focusInitialized = true;
            return;
        }

        _focus.x = PushIntoDeadzone(_focus.x, desiredX, deadzoneWidth);

        // 접지 상태에서는 발밑 높이를 그대로 목표로 삼는다.
        // 공중에서는 데드존을 벗어날 때만 따라가므로 점프해도 화면이 출렁이지 않고,
        // 다른 층에 착지하면 그 층 높이로 부드럽게 정착한다.
        bool grounded = !followGroundedYOnly || (_playerMovement?.IsGrounded ?? true);
        if (grounded)
            _focus.y = desiredY;
        else
            _focus.y = PushIntoDeadzone(_focus.y, desiredY, deadzoneHeight);
    }

    static float PushIntoDeadzone(float current, float desired, float deadzoneSize)
    {
        if (deadzoneSize <= 0f)
            return desired;

        float half = deadzoneSize * 0.5f;
        float delta = desired - current;
        if (delta > half)
            return desired - half;
        if (delta < -half)
            return desired + half;
        return current;
    }

    // ── 셰이크 ────────────────────────────────────────────

    /// <summary>기본 세기로 화면을 흔든다. 피격·보스 착지 등에서 호출.</summary>
    public void Shake() => Shake(shakeDuration, shakeMagnitude);

    public void Shake(float duration, float magnitude)
    {
        // 이미 더 센 흔들림이 진행 중이면 덮어쓰지 않는다.
        if (_shakeTimeLeft > 0f && magnitude < _shakeMag)
            return;
        _shakeTotal = Mathf.Max(0.0001f, duration);
        _shakeTimeLeft = _shakeTotal;
        _shakeMag = magnitude;
    }

    void UpdateShake()
    {
        if (_shakeTimeLeft <= 0f)
        {
            _shakeOffset = Vector2.zero;
            return;
        }

        // 일시정지(timeScale=0) 중에도 잔여 흔들림이 멈추지 않도록 unscaled 사용
        _shakeTimeLeft -= Time.unscaledDeltaTime;
        float falloff = Mathf.Clamp01(_shakeTimeLeft / _shakeTotal);
        _shakeOffset = Random.insideUnitCircle * (_shakeMag * falloff);
    }

    // ── 위치 제어 ─────────────────────────────────────────

    public void SnapToTarget()
    {
        if (target == null)
            return;

        _velX = 0f;
        _velY = 0f;
        _currentLookAhead = _lastFacingDir * lookAheadDistance;
        _focusInitialized = false;
        _shakeTimeLeft = 0f;
        _shakeOffset = Vector2.zero;

        UpdateFocus();
        Vector2 clamped = ClampToBounds(_focus);
        transform.position = new Vector3(clamped.x, clamped.y, cameraZ);
    }

    /// <summary>월드 좌표로 카메라를 강제 이동. 방 전환 시 사용.</summary>
    public void ForcePosition(Vector3 worldPos)
    {
        _velX = 0f;
        _velY = 0f;
        _focus = worldPos;
        _focusInitialized = true;

        // 경계를 무시하고 던지면 전환 직후 한 프레임 맵 밖이 노출된다.
        Vector2 clamped = ClampToBounds(_focus);
        transform.position = new Vector3(clamped.x, clamped.y, cameraZ);
    }

    /// <summary>추적 대상 변경. 보스 인트로 줌인 등에서 사용.</summary>
    public void SetFollowTarget(Transform t)
    {
        target = t;
        _playerMovement = t != null ? t.GetComponent<PlayerMovement>() : null;
        _focusInitialized = false;
    }

    // ── 경계 ──────────────────────────────────────────────

    public void SetBoundsFromPolygon(PolygonCollider2D boundsCollider)
    {
        if (boundsCollider == null)
        {
            useBounds = false;
            return;
        }

        // Collider2D.bounds 는 물리 갱신 전이면 부정확할 수 있다.
        // 프리팹을 Instantiate 한 직후에도 정확하도록 실제 점을 직접 변환한다.
        bool first = true;
        float nx = 0f,
            xx = 0f,
            ny = 0f,
            xy = 0f;

        for (int p = 0; p < boundsCollider.pathCount; p++)
        {
            foreach (var pt in boundsCollider.GetPath(p))
            {
                Vector2 w = boundsCollider.transform.TransformPoint(pt + boundsCollider.offset);
                if (first)
                {
                    nx = xx = w.x;
                    ny = xy = w.y;
                    first = false;
                }
                else
                {
                    nx = Mathf.Min(nx, w.x);
                    xx = Mathf.Max(xx, w.x);
                    ny = Mathf.Min(ny, w.y);
                    xy = Mathf.Max(xy, w.y);
                }
            }
        }

        if (first)
        {
            // 점이 하나도 없는 폴리곤 — bounds 로 폴백
            var b = boundsCollider.bounds;
            nx = b.min.x;
            xx = b.max.x;
            ny = b.min.y;
            xy = b.max.y;
        }

        minX = nx;
        maxX = xx;
        minY = ny;
        maxY = xy;
        useBounds = true;
    }

    public void ClearBounds() => useBounds = false;

    /// <summary>
    /// 방 경계를 다시 감지한다. 이전 방의 경계가 남지 않도록 항상 초기화 후 재탐색한다.
    /// </summary>
    public void RefreshBounds(Transform root = null)
    {
        useBounds = false;

        if (!autoDetectBoundsFromTilemap)
            return;

        TryAutoDetectBoundsFromTilemap(root);

        if (!useBounds)
            Debug.LogWarning(
                "[CameraFollow] 이 방에 CameraBounds 폴리곤도 타일맵도 없습니다. "
                    + "카메라가 맵 밖까지 나갈 수 있습니다."
            );
    }

    void TryAutoDetectBoundsFromTilemap(Transform root = null)
    {
        Tilemap[] tilemaps;
        if (root != null)
        {
            tilemaps = root.GetComponentsInChildren<Tilemap>();
        }
        else
        {
            var list = new System.Collections.Generic.List<Tilemap>();
            foreach (
                var go in UnityEngine
                    .SceneManagement.SceneManager.GetActiveScene()
                    .GetRootGameObjects()
            )
                list.AddRange(go.GetComponentsInChildren<Tilemap>());
            tilemaps = list.ToArray();
        }
        if (tilemaps.Length == 0)
            return;

        bool first = true;
        Bounds combined = default;
        foreach (var tm in tilemaps)
        {
            tm.CompressBounds();
            var b = tm.localBounds;
            if (b.size == Vector3.zero)
                continue;
            var worldMin = tm.transform.TransformPoint(b.min);
            var worldMax = tm.transform.TransformPoint(b.max);
            if (first)
            {
                combined = new Bounds();
                combined.SetMinMax(worldMin, worldMax);
                first = false;
            }
            else
            {
                combined.Encapsulate(worldMin);
                combined.Encapsulate(worldMax);
            }
        }

        if (first)
            return;

        minX = combined.min.x;
        maxX = combined.max.x;
        minY = combined.min.y;
        maxY = combined.max.y;
        useBounds = true;
    }

    Vector2 ClampToBounds(Vector2 pos)
    {
        if (!useBounds)
            return pos;

        float halfH = _cam != null ? _cam.orthographicSize : orthographicSize;
        float halfW = halfH * (_cam != null ? _cam.aspect : 16f / 9f);

        float loX = minX + halfW + boundsPadding;
        float hiX = maxX - halfW - boundsPadding;
        float loY = minY + halfH + boundsPadding;
        float hiY = maxY - halfH - boundsPadding;

        // 방이 화면보다 좁으면 클램프 범위가 뒤집힌다. 그 축은 방 중앙에 고정.
        pos.x = loX >= hiX ? (minX + maxX) * 0.5f : Mathf.Clamp(pos.x, loX, hiX);
        pos.y = loY >= hiY ? (minY + maxY) * 0.5f : Mathf.Clamp(pos.y, loY, hiY);

        return pos;
    }

    // ── 줌 ────────────────────────────────────────────────

    public float OrthographicSize
    {
        get => _cam != null ? _cam.orthographicSize : orthographicSize;
        set
        {
            orthographicSize = value;
            if (_cam != null)
                _cam.orthographicSize = value;
        }
    }

    /// <summary>OrthographicSize를 from에서 to까지 duration 동안 보간.</summary>
    public async UniTask LerpOrthographicSize(float from, float to, float duration)
    {
        if (_cam == null)
        {
            OrthographicSize = to;
            return;
        }

        var token = this.GetCancellationTokenOnDestroy();
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            OrthographicSize = Mathf.Lerp(from, to, t);
            await UniTask.Yield(token);
        }
        OrthographicSize = to;
    }

    // ── 내부 ──────────────────────────────────────────────

    bool TryAcquireTarget()
    {
        if (target != null)
            return true;

        if (PlayerRef.Movement == null)
            return false;

        target = PlayerRef.Transform;
        _playerMovement = PlayerRef.Movement;
        return true;
    }

    void ApplyCameraSettings()
    {
        if (_cam == null)
            _cam = GetComponent<Camera>();
        if (_cam != null)
            _cam.orthographicSize = orthographicSize;
    }

    float GetFacingDirection()
    {
        if (_playerMovement == null)
            return 0f;

        if (_playerMovement.MoveInput != 0f)
            return Mathf.Sign(_playerMovement.MoveInput);

        if (_playerMovement.Visuals != null)
            return _playerMovement.Visuals.localScale.x >= 0f ? 1f : -1f;

        return 0f;
    }

#if UNITY_EDITOR
    void OnValidate() => ApplyCameraSettings();

    void OnDrawGizmosSelected()
    {
        // 경계
        if (useBounds)
        {
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.9f);
            var c = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, 0f);
            Gizmos.DrawWireCube(c, new Vector3(maxX - minX, maxY - minY, 0f));
        }

        // 데드존
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.8f);
        Gizmos.DrawWireCube(
            new Vector3(transform.position.x, transform.position.y, 0f),
            new Vector3(deadzoneWidth, deadzoneHeight, 0f)
        );
    }
#endif
}
