using UnityEngine;

public class ChestSpawnPoint : MonoBehaviour
{
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.8f, 0f);
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.6f);
        Gizmos.DrawIcon(transform.position, "console.infoicon.inactive.sml", true);
    }
}
