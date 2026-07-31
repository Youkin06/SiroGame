using System.Collections.Generic;
using SiroGame.StageBuilder;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SiroGame.Editor.StageBuilder
{
    public static class BoxStageGenerator
    {
        private const string GeneratedRootName = "__GeneratedBoxes";

        public static void Rebuild(BoxStageRoot stageRoot)
        {
            if (stageRoot == null || stageRoot.StageData == null)
            {
                return;
            }

            RemoveGeneratedRoot(stageRoot);

            GameObject generatedObject = new GameObject(GeneratedRootName);
            Transform generatedTransform = generatedObject.transform;
            generatedTransform.SetParent(stageRoot.transform, false);
            generatedObject.AddComponent<BoxStageGeneratedRoot>();

            BoxStageData stageData = stageRoot.StageData;
            Dictionary<Vector3Int, BoxStageCell> cells = BuildCellMap(stageData);
            HashSet<BoxRuleTile> missingPrefabWarnings = new();

            foreach (BoxStageCell cell in stageData.Cells)
            {
                if (cell.Tile == null)
                {
                    continue;
                }

                BoxRuleResult result = cell.Tile.Resolve(
                    GetTile(cells, cell.Position + Vector3Int.forward),
                    GetTile(cells, cell.Position + Vector3Int.right),
                    GetTile(cells, cell.Position + Vector3Int.back),
                    GetTile(cells, cell.Position + Vector3Int.left)
                );

                if (result.Prefab == null)
                {
                    if (missingPrefabWarnings.Add(cell.Tile))
                    {
                        Debug.LogWarning(
                            $"Rule Tile '{cell.Tile.name}' に生成可能なPrefabがありません。",
                            cell.Tile
                        );
                    }

                    continue;
                }

                GameObject instance = CreatePrefabInstance(result.Prefab, generatedTransform);
                Transform instanceTransform = instance.transform;
                Quaternion prefabRotation = instanceTransform.localRotation;
                Vector3 prefabScale = instanceTransform.localScale;

                instance.name = $"{cell.Tile.name}_{cell.Position.x}_{cell.Position.y}_{cell.Position.z}";
                instanceTransform.localPosition = Vector3.Scale(
                    (Vector3)cell.Position,
                    stageData.CellSize
                );
                instanceTransform.localRotation = result.Rotation * prefabRotation;
                instanceTransform.localScale = Vector3.Scale(prefabScale, cell.BoxSize);
            }

            EditorSceneManager.MarkSceneDirty(stageRoot.gameObject.scene);
        }

        public static void RemoveGeneratedRoot(BoxStageRoot stageRoot)
        {
            if (stageRoot == null)
            {
                return;
            }

            BoxStageGeneratedRoot generatedRoot = null;

            for (int i = 0; i < stageRoot.transform.childCount; i++)
            {
                Transform child = stageRoot.transform.GetChild(i);
                if (child.TryGetComponent(out BoxStageGeneratedRoot marker))
                {
                    generatedRoot = marker;
                    break;
                }
            }

            if (generatedRoot != null)
            {
                Object.DestroyImmediate(generatedRoot.gameObject);
            }
        }

        private static Dictionary<Vector3Int, BoxStageCell> BuildCellMap(BoxStageData stageData)
        {
            Dictionary<Vector3Int, BoxStageCell> result = new();

            foreach (BoxStageCell cell in stageData.Cells)
            {
                result[cell.Position] = cell;
            }

            return result;
        }

        private static BoxRuleTile GetTile(
            IReadOnlyDictionary<Vector3Int, BoxStageCell> cells,
            Vector3Int position
        )
        {
            return cells.TryGetValue(position, out BoxStageCell cell)
                ? cell.Tile
                : null;
        }

        private static GameObject CreatePrefabInstance(GameObject prefab, Transform parent)
        {
            if (PrefabUtility.IsPartOfPrefabAsset(prefab))
            {
                return (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            }

            GameObject instance = Object.Instantiate(prefab, parent);
            instance.name = prefab.name;
            return instance;
        }
    }
}
