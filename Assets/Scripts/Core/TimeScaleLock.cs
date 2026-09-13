using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Time.timeScale 을 단일 소유자로 관리한다.
/// 일시정지·인벤토리·상점·강화·게임오버가 각자 0/1을 대입하면
/// 서로를 모르는 채로 덮어써서 모달이 열린 상태로 게임이 진행된다.
/// 하나라도 잠금을 쥐고 있으면 0, 전부 놓으면 1.
/// </summary>
public static class TimeScaleLock
{
    static readonly HashSet<Object> holders = new HashSet<Object>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        holders.Clear();
        Time.timeScale = 1f;
    }

    public static bool IsFrozen => holders.Count > 0;

    public static void Acquire(Object owner)
    {
        if (owner == null)
            return;
        if (holders.Add(owner))
            Apply();
    }

    public static void Release(Object owner)
    {
        if (owner == null)
            return;
        if (holders.Remove(owner))
            Apply();
    }

    /// <summary>마을 귀환·타이틀 복귀처럼 상태를 통째로 재설정하는 지점에서만 사용.</summary>
    public static void ReleaseAll()
    {
        holders.Clear();
        Apply();
    }

    static void Apply()
    {
        // 파괴된 소유자가 남아 잠금이 영구히 풀리지 않는 것을 방지
        holders.RemoveWhere(o => o == null);
        Time.timeScale = holders.Count > 0 ? 0f : 1f;
    }
}
