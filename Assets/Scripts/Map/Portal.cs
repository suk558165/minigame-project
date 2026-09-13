using UnityEngine;

public enum PortalType
{
    DungeonEntrance,
    NextRoom,
}

public class Portal : MonoBehaviour
{
    [SerializeField]
    private PortalType portalType = PortalType.NextRoom;

    [SerializeField]
    private GameObject visualEffect;

    [Tooltip("포탈 스프라이트 아래쪽 투명 여백(유닛). 보이는 소용돌이 밑이 바닥에 닿도록 이만큼 더 내려 붙인다")]
    [SerializeField]
    private float spriteBottomPadding = 0.44f;

    private bool _active;
    private bool _triggered;
    private bool _playerInRange;

    void Awake()
    {
        SetActive(portalType == PortalType.DungeonEntrance);
    }

    void Start()
    {
        SnapToGround();
    }

    // 맵마다 손으로 놓은 높이가 제각각이고 스프라이트 아래에 투명 여백이 있어 바닥에서 떠 보인다.
    // 방을 불러온 뒤 발밑 바닥을 찾아 붙인다. 포탈 연출은 방을 깨기 전까지 꺼져 있어
    // renderer.bounds 를 쓸 수 없으므로 스프라이트 크기로 계산한다.
    void SnapToGround()
    {
        var sr = GetComponentInChildren<SpriteRenderer>(true);
        if (sr == null || sr.sprite == null)
            return;

        Bounds local = sr.sprite.bounds;
        Vector3 center = sr.transform.TransformPoint(local.center);
        float bottom = sr.transform.TransformPoint(new Vector3(local.center.x, local.min.y, 0f)).y;

        var hit = Physics2D.Raycast(center, Vector2.down, 30f, LayerMask.GetMask("Ground", "Platform"));
        if (hit.collider == null)
            return;

        float visibleBottom = bottom + spriteBottomPadding;
        transform.position += new Vector3(0f, hit.point.y - visibleBottom, 0f);
    }

    public void SetActive(bool active)
    {
        _active = active;
        _triggered = false;
        _playerInRange = false;

        if (visualEffect != null)
            visualEffect.SetActive(active);

        var col = GetComponent<Collider2D>();
        if (col != null)
            col.enabled = active;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (_active && other.CompareTag("Player"))
            _playerInRange = true;
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
            _playerInRange = false;
    }

    void Update()
    {
        if (!_active || _triggered || !_playerInRange)
            return;

        if (!Input.GetKeyDown(KeyCode.UpArrow))
            return;

        _triggered = true;

        if (portalType == PortalType.DungeonEntrance)
            GameFlowController.Instance?.EnterDungeon();
        else
            RoomManager.Instance?.GoToNextRoom();
    }
}
