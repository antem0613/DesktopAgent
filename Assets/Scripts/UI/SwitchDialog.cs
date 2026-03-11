using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class SwitchDialog : MonoBehaviour
{
    [System.Serializable]
    struct Dialog
    {
        public Button TopTab;
        public GameObject Panel;
    }

    [SerializeField] Dialog[] dialogs;
    [SerializeField] Button closeButton, backButton, shutdownButton;
    [SerializeField] GameObject dialogContainer, shutdownConfirmPanel;
    [SerializeField] MenuDialog menuDialog;
    Stack<int> history = new Stack<int>();

    void Start()
    {
        for (int i = 0; i < dialogs.Length; i++)
        {
            int index = i;
            dialogs[i].TopTab.onClick.AddListener(() => SwitchDialogPanel(index));
            dialogs[i].Panel.SetActive(false);
        }
        closeButton.onClick.AddListener(OnCloseButtonClicked);
        backButton.onClick.AddListener(OnBackButtonClicked);
        shutdownButton.onClick.AddListener(OnShutdownButtonClicked);
        shutdownConfirmPanel.SetActive(false);

        SwitchDialogPanel(0);
    }

    public void SwitchDialogPanel(int index)
    {
        SwitchDialogPanelInternal(index, true);
    }

    public void OnCloseButtonClicked()
    {
        menuDialog.Hide();
    }

    public void OnBackButtonClicked()
    {
        if(history.Count > 1)
        {
            int currentIndex = history.Pop();
            SwitchDialogPanelInternal(history.Peek(), false, currentIndex);
        }else if(shutdownConfirmPanel.activeSelf)
        {
            shutdownConfirmPanel.SetActive(false);
        }
    }

    public void OnShutdownButtonClicked()
    {
        shutdownConfirmPanel.SetActive(true);
    }

    void SwitchDialogPanelInternal(int index, bool recordHistory, int? currentIndex = null)
    {
        if(shutdownConfirmPanel.activeSelf)
        {
            shutdownConfirmPanel.SetActive(false);
        }

        int indexToDeactivate = currentIndex ?? (history.Count > 0 ? history.Peek() : -1);
        if (indexToDeactivate >= 0)
        {
            dialogs[indexToDeactivate].Panel.SetActive(false);
        }
        dialogs[index].Panel.SetActive(true);

        if (recordHistory)
        {
            history.Push(index);
        }
    }
}
