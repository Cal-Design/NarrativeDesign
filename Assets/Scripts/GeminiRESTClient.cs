using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System;

public class GeminiRESTClient : MonoBehaviour
{
    private string apiKey;
    // Using 1.5-flash for speed and cost-efficiency
    private string apiURL = "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent";

    void Awake()
    {
        // Safely grab the key from the machine's environment variables
        apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");

        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("Gemini API Key not found! Add GEMINI_API_KEY to your System Environment Variables.");
        }
    }

    public void SendPrompt(string userPrompt)
    {
        if (string.IsNullOrEmpty(apiKey)) return;
        StartCoroutine(PostRequest(userPrompt));
    }

    IEnumerator PostRequest(string prompt)
    {
        // 1. Create the Request Body (Matches Gemini's nested JSON structure)
        // For simple scripts, we use a string; for complex ones, use a Serialized class.
        string jsonData = "{\"contents\":[{\"parts\":[{\"text\":\"" + prompt + "\"}]}]}";

        // 2. Setup the UnityWebRequest
        using (UnityWebRequest request = new UnityWebRequest($"{apiURL}?key={apiKey}", "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            
            request.SetRequestHeader("Content-Type", "application/json");

            // 3. Send and Wait
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                ParseResponse(request.downloadHandler.text);
            }
            else
            {
                Debug.LogError($"Gemini Error: {request.error} | Response: {request.downloadHandler.text}");
            }
        }
    }

    void ParseResponse(string rawJson)
    {
        // Gemini returns a massive JSON. To get just the text:
        // You can use a JSON library like Newtonsoft, but for a quick test:
        Debug.Log("Full JSON: " + rawJson);
        
        // Simple extraction logic (recommended: use JsonUtility or Newtonsoft for production)
        if (rawJson.Contains("\"text\": \""))
        {
            int start = rawJson.IndexOf("\"text\": \"") + 9;
            int end = rawJson.IndexOf("\"", start);
            string responseText = rawJson.Substring(start, end - start);
            Debug.Log("<color=green>Gemini Response:</color> " + responseText.Replace("\\n", "\n"));
        }
    }
}