using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ButtonSpawnLego : MonoBehaviour
{
    public TextMeshProUGUI displayText;
    private ButtonData button;
    private LegoHandMenu handMenuManager;

    public void InitButton(ButtonData datos)
    {
        button = datos;
        handMenuManager = FindObjectOfType<LegoHandMenu>();

        if (displayText != null) displayText.text = $"{button.displayName} (x{button.spawnNumber})";

        GetComponent<Button>().onClick.AddListener(OnClick);
    }

    private void OnClick()
    {
        if (button.spawnNumber > 0 && handMenuManager != null)
        {            
            ButtonData data = new ButtonData();
            handMenuManager.OnBlockButtonPressed(data);

            button.spawnNumber--;

            if (displayText != null) displayText.text = $"{button.displayName} (x{button.spawnNumber})";
             
        }
        else  GetComponent<Button>().interactable = false;

    }
}
