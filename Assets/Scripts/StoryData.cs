using UnityEngine;

[CreateAssetMenu(fileName = "StoryData", menuName = "Scriptable Objects/StoryData")]
public class StoryData : ScriptableObject
{
    public DialoguePanel[] DialoguePanels;
}
[System.Serializable]
public class DialoguePanel
{
    public string Text;
    public string LoadScene;
    public string UnloadScene;
    public bool SwapController;
}
