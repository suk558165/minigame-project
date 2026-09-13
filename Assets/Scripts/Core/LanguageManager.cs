using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Localization.Settings;

public class LanguageManager : MonoBehaviour
{
    public static LanguageManager Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    public int CurrentIndex { get; private set; }

    static readonly string[] localeCodes = { "en", "ko-KR" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null)
            return;
        var go = new GameObject("LanguageManager");
        go.AddComponent<LanguageManager>();
    }

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

    async UniTaskVoid Start()
    {
        // 중복 인스턴스는 Awake 에서 Destroy 되지만 Start 는 그대로 실행된다.
        // 토큰 없이 기다리면 파괴된 뒤에 ApplyLocale 이 돌아 예외가 난다.
        var token = this.GetCancellationTokenOnDestroy();
        await UniTask.WaitUntil(
            () => LocalizationSettings.InitializationOperation.IsDone,
            cancellationToken: token
        );

        string saved = SaveManager.Instance != null
            ? SaveManager.Instance.Data.languageCode
            : "";

        if (!string.IsNullOrEmpty(saved))
            ApplyLocale(saved);

        CurrentIndex = GetCurrentLocaleIndex();
    }

    public void SetLanguage(int index)
    {
        if (index < 0 || index >= localeCodes.Length)
            return;

        string code = localeCodes[index];
        ApplyLocale(code);
        CurrentIndex = index;

        if (SaveManager.Instance != null)
        {
            SaveManager.Instance.Data.languageCode = code;
            SaveManager.Instance.Save();
        }
    }

    void ApplyLocale(string code)
    {
        var locales = LocalizationSettings.AvailableLocales.Locales;
        for (int i = 0; i < locales.Count; i++)
        {
            if (locales[i].Identifier.Code == code)
            {
                LocalizationSettings.SelectedLocale = locales[i];
                return;
            }
        }
    }

    int GetCurrentLocaleIndex()
    {
        var current = LocalizationSettings.SelectedLocale;
        if (current == null)
            return 0;

        for (int i = 0; i < localeCodes.Length; i++)
        {
            if (current.Identifier.Code == localeCodes[i])
                return i;
        }
        return 0;
    }
}
