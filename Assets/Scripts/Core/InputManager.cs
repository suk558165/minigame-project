using UnityEngine;

public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    public KeyCode MoveLeft { get; private set; } = KeyCode.LeftArrow;
    public KeyCode MoveRight { get; private set; } = KeyCode.RightArrow;
    public KeyCode MoveDown { get; private set; } = KeyCode.DownArrow;
    public KeyCode Jump { get; private set; } = KeyCode.Space;
    public KeyCode Dash { get; private set; } = KeyCode.Z;
    public KeyCode Attack { get; private set; } = KeyCode.X;
    public KeyCode Inventory { get; private set; } = KeyCode.Tab;
    public KeyCode Interact { get; private set; } = KeyCode.A;
    public KeyCode WeaponSwitch { get; private set; } = KeyCode.C;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        Load();
    }

    public void Load()
    {
        if (SaveManager.Instance == null)
            return;

        var kb = SaveManager.Instance.Data.keyBindings;
        MoveLeft = ParseKey(kb.moveLeftKey, KeyCode.LeftArrow);
        MoveRight = ParseKey(kb.moveRightKey, KeyCode.RightArrow);
        MoveDown = ParseKey(kb.moveDownKey, KeyCode.DownArrow);
        Jump = ParseKey(kb.jumpKey, KeyCode.Space);
        Dash = ParseKey(kb.dashKey, KeyCode.Z);
        Attack = ParseKey(kb.attackKey, KeyCode.X);
        Inventory = ParseKey(kb.inventoryKey, KeyCode.Tab);
        Interact = ParseKey(kb.interactKey, KeyCode.A);
        WeaponSwitch = ParseKey(kb.weaponSwitchKey, KeyCode.C);
    }

    public void SetKey(string action, KeyCode key)
    {
        if (SaveManager.Instance == null)
            return;

        var kb = SaveManager.Instance.Data.keyBindings;
        switch (action)
        {
            case "MoveLeft":
                MoveLeft = key;
                kb.moveLeftKey = key.ToString();
                break;
            case "MoveRight":
                MoveRight = key;
                kb.moveRightKey = key.ToString();
                break;
            case "MoveDown":
                MoveDown = key;
                kb.moveDownKey = key.ToString();
                break;
            case "Jump":
                Jump = key;
                kb.jumpKey = key.ToString();
                break;
            case "Dash":
                Dash = key;
                kb.dashKey = key.ToString();
                break;
            case "Attack":
                Attack = key;
                kb.attackKey = key.ToString();
                break;
            case "Inventory":
                Inventory = key;
                kb.inventoryKey = key.ToString();
                break;
            case "Interact":
                Interact = key;
                kb.interactKey = key.ToString();
                break;
            case "WeaponSwitch":
                WeaponSwitch = key;
                kb.weaponSwitchKey = key.ToString();
                break;
        }
        SaveManager.Instance.Save();
    }

    static KeyCode ParseKey(string name, KeyCode fallback)
    {
        try
        {
            return (KeyCode)System.Enum.Parse(typeof(KeyCode), name, true);
        }
        catch
        {
            return fallback;
        }
    }
}
