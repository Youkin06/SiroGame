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
    private AudioSource _decisionAudioSource;
    private GameObject _selectObject;
    private GameObject _startObject;
    private GameObject _settingsObject;
    private int _selectedIndex;
    private bool _menuVisible = true;

    public int SelectedIndex => _selectedIndex;
    public bool IsMenuVisible => _menuVisible;
    public event Action SettingsSelected;

    private void Awake()
    {
        _selectObject = GameObject.Find("select");
        _startObject = GameObject.Find("Start");
        _settingsObject = GameObject.Find("setting");
        _decisionAudioSource = GetComponent<AudioSource>();

        if (_selectObject != null)
        {
            _selectRectTransform = _selectObject.GetComponent<RectTransform>();
        }

        if (_startObject != null)
        {
            _startText = _startObject.GetComponent<TMP_Text>();
        }

        if (_settingsObject != null)
        {
            _settingsText = _settingsObject.GetComponent<TMP_Text>();
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

        if (_decisionAudioSource == null || _decisionAudioSource.clip == null)
        {
            Debug.LogError(
                "TitleMenuSystemのAudioSourceにselect_001が設定されていません。",
                this
            );
        }
        else
        {
            if (_decisionAudioSource.clip.loadState == AudioDataLoadState.Unloaded)
            {
                _decisionAudioSource.clip.LoadAudioData();
            }

            _decisionAudioSource.playOnAwake = false;
            _decisionAudioSource.loop = false;
            _decisionAudioSource.spatialBlend = 0f;
        }

        SetSelection(StartIndex);
    }

    private void Update()
    {
        if (!_menuVisible)
        {
            return;
        }

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
        if (!_menuVisible || direction == 0)
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
        if (!_menuVisible)
        {
            return;
        }

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
                PlayDecisionSound();
                TileLoadingScreen.Instance.LoadNewGameScene("SampleScene");
            }

            return;
        }

        PlayDecisionSound();
        SettingsSelected?.Invoke();
        Debug.Log("設定が選択されました。設定画面は未接続です。", this);
    }

    public void SetMenuVisible(bool visible)
    {
        _menuVisible = visible;
        _startObject.SetActive(visible);
        _settingsObject.SetActive(visible);
        _selectObject.SetActive(visible);

        if (visible)
        {
            SetSelection(StartIndex);
        }
    }

    private void PlayDecisionSound()
    {
        if (_decisionAudioSource != null && _decisionAudioSource.clip != null)
        {
            _decisionAudioSource.PlayOneShot(_decisionAudioSource.clip);
        }
    }
}
