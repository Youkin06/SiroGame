using UnityEngine;

/// <summary>
/// 穴の底に置く Trigger。落ちた敵を Dead 状態にする。
/// </summary>
public class HoleDeathZone : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        EnemyController enemy = other.GetComponentInParent<EnemyController>();

        if (enemy != null)
        {
            enemy.Die();
        }
    }
}
