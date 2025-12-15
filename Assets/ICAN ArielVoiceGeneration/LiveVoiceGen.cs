using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections.Generic;
using Unity.VisualScripting;

public class LiveVoiceGen : MonoBehaviour
{
    public AudioSource audioSource;
    string url;
    UnityWebRequest www;
    AudioContent info = null;
    [Header("Information")]
    public string PlayerName;
    public string VoiceToUse;
    public string NPCName;

    private void Start()
    {
        PlayVoice();
    }

    private async void PlayVoice()
    {
        AudioClip clip = await TextToAudioLive(VoiceToUse, "hello " + PlayerName + " , my name is " + NPCName, "none", 1f, "31c67b73-e452-467e-b3bc-68324176dba6");

        if (clip != null)
        {
            audioSource.clip = clip;
            audioSource.Play();
        }
    }
    public async Task<AudioClip> TextToAudioLive(string option, string phrase, string effect, float volume, string apikey)
    {
        AudioClip soundToReturn = null;

        if (apikey == null)
        {
            UnityEngine.Debug.LogError("Please contact the support at contact@xandimmersion.com");
            return soundToReturn;
        }

        if (phrase == null || phrase == "")
        {
            UnityEngine.Debug.LogError($"Text is empty. It will be ignored");
            return soundToReturn;
        }
        www = null;
        WWWForm form = new WWWForm();
        form.AddField("sentence", phrase);
        form.AddField("volume", volume.ToString().Replace(',', '.'));
        //form.AddField("convergence", temperature.ToString().Replace(',', '.'));   
        form.AddField("effect", effect.Replace(',', '.'));

        string lien = $"https://ariel-api.xandimmersion.com/tts/" + option;
        www = UnityWebRequest.Post(lien, form);
        www.SetRequestHeader("Authorization", "Api-Key " + apikey);

        var operation = www.SendWebRequest();
        while (!operation.isDone)
        {
            await Task.Yield(); // Prevent blocking the main thread
        }

        if (www.result == UnityWebRequest.Result.ProtocolError)
        {
            UnityEngine.Debug.LogError("Error While Sending: " + www.error + " " + www.downloadHandler);
        }
        else
        {
            info = JsonUtility.FromJson<AudioContent>(www.downloadHandler.text);
        }
        //UnityEngine.Debug.Log(info.audio);
        //url = "https://rocky-taiga-14840.herokuapp.com/" + info.audio;
        url = info.audio;
        using (UnityWebRequest www_audio = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV))
        {
            www_audio.SetRequestHeader("x-requested-with", "http://127.0.0.1:8080");

            var download_operation = www_audio.SendWebRequest();
            while (!download_operation.isDone)
            {
                await Task.Yield(); // Prevent blocking the main thread
            }

            if (www_audio.result == UnityWebRequest.Result.ConnectionError)
            {
                UnityEngine.Debug.LogError(www_audio.error);
            }
            else
            {
                soundToReturn = DownloadHandlerAudioClip.GetContent(www_audio);
            }
        }

        AssetDatabase.Refresh();
        info = null;
        return soundToReturn;
    }

    public class AudioContent
    {
        public string sentence;
        public string audio;
    }

    
}
