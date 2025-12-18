using UnityEngine;
using TMPro;

public class InteractableText : MonoBehaviour
{
    [Header("UI")]
    public GameObject interactionPanel;
    public TextMeshProUGUI interactionText;

    [Header("Texts")]
    [TextArea]
    public string promptText = "Appuyez sur E pour observer l'objet";

    [TextArea(4, 8)]
    public string contentText = "Ceci est le vrai texte descriptif de l'objet.";

    private bool playerInRange;
    private bool isReading;

    void Start()
    {
        interactionPanel.SetActive(false);
    }

    void Update()
    {
        if (!playerInRange) return;

        if (Input.GetKeyDown(KeyCode.E))
        {
            if (!isReading)
            {
                ShowContent();
            }
            else
            {
                CloseContent();
            }
        }
    }

    void ShowPrompt()
    {
        interactionPanel.SetActive(true);
        interactionText.text = promptText;
        isReading = false;
    }

    void ShowContent()
    {
        interactionPanel.SetActive(true);
        interactionText.text = contentText;
        isReading = true;
    }

    void CloseContent()
    {
        interactionPanel.SetActive(false);
        isReading = false;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
            ShowPrompt();
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
            CloseContent();
        }
    }
}