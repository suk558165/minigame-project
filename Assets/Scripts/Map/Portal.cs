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

    private bool _active;
    private bool _triggered;
    private bool _playerInRange;

    void Awake()
    {
        SetActive(portalType == PortalType.DungeonEntrance);
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
