using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class GameManager : MonoBehaviour
{
    void Update()
    {
#if UNITY_EDITOR
        // ── 디버그 단축키 (에디터 전용) ──────────────────────
        // F1 : 현재 방 즉시 클리어 → 다음 방으로 이동
        if (Input.GetKeyDown(KeyCode.F1))
        {
            var rm = RoomManager.Instance;
            if (rm != null && rm.CurrentRoomNumber > 0)
            {
                foreach (var e in EnemyController.Instances.ToArray())
                    e.TakeDamage(99999f);
                foreach (var m in MiniBossController.Instances.ToArray())
                    m.TakeDamage(99999f);
                foreach (var b in BossController.Instances.ToArray())
                    b.TakeDamage(99999f);
            }
        }
#endif
    }
}
