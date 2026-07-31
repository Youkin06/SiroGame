using System.Collections.Generic;
using SiroGame.StageBuilder;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace SiroGame.Editor.StageBuilder
{
    public static class BoxStageGenerator
    {
        private const string GeneratedRootName = "__GeneratedBoxes";
        private const string NavigationProxyName = "NavigationProxy";
        private const string NavigationProxyMaterialPath =
            "Assets/Materials/NavigationProxyInvisible.mat";
        private const float NavigationProxyThickness = 0.02f;

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
                if (cell.CellType == BoxStageCellType.Hole)
                {
                    CreateHole(cell, stageData.CellSize, generatedTransform);
                    continue;
                }

                if (cell.Tile == null)
                {
                    continue;
                }

                BoxRuleResult result = cell.Tile.Resolve(
                    GetNeighbor(cells, cell.Position + Vector3Int.forward),
                    GetNeighbor(cells, cell.Position + Vector3Int.right),
                    GetNeighbor(cells, cell.Position + Vector3Int.back),
                    GetNeighbor(cells, cell.Position + Vector3Int.left)
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

        private static BoxRuleNeighbor GetNeighbor(
            IReadOnlyDictionary<Vector3Int, BoxStageCell> cells,
            Vector3Int position
        )
        {
            if (!cells.TryGetValue(position, out BoxStageCell cell))
            {
                return new BoxRuleNeighbor(null, false);
            }

            return cell.CellType == BoxStageCellType.Hole
                ? new BoxRuleNeighbor(null, true)
                : new BoxRuleNeighbor(cell.Tile, false);
        }

        private static void CreateHole(
            BoxStageCell cell,
            Vector3 cellSize,
            Transform parent
        )
        {
            GameObject holeRoot = new GameObject(
                $"Hole_{cell.Position.x}_{cell.Position.y}_{cell.Position.z}"
            );
            Transform holeTransform = holeRoot.transform;
            holeTransform.SetParent(parent, false);
            holeTransform.localPosition = Vector3.Scale((Vector3)cell.Position, cellSize);

            float deathTriggerMargin = Mathf.Clamp(
                Mathf.Min(cellSize.x, cellSize.z) * 0.5f,
                0.5f,
                1f
            );
            float triggerHeight = Mathf.Max(0.5f, cellSize.y);
            float openingY = cellSize.y * 0.5f;

            GameObject fallTriggerObject = new GameObject("FallTrigger");
            Transform fallTriggerTransform = fallTriggerObject.transform;
            fallTriggerTransform.SetParent(holeTransform, false);
            fallTriggerTransform.localPosition = Vector3.up * (
                openingY + triggerHeight * 0.5f
            );

            BoxCollider fallCollider = fallTriggerObject.AddComponent<BoxCollider>();
            fallCollider.isTrigger = true;
            fallCollider.size = new Vector3(
                cellSize.x,
                triggerHeight,
                cellSize.z
            );
            fallTriggerObject.AddComponent<HoleFallTrigger>();

            CreateNavigationProxy(cellSize, openingY, holeTransform);

            GameObject deathTriggerObject = new GameObject("DeathTrigger");
            Transform deathTriggerTransform = deathTriggerObject.transform;
            deathTriggerTransform.SetParent(holeTransform, false);
            deathTriggerTransform.localPosition = Vector3.down * (
                cell.HoleDepth - openingY
            );

            BoxCollider deathCollider = deathTriggerObject.AddComponent<BoxCollider>();
            deathCollider.isTrigger = true;
            deathCollider.size = new Vector3(
                cellSize.x + deathTriggerMargin * 2f,
                1f,
                cellSize.z + deathTriggerMargin * 2f
            );
            deathTriggerObject.AddComponent<HoleDeathZone>();
        }

        private static void CreateNavigationProxy(
            Vector3 cellSize,
            float openingY,
            Transform parent
        )
        {
            GameObject proxyObject = new GameObject(NavigationProxyName);
            Transform proxyTransform = proxyObject.transform;
            proxyTransform.SetParent(parent, false);
            proxyTransform.localPosition = Vector3.up * (
                openingY - NavigationProxyThickness * 0.5f
            );
            proxyTransform.localScale = new Vector3(
                cellSize.x,
                NavigationProxyThickness,
                cellSize.z
            );

            MeshFilter meshFilter = proxyObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

            MeshRenderer meshRenderer = proxyObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                NavigationProxyMaterialPath
            );
            meshRenderer.forceRenderingOff = true;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            if (meshRenderer.sharedMaterial == null)
            {
                Debug.LogError(
                    $"NavMesh用の不可視Materialが見つかりません: " +
                    NavigationProxyMaterialPath,
                    proxyObject
                );
            }
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
