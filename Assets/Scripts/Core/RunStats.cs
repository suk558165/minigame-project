using UnityEngine;

public class RunStats : MonoBehaviour
{
    public static RunStats Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    public float PlayTime { get; private set; }
    public int Deaths { get; private set; }
    public int Kills { get; private set; }
    public int GoldEarned { get; private set; }
    public float DamageDealt { get; private set; }
    public float DamageTaken { get; private set; }
    public int ItemsGained { get; private set; }

    private bool running;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject); // 씬 전환 시에도 유지
    }

    void Update()
    {
        if (running)
            PlayTime += Time.deltaTime;
    }

    public void StartRun()
    {
        PlayTime = 0f;
        Deaths = 0;
        Kills = 0;
        GoldEarned = 0;
        DamageDealt = 0f;
        DamageTaken = 0f;
        ItemsGained = 0;
        running = true;
    }

    public void StopTimer()
    {
        running = false;
        SaveBestRun();
    }

    void SaveBestRun()
    {
        if (SaveManager.Instance == null)
            return;

        var best = SaveManager.Instance.Data.bestRun;
        bool changed = false;

        if (Kills > best.bestKills)
        {
            best.bestKills = Kills;
            changed = true;
        }
        if (GoldEarned > best.bestGoldEarned)
        {
            best.bestGoldEarned = GoldEarned;
            changed = true;
        }
        if (PlayTime > best.bestPlayTime)
        {
            best.bestPlayTime = PlayTime;
            changed = true;
        }
        if (DamageDealt > best.bestDamageDealt)
        {
            best.bestDamageDealt = DamageDealt;
            changed = true;
        }

        if (changed)
            SaveManager.Instance.Save();
    }

    public void AddKill() => Kills++;

    public void AddDeath() => Deaths++;

    public void AddGold(int amount) => GoldEarned += Mathf.Max(0, amount);

    public void AddDamageDealt(float d) => DamageDealt += Mathf.Max(0f, d);

    public void AddDamageTaken(float d) => DamageTaken += Mathf.Max(0f, d);

    public void AddItem() => ItemsGained++;
}
