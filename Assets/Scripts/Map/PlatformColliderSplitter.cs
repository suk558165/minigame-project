using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

// 방 프리팹을 불러올 때 RoomManager 가 호출한다.
public static class PlatformColliderSplitter
{
    /// <summary>
    /// 통과 발판 타일맵의 콜라이더를 발판 덩어리마다 BoxCollider2D로 나눈다.
    /// 타일맵 콜라이더는 방의 모든 발판이 콜라이더 하나라서, 아래 점프로 충돌을 끄면
    /// 아래층 발판까지 함께 꺼져 1층·2층·3층이 겹친 곳에서 중간 층을 건너뛴다.
    /// </summary>
    public static void Split(GameObject room)
    {
        foreach (var tc in room.GetComponentsInChildren<TilemapCollider2D>())
        {
            if (!tc.usedByEffector)
                continue;

            var tm = tc.GetComponent<Tilemap>();
            tm.CompressBounds();
            var cb = tm.cellBounds;
            var used = new HashSet<Vector3Int>();

            int BottomOf(int x, int y)
            {
                while (tm.HasTile(new Vector3Int(x, y - 1, 0)))
                    y--;
                return y;
            }

            // 위에서부터 훑으며 윗면·아랫면 높이가 같은 칸을 가로로 묶어 한 덩어리로 만든다.
            for (int y = cb.yMax - 1; y >= cb.yMin; y--)
            for (int x = cb.xMin; x < cb.xMax; x++)
            {
                var cell = new Vector3Int(x, y, 0);
                if (!tm.HasTile(cell) || used.Contains(cell))
                    continue;

                int bottom = BottomOf(x, y);
                int right = x;
                while (right + 1 < cb.xMax)
                {
                    var next = new Vector3Int(right + 1, y, 0);
                    if (
                        !tm.HasTile(next)
                        || used.Contains(next)
                        || tm.HasTile(next + Vector3Int.up)
                        || BottomOf(next.x, y) != bottom
                    )
                        break;
                    right++;
                }

                for (int bx = x; bx <= right; bx++)
                for (int by = bottom; by <= y; by++)
                    used.Add(new Vector3Int(bx, by, 0));

                Vector3 min = tm.CellToLocal(new Vector3Int(x, bottom, 0));
                Vector3 max = tm.CellToLocal(new Vector3Int(right + 1, y + 1, 0));
                var box = tc.gameObject.AddComponent<BoxCollider2D>();
                box.offset = (min + max) * 0.5f;
                box.size = max - min;
                box.usedByEffector = true;
            }
            tc.enabled = false;
        }
    }
}
