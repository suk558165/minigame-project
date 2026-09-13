using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// 보스·미니보스 공통 필드를 BossBase 로 옮긴 뒤에도 프리팹에 저장된 값이 그대로 읽히는지 확인한다.
public class BossPrefabTests
{
    static readonly string[] BossPrefabPaths =
    {
        "Assets/Prefabs/Enemy/Boss_DeathAngel.prefab",
        "Assets/Prefabs/Enemy/MiniBoss_SkeletonKing.prefab",
    };

    [TestCaseSource(nameof(BossPrefabPaths))]
    public void SharedBossFields_AreStillSerialized(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.IsNotNull(prefab, path);

        var boss = prefab.GetComponent<BossBase>();
        Assert.IsNotNull(boss, path + " 에 BossBase 파생 컴포넌트가 없다");

        var so = new SerializedObject(boss);
        Assert.Greater(so.FindProperty("maxHp").floatValue, 0f, "maxHp");
        Assert.Greater(so.FindProperty("moveSpeed").floatValue, 0f, "moveSpeed");
        Assert.Greater(so.FindProperty("damage").floatValue, 0f, "damage");
        Assert.Greater(so.FindProperty("patternCooldown").floatValue, 0f, "patternCooldown");
        Assert.Greater(so.FindProperty("tellDuration").floatValue, 0f, "tellDuration");
        Assert.Greater(so.FindProperty("detectionRange").floatValue, 0f, "detectionRange");
        Assert.GreaterOrEqual(so.FindProperty("goldDropMax").intValue, so.FindProperty("goldDropMin").intValue, "goldDrop 범위");
        Assert.AreNotEqual(0, so.FindProperty("groundLayer").intValue, "groundLayer");
    }
}
