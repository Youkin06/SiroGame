using SiroGame.StageBuilder;
using UnityEditor;

namespace SiroGame.Editor.StageBuilder
{
    [CustomEditor(typeof(BoxRuleTile))]
    public sealed class BoxRuleTileEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "Northは+Z、Eastは+Xです。Rulesは上から順に評価されます。\n" +
                "Allow Rotationを有効にすると、同じルールを90度ずつ回転して照合します。\n" +
                "HoleはEmptyにも一致します。穴専用ルールはEmptyルールより上に置いてください。",
                MessageType.Info
            );

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_defaultPrefab")
            );
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_defaultRotation")
            );
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_rules"),
                true
            );

            serializedObject.ApplyModifiedProperties();
        }
    }
}
