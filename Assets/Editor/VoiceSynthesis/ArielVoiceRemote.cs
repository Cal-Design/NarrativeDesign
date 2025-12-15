using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.UIElements;

// Define custom classes
using ArielCommonTypes;
using ArielVoiceRemote;

namespace ArielVoiceRemote
{
    public class ArielRemote
    {
        public string url;
        UnityWebRequest www;

        string s_octave;
        
        string s_speed;

        private ArielSynthese info = null;

        public async Task<SpeakerSettings> GetSpeakers(string apikey)
        {
            // Define empty state object
            SpeakerSettings settings = new SpeakerSettings();
            //LanguageCodes languageCodes = new LanguageCodes();

            if (string.IsNullOrEmpty(apikey))
            {
                UnityEngine.Debug.LogError("API Key is missing.");
                return null;
            }
            else
            {
                UnityEngine.Debug.Log("API Key is set.");
            }

            string url = "https://ariel-api.xandimmersion.com/speakers";

            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                www.SetRequestHeader("Authorization", "Api-Key " + apikey);

                // Send the request asynchronously and wait for it to complete
                var operation = www.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield(); // Prevent blocking the main thread
                }

                if (www.result == UnityWebRequest.Result.ProtocolError || www.result == UnityWebRequest.Result.ConnectionError)
                {
                    UnityEngine.Debug.LogError("Error While Sending: " + www.error);
                    return null;
                }
                else
                {
                    try
                    {
                        string jsonResponse = www.downloadHandler.text;

                        // Unity's JsonUtility doesn't handle arrays directly, so we'll fix the JSON format
                        string fixedJson = "{\"speakers\":" + jsonResponse + "}";
                        Speaker[] speakers = JsonUtility.FromJson<SpeakerObject>(fixedJson).speakers;

                        settings.speakers = speakers;
                        return settings;
                    }
                    catch (System.Exception ex)
                    {
                        UnityEngine.Debug.LogError($"Error parsing JSON: {ex.Message}");
                        return settings;
                    }
                }
            }
        }

        public async Task<bool> TextToAudio(string option, string phrase, string title, float octave, float speed, string effect, float volume, float temperature,string savePath, string apikey, int position)
        {
            if (apikey == null)
            {
                UnityEngine.Debug.LogError("Please contact the support at contact@xandimmersion.com");
                return true;
            }

            if (phrase == null || phrase == "")
            {
                UnityEngine.Debug.LogError($"Text {position + 1} is empty. It will be ignored");
                return true;
            }
            www = null;
            WWWForm form = new WWWForm();
            form.AddField("sentence", phrase);
            s_octave = octave.ToString();
            form.AddField("octave", s_octave.Replace(',', '.'));
            s_speed = speed.ToString();
            form.AddField("speed", s_speed.Replace(',', '.'));
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
                info = JsonUtility.FromJson<ArielSynthese>(www.downloadHandler.text);
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
                    AudioClip son_pilou = DownloadHandlerAudioClip.GetContent(www_audio);
                    //UnityEngine.Debug.Log(son_pilou.length);
                    if (title == null || title == "")
                    {
                        UnityEngine.Debug.Log($"Text {position} no file name : file will be saved at untitled(X).wav");
                        title = "untitled";
                    }
                    //UnityEngine.Debug.Log("Audio Generation In Progress.");
                    string tempName = $"{savePath}/{title}.wav";

                    //UnityEngine.Debug.Log("tempName: " + tempName);

                    tempName = getNextFileName(tempName);
                    //UnityEngine.Debug.Log("Name: " + tempName);

                    ArielVoiceGenSav.ArielSavWav.Save($"{tempName}", son_pilou);

                }
            }

            AssetDatabase.Refresh();
            info = null;
            return true;
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
                info = JsonUtility.FromJson<ArielSynthese>(www.downloadHandler.text);
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

        private string getNextFileName(string fileName)
        {
            string extension = Path.GetExtension(fileName);
            //UnityEngine.Debug.Log("extension: " + extension);
            int i = 0;
            //We loop until we create a filename that doesnt exist yet
            while (File.Exists(fileName))
            {
                if (i == 0)
                    fileName = fileName.Replace(extension, "(" + ++i + ")" + extension);
                else
                    fileName = fileName.Replace("(" + i + ")" + extension, "(" + ++i + ")" + extension);
                //UnityEngine.Debug.Log("i: " + i);

            }

            return fileName;
        }
    }
}
