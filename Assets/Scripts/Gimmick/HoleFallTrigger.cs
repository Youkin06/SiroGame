using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 穴の縁に置く Trigger。侵入した敵を Falling 状態にする。
/// </summary>
public class HoleFallTrigger : MonoBehaviour
{
    private static readonly List<HoleFallTrigger> ActiveHoles = new();

    private BoxCollider _holeCollider;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetActiveHoles()
    {
        ActiveHoles.Clear();
    }

    private void Awake()
    {
        _holeCollider = GetComponent<BoxCollider>();

        if (_holeCollider == null)
        {
            Debug.LogError(
                "HoleFallTrigger には BoxCollider が必要です。",
                this
            );
            enabled = false;
        }
    }

    private void OnEnable()
    {
        if (!ActiveHoles.Contains(this))
        {
            ActiveHoles.Add(this);
        }
    }

    private void OnDisable()
    {
        ActiveHoles.Remove(this);
    }

    private void OnTriggerEnter(Collider other)
    {
        TryStartFall(other);
    }

    private void OnTriggerStay(Collider other)
    {
        // 最初からTrigger内にいる状態になっても、確実に検知する。
        TryStartFall(other);
    }

    private void TryStartFall(Collider other)
    {
        EnemyController enemy = other.GetComponentInParent<EnemyController>();

        if (enemy == null)
        {
            return;
        }

        if (!ContainsCenter(enemy.transform.position))
        {
            return;
        }

        enemy.EnterFalling(GetHoleCenter());
    }

    public static bool TryFindCrossedHole(
        Vector3 previousPosition,
        Vector3 currentPosition,
        out Vector3 holeCenter
    )
    {
        for (int i = ActiveHoles.Count - 1; i >= 0; i--)
        {
            HoleFallTrigger hole = ActiveHoles[i];

            if (hole == null)
            {
                ActiveHoles.RemoveAt(i);
                continue;
            }

            if (!hole.isActiveAndEnabled || hole._holeCollider == null)
            {
                continue;
            }

            if (hole.ContainsCenter(currentPosition) ||
                hole.SegmentCrossesHole(previousPosition, currentPosition))
            {
                holeCenter = hole.GetHoleCenter();
                return true;
            }
        }

        holeCenter = default;
        return false;
    }

    private bool ContainsCenter(Vector3 worldPosition)
    {
        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        Vector3 offset = localPosition - _holeCollider.center;
        Vector3 halfSize = _holeCollider.size * 0.5f;

        return Mathf.Abs(offset.x) < halfSize.x &&
               Mathf.Abs(offset.y) <= halfSize.y + 0.01f &&
               Mathf.Abs(offset.z) < halfSize.z;
    }

    private bool SegmentCrossesHole(Vector3 worldStart, Vector3 worldEnd)
    {
        Vector3 start = transform.InverseTransformPoint(worldStart) -
            _holeCollider.center;
        Vector3 end = transform.InverseTransformPoint(worldEnd) -
            _holeCollider.center;
        Vector3 halfSize = _holeCollider.size * 0.5f + Vector3.one * 0.001f;

        float segmentMinY = Mathf.Min(start.y, end.y);
        float segmentMaxY = Mathf.Max(start.y, end.y);
        if (segmentMaxY < -halfSize.y || segmentMinY > halfSize.y)
        {
            return false;
        }

        float enter = 0f;
        float exit = 1f;
        Vector3 delta = end - start;

        return ClipAxis(start.x, delta.x, -halfSize.x, halfSize.x, ref enter, ref exit) &&
               ClipAxis(start.z, delta.z, -halfSize.z, halfSize.z, ref enter, ref exit);
    }

    private static bool ClipAxis(
        float start,
        float delta,
        float minimum,
        float maximum,
        ref float enter,
        ref float exit
    )
    {
        if (Mathf.Abs(delta) < 0.000001f)
        {
            return start >= minimum && start <= maximum;
        }

        float inverseDelta = 1f / delta;
        float first = (minimum - start) * inverseDelta;
        float second = (maximum - start) * inverseDelta;

        if (first > second)
        {
            (first, second) = (second, first);
        }

        enter = Mathf.Max(enter, first);
        exit = Mathf.Min(exit, second);
        return enter <= exit;
    }

    private Vector3 GetHoleCenter()
    {
        return transform.parent != null
            ? transform.parent.position
            : transform.position;
    }
}
