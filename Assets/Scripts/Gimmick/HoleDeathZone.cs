using UnityEngine;

/// <summary>
/// 穴の底に置く Trigger。床がない穴でも死亡できるよう、底への到達を通知する。
/// </summary>
public class HoleDeathZone : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        NotifyEnemy(other);
    }

    private void OnTriggerStay(Collider other)
    {
        // 高速落下や生成直後の重なりでも、底へ到達したことを確実に記録する。
        NotifyEnemy(other);
    }

    private static void NotifyEnemy(Collider other)
    {
        EnemyController enemy = other.GetComponentInParent<EnemyController>();

        if (enemy != null)
        {
            enemy.NotifyEnteredDeathZone();
        }
    }
}
