using System;
using System.Collections.Generic;
using UnityEngine;

namespace SiroGame.StageBuilder
{
    public enum BoxNeighborCondition
    {
        Any,
        Empty,
        SameTile,
        DifferentTile,
        AnyTile,
        Hole
    }

    [Serializable]
    public sealed class BoxAdjacencyRule
    {
        [SerializeField] private string _name = "New Rule";
        [SerializeField] private BoxNeighborCondition _north = BoxNeighborCondition.Any;
        [SerializeField] private BoxNeighborCondition _east = BoxNeighborCondition.Any;
        [SerializeField] private BoxNeighborCondition _south = BoxNeighborCondition.Any;
        [SerializeField] private BoxNeighborCondition _west = BoxNeighborCondition.Any;
        [SerializeField] private GameObject _resultPrefab;
        [SerializeField] private Vector3 _rotationOffset;
        [SerializeField] private bool _allowRotation = true;

        public string Name => _name;
        public GameObject ResultPrefab => _resultPrefab;
        public Vector3 RotationOffset => _rotationOffset;
        public bool AllowRotation => _allowRotation;

        public BoxNeighborCondition GetCondition(int directionIndex)
        {
            return directionIndex switch
            {
                0 => _north,
                1 => _east,
                2 => _south,
                3 => _west,
                _ => BoxNeighborCondition.Any
            };
        }
    }

    public readonly struct BoxRuleResult
    {
        public BoxRuleResult(GameObject prefab, Quaternion rotation)
        {
            Prefab = prefab;
            Rotation = rotation;
        }

        public GameObject Prefab { get; }
        public Quaternion Rotation { get; }
    }

    public readonly struct BoxRuleNeighbor
    {
        public BoxRuleNeighbor(BoxRuleTile tile, bool isHole)
        {
            Tile = tile;
            IsHole = isHole;
        }

        public BoxRuleTile Tile { get; }
        public bool IsHole { get; }
        public bool HasTile => Tile != null;
        public bool IsEmpty => !HasTile;
    }

    /// <summary>
    /// 1種類のボックスと、その上下左右の接続ルールを定義するアセット。
    /// North はワールドの +Z、East は +X として扱う。
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewBoxRuleTile",
        menuName = "SiroGame/Box Stage/Rule Tile"
    )]
    public sealed class BoxRuleTile : ScriptableObject
    {
        [SerializeField] private GameObject _defaultPrefab;
        [SerializeField] private Vector3 _defaultRotation;
        [SerializeField] private List<BoxAdjacencyRule> _rules = new();

        public GameObject DefaultPrefab => _defaultPrefab;
        public IReadOnlyList<BoxAdjacencyRule> Rules => _rules;

        public BoxRuleResult Resolve(
            BoxRuleNeighbor north,
            BoxRuleNeighbor east,
            BoxRuleNeighbor south,
            BoxRuleNeighbor west
        )
        {
            BoxRuleNeighbor[] neighbors = { north, east, south, west };

            foreach (BoxAdjacencyRule rule in _rules)
            {
                int rotationCount = rule.AllowRotation ? 4 : 1;

                for (int rotationStep = 0; rotationStep < rotationCount; rotationStep++)
                {
                    if (!Matches(rule, neighbors, rotationStep))
                    {
                        continue;
                    }

                    GameObject prefab = rule.ResultPrefab != null
                        ? rule.ResultPrefab
                        : _defaultPrefab;

                    Quaternion rotation = Quaternion.Euler(
                        rule.RotationOffset + Vector3.up * (rotationStep * 90f)
                    );
                    return new BoxRuleResult(prefab, rotation);
                }
            }

            return new BoxRuleResult(_defaultPrefab, Quaternion.Euler(_defaultRotation));
        }

        private bool Matches(
            BoxAdjacencyRule rule,
            IReadOnlyList<BoxRuleNeighbor> neighbors,
            int rotationStep
        )
        {
            for (int worldDirection = 0; worldDirection < 4; worldDirection++)
            {
                int sourceDirection = (worldDirection - rotationStep + 4) % 4;
                BoxNeighborCondition condition = rule.GetCondition(sourceDirection);

                if (!MatchesCondition(condition, neighbors[worldDirection]))
                {
                    return false;
                }
            }

            return true;
        }

        private bool MatchesCondition(
            BoxNeighborCondition condition,
            BoxRuleNeighbor neighbor
        )
        {
            return condition switch
            {
                BoxNeighborCondition.Any => true,
                BoxNeighborCondition.Empty => neighbor.IsEmpty,
                BoxNeighborCondition.SameTile => neighbor.Tile == this,
                BoxNeighborCondition.DifferentTile => neighbor.HasTile && neighbor.Tile != this,
                BoxNeighborCondition.AnyTile => neighbor.HasTile,
                BoxNeighborCondition.Hole => neighbor.IsHole,
                _ => false
            };
        }
    }
}
