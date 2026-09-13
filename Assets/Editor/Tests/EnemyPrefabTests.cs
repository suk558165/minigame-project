using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// 적 스크립트를 분리·private 로 바꾼 뒤에도 프리팹 연결과 인스펙터 값이 유지되는지 확인한다.
public class EnemyPrefabTests
{
    static string[] EnemyPrefabPaths() =>
        AssetDatabase
            .FindAssets("t:Prefab", new[] { "Assets/Prefabs/Enemy" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .ToArray();

    [TestCaseSource(nameof(EnemyPrefabPaths))]
    public void Prefab_HasNoMissingScripts(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        int missing = prefab
            .GetComponentsInChildren<Transform>(true)
            .Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
        Assert.AreEqual(0, missing, path + " 에 누락된 스크립트가 있다");
    }

    [TestCaseSource(nameof(EnemyPrefabPaths))]
    public void EnemyController_StatsAreSerialized(string path)
    {
        var enemy = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<EnemyController>();
        if (enemy == null)
            Assert.Ignore("EnemyController 가 없는 프리팹 (보스/미니보스)");

        var so = new SerializedObject(enemy);
        Assert.Greater(so.FindProperty("maxHp").floatValue, 0f, "maxHp");
        Assert.Greater(so.FindProperty("moveSpeed").floatValue, 0f, "moveSpeed");
        Assert.Greater(so.FindProperty("damage").floatValue, 0f, "damage");
        Assert.GreaterOrEqual(so.FindProperty("goldDropMax").intValue, so.FindProperty("goldDropMin").intValue, "goldDrop 범위");
    }
}
