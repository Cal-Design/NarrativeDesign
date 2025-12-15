using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using ArielCommonTypes;



namespace ArielCommonTypes
{
    public class ArielTts
    {
        public string Phrase;

        public float Octave;

        public float Speed;

        public string Title;

        public float Temperature;

        public int Selected_O = 0;
        public string formattedSpeakerList = "new List<string>";

        public ArielTts(string phrase, float octave, float speed, string title, int selected_O,int effect, float volume, float temperature)
        {
            this.Phrase = phrase;
            this.Octave = octave;
            this.Speed = speed;
            this.Title = title;
            this.Selected_O = selected_O;
            this.Effect = effect;
            this.Volume = volume;
            this.Temperature = temperature;
        }

        public string glossaryToUsePath;
        public bool useGlossary = false;

        public int Effect;

        public float Volume;

        public bool AdvancedOptions;
    }

   
    public class ArielSynthese
    {
        public string sentence;
        public string audio;

    }

    public class ArielCsv
    {
        public string Word;

        public string Pronunciation;

        public ArielCsv(string word, string pronunciation)
        {
            this.Word = word;
            this.Pronunciation = pronunciation;
        }
    }

    // Classes to match JSON structure
    [System.Serializable]
    public class Speaker
    {
        public string name;
        public int id;
        public List<string> emotion;
        public string gender;
    }

    public class SpeakerObject
    {
        public Speaker[] speakers;
    }

    [System.Serializable]

    public class SpeakerSettings
    {
        public Speaker[] speakers = new Speaker[0];
    }

    public class ServerState
    {
        public bool portInUse = true;
        public bool arielServerProcessRunning = false;
    }
}