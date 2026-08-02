using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// タイトル画面の2項目メニューをキーボードで操作する。
/// Scene内の参照はInspectorへ持たず、Awakeで既存オブジェクトから取得する。
/// </summary>
[DisallowMultipleComponent]
public sealed class TitleMenuController : MonoBehaviour
{
    private const int StartIndex = 0;
    private const int SettingsIndex = 1;
    private const float StartSelectY = -76f;
    private const float SettingsSelectY = -186f;

    private RectTransform _selectRectTransform;
    private TMP_Text _startText;
    private TMP_Text _settingsText;
    private int _selectedIndex;

    public int SelectedIndex => _selectedIndex;
    public event Action SettingsSelected;

    private void Awake()
    {
        GameObject selectObject = GameObject.Find("select");
        GameObject startObject = GameObject.Find("Start");
        GameObject settingsObject = GameObject.Find("setting");

        if (selectObject != null)
        {
            _selectRectTransform = selectObject.GetComponent<RectTransform>();
        }

        if (startObject != null)
        {
            _startText = startObject.GetComponent<TMP_Text>();
        }

        if (settingsObject != null)
        {
            _settingsText = settingsObject.GetComponent<TMP_Text>();
        }

        if (_selectRectTransform == null || _startText == null || _settingsText == null)
        {
            Debug.LogError(
                "TitleMenuControllerに必要なselect、Start、settingが見つかりません。",
                this
            );
            enabled = false;
            return;
        }

        SetSelection(StartIndex);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        bool moveUp = keyboard.wKey.wasPressedThisFrame ||
                      keyboard.upArrowKey.wasPressedThisFrame;
        bool moveDown = keyboard.sKey.wasPressedThisFrame ||
                        keyboard.downArrowKey.wasPressedThisFrame;

        if (moveUp)
        {
            MoveSelection(-1);
        }
        else if (moveDown)
        {
            MoveSelection(1);
        }

        if (keyboard.enterKey.wasPressedThisFrame ||
            keyboard.numpadEnterKey.wasPressedThisFrame)
        {
            ConfirmSelection();
        }
    }

    public void MoveSelection(int direction)
    {
        if (direction == 0)
        {
            return;
        }

        int nextIndex = _selectedIndex == StartIndex
            ? SettingsIndex
            : StartIndex;
        SetSelection(nextIndex);
    }

    public void SetSelection(int index)
    {
        _selectedIndex = Mathf.Clamp(index, StartIndex, SettingsIndex);

        Vector2 selectPosition = _selectRectTransform.anchoredPosition;
        selectPosition.y = _selectedIndex == StartIndex
            ? StartSelectY
            : SettingsSelectY;
        _selectRectTransform.anchoredPosition = selectPosition;

        _startText.color = _selectedIndex == StartIndex
            ? Color.white
            : Color.black;
        _settingsText.color = _selectedIndex == SettingsIndex
            ? Color.white
            : Color.black;
    }

    public void ConfirmSelection()
    {
        if (_selectedIndex == StartIndex)
        {
            if (TileLoadingScreen.Instance == null)
            {
                Debug.LogError(
                    "TitleSceneにTileLoadingScreenが見つかりません。",
                    this
                );
                return;
            }

            if (!TileLoadingScreen.Instance.IsLoading)
            {
                TileLoadingScreen.Instance.LoadNewGameScene("SampleScene");
            }

            return;
        }

        SettingsSelected?.Invoke();
        Debug.Log("設定が選択されました。設定画面は未接続です。", this);
    }
}
