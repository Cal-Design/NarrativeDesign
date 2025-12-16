using UnityEngine;

public class DialogueManager : MonoBehaviour
{
    GeminiRESTClient restClient;
    public DialoguePanel[] DialoguePanels;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        restClient.SendPrompt("What is the name of the gameAward winner this year ?");
        //DialogueStart()
    }

    public void DialogueStart()
    {
        foreach (DialoguePanel panel in DialoguePanels)
        {
            if (panel.AnswerResults == null)
            {
                
            }
            Debug.Log(panel.Text);
        }
    }
}

public class DialoguePanel
{
    public string Text;
    public string VoiceToPlay;
    public int ID;
    public AnswerResult[] AnswerResults;
}

public class AnswerResult
{
    public string[] Subject;
    public int JumpToID;
}
