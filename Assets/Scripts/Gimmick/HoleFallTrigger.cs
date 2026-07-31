using UnityEngine;

/// <summary>
/// 穴の縁に置く Trigger。侵入した敵を Falling 状態にする。
/// </summary>
public class HoleFallTrigger : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        EnemyController enemy = other.GetComponentInParent<EnemyController>();

        if (enemy == null)
        {
            return;
        }

        enemy.FallIntoHole(transform.forward);
    }
}
