using System.Collections.Generic;
using UnityEngine;

public readonly struct HoleFallArea
{
    public HoleFallArea(Vector3 center, Vector2 halfExtents)
    {
        Center = center;
        HalfExtents = halfExtents;
    }

    public Vector3 Center { get; }
    public Vector2 HalfExtents { get; }

    /// <summary>
    /// 穴の長方形内で最も余裕がある対角線のうち、現在の向きに近い方向を返す。
    /// </summary>
    public Vector3 GetNearestDiagonal(Vector3 currentForward)
    {
        Vector3 first = new Vector3(HalfExtents.x, 0f, HalfExtents.y).normalized;
        Vector3 second = new Vector3(HalfExtents.x, 0f, -HalfExtents.y).normalized;
        Vector3 planarForward = Vector3.ProjectOnPlane(currentForward, Vector3.up);

        if (planarForward.sqrMagnitude < 0.0001f)
        {
            return first;
        }

        planarForward.Normalize();
        Vector3[] candidates = { first, -first, second, -second };
        Vector3 best = candidates[0];
        float bestDot = Vector3.Dot(planarForward, best);

        for (int i = 1; i < candidates.Length; i++)
        {
            float dot = Vector3.Dot(planarForward, candidates[i]);
            if (dot > bestDot)
            {
                best = candidates[i];
                bestDot = dot;
            }
        }

        return best;
    }
}

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

        enemy.EnterFalling(GetConnectedHoleArea());
    }

    public static bool TryFindCrossedHole(
        Vector3 previousPosition,
        Vector3 currentPosition,
        out Vector3 holeCenter
    )
    {
        if (TryFindCrossedHole(previousPosition, currentPosition, out HoleFallArea area))
        {
            holeCenter = area.Center;
            return true;
        }

        holeCenter = default;
        return false;
    }

    public static bool TryFindCrossedHole(
        Vector3 previousPosition,
        Vector3 currentPosition,
        out HoleFallArea holeArea
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
                holeArea = hole.GetConnectedHoleArea();
                return true;
            }
        }

        holeArea = default;
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

    private HoleFallArea GetConnectedHoleArea()
    {
        List<HoleFallTrigger> connected = new() { this };

        for (int index = 0; index < connected.Count; index++)
        {
            HoleFallTrigger current = connected[index];

            foreach (HoleFallTrigger candidate in ActiveHoles)
            {
                if (candidate == null || connected.Contains(candidate) ||
                    !candidate.isActiveAndEnabled || candidate._holeCollider == null)
                {
                    continue;
                }

                if (AreSideAdjacent(current, candidate))
                {
                    connected.Add(candidate);
                }
            }
        }

        Bounds combinedBounds = connected[0]._holeCollider.bounds;
        foreach (HoleFallTrigger hole in connected)
        {
            combinedBounds.Encapsulate(hole._holeCollider.bounds);
        }

        Vector3 center = GetHoleCenter();
        center.x = combinedBounds.center.x;
        center.z = combinedBounds.center.z;

        return new HoleFallArea(
            center,
            new Vector2(combinedBounds.extents.x, combinedBounds.extents.z)
        );
    }

    private static bool AreSideAdjacent(HoleFallTrigger first, HoleFallTrigger second)
    {
        const float edgeTolerance = 0.02f;

        Vector3 firstCenter = first.GetHoleCenter();
        Vector3 secondCenter = second.GetHoleCenter();
        if (Mathf.Abs(firstCenter.y - secondCenter.y) > edgeTolerance)
        {
            return false;
        }

        Bounds firstBounds = first._holeCollider.bounds;
        Bounds secondBounds = second._holeCollider.bounds;
        float overlapX = Mathf.Min(firstBounds.max.x, secondBounds.max.x) -
            Mathf.Max(firstBounds.min.x, secondBounds.min.x);
        float overlapZ = Mathf.Min(firstBounds.max.z, secondBounds.max.z) -
            Mathf.Max(firstBounds.min.z, secondBounds.min.z);

        bool touchesOnX = overlapZ > edgeTolerance &&
            (Mathf.Abs(firstBounds.max.x - secondBounds.min.x) <= edgeTolerance ||
             Mathf.Abs(secondBounds.max.x - firstBounds.min.x) <= edgeTolerance);
        bool touchesOnZ = overlapX > edgeTolerance &&
            (Mathf.Abs(firstBounds.max.z - secondBounds.min.z) <= edgeTolerance ||
             Mathf.Abs(secondBounds.max.z - firstBounds.min.z) <= edgeTolerance);

        return touchesOnX || touchesOnZ;
    }
}
