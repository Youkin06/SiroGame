using System;
using System.Collections.Generic;
using UnityEngine;

namespace SiroGame.StageBuilder
{
    public enum BoxStageCellType
    {
        Box,
        Hole
    }

    [Serializable]
    public sealed class BoxStageCell
    {
        [SerializeField] private Vector3Int _position;
        [SerializeField] private BoxStageCellType _cellType;
        [SerializeField] private BoxRuleTile _tile;
        [SerializeField] private Vector3 _boxSize = Vector3.one;
        [SerializeField] private float _holeDepth = 5f;

        public BoxStageCell(Vector3Int position, BoxRuleTile tile, Vector3 boxSize)
        {
            _position = position;
            _tile = tile;
            _boxSize = boxSize;
        }

        public Vector3Int Position => _position;
        public BoxStageCellType CellType => _cellType;
        public BoxRuleTile Tile => _tile;
        public Vector3 BoxSize => _boxSize;
        public float HoleDepth => _holeDepth;

        public void SetBox(BoxRuleTile tile, Vector3 boxSize)
        {
            _cellType = BoxStageCellType.Box;
            _tile = tile;
            _boxSize = boxSize;
        }

        public void SetHole(float holeDepth)
        {
            _cellType = BoxStageCellType.Hole;
            _tile = null;
            _boxSize = Vector3.one;
            _holeDepth = holeDepth;
        }
    }

    /// <summary>
    /// ボックスステージのグリッド情報を保持するアセット。
    /// シーン上のオブジェクトは、このデータから再生成できる。
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewBoxStage",
        menuName = "SiroGame/Box Stage/Stage Data"
    )]
    public sealed class BoxStageData : ScriptableObject
    {
        [SerializeField] private Vector3 _cellSize = Vector3.one;
        [SerializeField] private List<BoxRuleTile> _tilePalette = new();
        [SerializeField, HideInInspector] private List<BoxStageCell> _cells = new();

        public Vector3 CellSize => _cellSize;
        public IReadOnlyList<BoxRuleTile> TilePalette => _tilePalette;
        public IReadOnlyList<BoxStageCell> Cells => _cells;

        public void SetCellSize(Vector3 cellSize)
        {
            _cellSize = new Vector3(
                Mathf.Max(0.01f, cellSize.x),
                Mathf.Max(0.01f, cellSize.y),
                Mathf.Max(0.01f, cellSize.z)
            );
        }

        public bool SetCell(Vector3Int position, BoxRuleTile tile, Vector3 boxSize)
        {
            if (tile == null)
            {
                return false;
            }

            Vector3 safeSize = new Vector3(
                Mathf.Max(0.01f, boxSize.x),
                Mathf.Max(0.01f, boxSize.y),
                Mathf.Max(0.01f, boxSize.z)
            );

            int index = FindCellIndex(position);
            if (index >= 0)
            {
                BoxStageCell existing = _cells[index];
                if (existing.CellType == BoxStageCellType.Box &&
                    existing.Tile == tile && existing.BoxSize == safeSize)
                {
                    return false;
                }

                existing.SetBox(tile, safeSize);
                return true;
            }

            _cells.Add(new BoxStageCell(position, tile, safeSize));
            return true;
        }

        public bool SetHole(Vector3Int position, float holeDepth)
        {
            float safeDepth = Mathf.Max(0.1f, holeDepth);
            int index = FindCellIndex(position);

            if (index >= 0)
            {
                BoxStageCell existing = _cells[index];
                if (existing.CellType == BoxStageCellType.Hole &&
                    Mathf.Approximately(existing.HoleDepth, safeDepth))
                {
                    return false;
                }

                existing.SetHole(safeDepth);
                return true;
            }

            BoxStageCell hole = new BoxStageCell(position, null, Vector3.one);
            hole.SetHole(safeDepth);
            _cells.Add(hole);
            return true;
        }

        public bool RemoveCell(Vector3Int position)
        {
            int index = FindCellIndex(position);
            if (index < 0)
            {
                return false;
            }

            _cells.RemoveAt(index);
            return true;
        }

        public void ClearCells()
        {
            _cells.Clear();
        }

        public BoxStageCell GetCell(Vector3Int position)
        {
            int index = FindCellIndex(position);
            return index >= 0 ? _cells[index] : null;
        }

        private int FindCellIndex(Vector3Int position)
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                if (_cells[i].Position == position)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
