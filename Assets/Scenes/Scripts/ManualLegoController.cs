using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ManualLegoController : MonoBehaviour
{
    public List<PageData> pages; 

    public Sprite displayImage;
    public Transform buttonContainer;
    public TextMeshProUGUI containerText;

    public GameObject prefabBotonUI;

    private int actualPage = 0;

    void Start()
    {
        LoadPage(0);
    }

    public void LoadPage(int index)
    {
        if (pages == null || index < 0 || index >= pages.Count) return;
        
        actualPage = index;
        PageData pageData = pages[actualPage];

        displayImage = pageData.imagenInstruccion;
        if (containerText != null) containerText.text = $"{actualPage + 1} / {pages.Count}";
        

        foreach (Transform button in buttonContainer)
        {
            Destroy(button.gameObject);
        }

        foreach (ButtonData configButton in pageData.listaBotones)
        {
            GameObject newButton = Instantiate(prefabBotonUI, buttonContainer);
            
            ButtonSpawnLego button = newButton.GetComponent<ButtonSpawnLego>();
            if (button != null)
            {
                button.InitButton(configButton);
            }
        }
    }

    public void GetNextPage() { if (actualPage < pages.Count - 1) LoadPage(actualPage + 1); }
    public void GetPreviousPage() { if (actualPage > 0) LoadPage(actualPage - 1); }
}
