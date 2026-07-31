using UnityEngine;

namespace SiroGame.StageBuilder
{
    /// <summary>
    /// 生成されたステージの配置基準になるコンポーネント。
    /// </summary>
    public sealed class BoxStageRoot : MonoBehaviour
    {
        [SerializeField] private BoxStageData _stageData;

        public BoxStageData StageData => _stageData;

        public void SetStageData(BoxStageData stageData)
        {
            _stageData = stageData;
        }
    }
}
