using UnityEngine.Localization.Settings;

/// <summary>
/// 코드에서 동적으로 로컬라이즈된 문자열을 얻기 위한 짧은 헬퍼.
/// "Localization"의 줄임 — 이름이 짧아 사용처에서 자주 부르기 좋다.
/// </summary>
public static class L10n
{
    const string DefaultTable = "Items";

    /// <summary>키로 번역된 문자열을 가져온다. 실패 시 fallback 반환.</summary>
    public static string Get(string key, string fallback = "")
    {
        // 초기화 전에 동기 GetTable을 호출하면 내부 WaitForCompletion이
        // ResourceManager.Update를 재진입시켜 예외가 난다.
        if (!LocalizationSettings.InitializationOperation.IsDone)
            return fallback;

        var table = LocalizationSettings.StringDatabase?.GetTable(DefaultTable);
        if (table == null)
            return fallback;
        var entry = table.GetEntry(key);
        return entry != null ? entry.GetLocalizedString() : fallback;
    }

    /// <summary>키로 가져온 형식 문자열에 인자 채워서 반환. 예: "Gold: {0}" + 100 → "Gold: 100"</summary>
    public static string Format(string key, string fallback, params object[] args)
    {
        string fmt = Get(key, fallback);
        return args == null || args.Length == 0 ? fmt : string.Format(fmt, args);
    }
}
