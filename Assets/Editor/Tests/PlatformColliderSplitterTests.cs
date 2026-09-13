using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;

// 통과 발판 타일맵이 발판 덩어리마다 BoxCollider2D 로 나뉘는지 확인한다.
public class PlatformColliderSplitterTests
{
    GameObject room;
    Tilemap tilemap;
    TilemapCollider2D tilemapCollider;
    Tile tile;

    [SetUp]
    public void SetUp()
    {
        room = new GameObject("TestRoom");
        var grid = new GameObject("Grid");
        grid.transform.SetParent(room.transform);
        grid.AddComponent<Grid>();

        var platforms = new GameObject("Platforms");
        platforms.transform.SetParent(grid.transform);
        tilemap = platforms.AddComponent<Tilemap>();
        tilemapCollider = platforms.AddComponent<TilemapCollider2D>();
        tilemapCollider.usedByEffector = true;

        tile = ScriptableObject.CreateInstance<Tile>();
        tile.colliderType = Tile.ColliderType.Grid;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(room);
        Object.DestroyImmediate(tile);
    }

    void FillRow(int y, int xMin, int xMax)
    {
        for (int x = xMin; x <= xMax; x++)
            tilemap.SetTile(new Vector3Int(x, y, 0), tile);
    }

    BoxCollider2D[] Boxes() => tilemapCollider.GetComponents<BoxCollider2D>();

    [Test]
    public void SameHeightRow_MergesIntoOneBox()
    {
        FillRow(0, 0, 3);

        PlatformColliderSplitter.Split(room);

        var boxes = Boxes();
        Assert.AreEqual(1, boxes.Length);
        Assert.AreEqual(new Vector2(4f, 1f), boxes[0].size);
        Assert.AreEqual(new Vector2(2f, 0.5f), boxes[0].offset);
        Assert.IsTrue(boxes[0].usedByEffector);
        Assert.IsFalse(tilemapCollider.enabled, "원래 타일맵 콜라이더는 꺼져야 한다");
    }

    [Test]
    public void StackedPlatforms_SplitIntoSeparateBoxes()
    {
        // 1층·2층이 겹친 곳에서 아래 점프 시 중간 층을 건너뛰던 문제의 원인 — 층마다 따로 나뉘어야 한다.
        FillRow(0, 0, 3);
        FillRow(3, 0, 3);

        PlatformColliderSplitter.Split(room);

        var boxes = Boxes();
        Assert.AreEqual(2, boxes.Length);
        Assert.That(boxes, Has.Some.Matches<BoxCollider2D>(b => b.offset == new Vector2(2f, 0.5f)));
        Assert.That(boxes, Has.Some.Matches<BoxCollider2D>(b => b.offset == new Vector2(2f, 3.5f)));
    }

    [Test]
    public void TwoCellThickPlatform_StaysOneBox()
    {
        FillRow(0, 0, 2);
        FillRow(1, 0, 2);

        PlatformColliderSplitter.Split(room);

        var boxes = Boxes();
        Assert.AreEqual(1, boxes.Length);
        Assert.AreEqual(new Vector2(3f, 2f), boxes[0].size);
    }

    [Test]
    public void NonEffectorTilemap_IsLeftUntouched()
    {
        tilemapCollider.usedByEffector = false;
        FillRow(0, 0, 3);

        PlatformColliderSplitter.Split(room);

        Assert.AreEqual(0, Boxes().Length);
        Assert.IsTrue(tilemapCollider.enabled);
    }
}
