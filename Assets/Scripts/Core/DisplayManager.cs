using System.Collections.Generic;
using UnityEngine;

public class DisplayManager : MonoBehaviour
{
    public static DisplayManager Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    /// <summary>UI에 표시할 해상도 목록 (낮은 → 높은 순). 시스템에서 지원하는 것만 필터링.</summary>
    private static readonly Vector2Int[] CandidateResolutions =
    {
        new Vector2Int(1280, 720),
        new Vector2Int(1366, 768),
        new Vector2Int(1600, 900),
        new Vector2Int(1920, 1080),
        new Vector2Int(2560, 1440),
        new Vector2Int(3840, 2160),
    };

    public List<Vector2Int> AvailableResolutions { get; private set; } = new List<Vector2Int>();

    public Vector2Int CurrentResolution => new Vector2Int(Screen.width, Screen.height);
    public FullScreenMode CurrentFullscreenMode => Screen.fullScreenMode;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null)
            return;
        var go = new GameObject("[DisplayManager]");
        DontDestroyOnLoad(go);
        go.AddComponent<DisplayManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        BuildResolutionList();
        ApplySavedSettings();
    }

    void BuildResolutionList()
    {
        AvailableResolutions.Clear();

        // 에디터의 게임뷰 크기(Screen.width/Height)가 아니라 실제 모니터 해상도를 기준으로 한다.
        int maxW = Display.main != null ? Display.main.systemWidth : 1920;
        int maxH = Display.main != null ? Display.main.systemHeight : 1080;
        // 에디터에서 Display.main이 비정상적으로 작게 잡히는 경우 대비 — 빌드된 게임에서도 최소 후보는 보장
        if (maxW < 1280 || maxH < 720)
        {
            maxW = Mathf.Max(maxW, 1920);
            maxH = Mathf.Max(maxH, 1080);
        }

        foreach (var r in CandidateResolutions)
        {
            if (r.x <= maxW && r.y <= maxH)
                AvailableResolutions.Add(r);
        }

        if (AvailableResolutions.Count == 0)
            AvailableResolutions.Add(new Vector2Int(1920, 1080));
    }

    void ApplySavedSettings()
    {
        if (SaveManager.Instance == null)
            return;

        var d = SaveManager.Instance.Data;
        if (d.resolutionWidth <= 0 || d.resolutionHeight <= 0)
            return;

        FullScreenMode mode =
            d.fullscreenMode >= 0 ? (FullScreenMode)d.fullscreenMode : Screen.fullScreenMode;

        Screen.SetResolution(d.resolutionWidth, d.resolutionHeight, mode);
    }

    public void SetResolution(int width, int height)
    {
        Screen.SetResolution(width, height, Screen.fullScreenMode);
        if (SaveManager.Instance != null)
        {
            SaveManager.Instance.Data.resolutionWidth = width;
            SaveManager.Instance.Data.resolutionHeight = height;
            SaveManager.Instance.Save();
        }
    }

    public void SetFullscreenMode(FullScreenMode mode)
    {
        Screen.SetResolution(Screen.width, Screen.height, mode);
        if (SaveManager.Instance != null)
        {
            SaveManager.Instance.Data.fullscreenMode = (int)mode;
            SaveManager.Instance.Save();
        }
    }

    public int GetCurrentResolutionIndex()
    {
        var cur = CurrentResolution;
        for (int i = 0; i < AvailableResolutions.Count; i++)
        {
            if (AvailableResolutions[i] == cur)
                return i;
        }
        return 0;
    }
}
