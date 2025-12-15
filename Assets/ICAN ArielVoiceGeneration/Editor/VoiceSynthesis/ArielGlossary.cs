using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;

public class ArielGlossary
{
    // Sentence to generate
    public string sentence;

    // New glossary name
    public string newGlossaryName;
    string filename = "";

    // Glossary to read or edit
    public TextAsset glossary;


    // Glossary List (word, pronounciation)
    [System.Serializable]
    public class Words
    {
        public string word;
        public string pronounciation;
    }

    [System.Serializable]
    public class WordList
    {
        public List<Words> words = new List<Words>();
    }

    public WordList wordList = new WordList();

    
    // Generates new glossary / overwrites existing one
    public void WriteCSV()
    {
        if (wordList.words.Count > 0)
        {
            if (newGlossaryName != null && newGlossaryName != "")
            {
                filename = Application.dataPath + "/" + newGlossaryName + ".csv";

                TextWriter tw = new StreamWriter(filename, false);
                tw.WriteLine("word,pronounciation");
                tw.Close();

                tw = new StreamWriter(filename, true);

                for (int i = 0; i < wordList.words.Count; i++)
                {
                    tw.WriteLine(wordList.words[i].word + "," + wordList.words[i].pronounciation);
                }

                tw.Close();
            }
        }
    }

    // Show current glossary
    void ReadCSV()
    {
        string[] data = glossary.text.Split(new string[] { ",", "\n" }, StringSplitOptions.None);

        int tableSize = data.Length / 2 - 1;

        //Debug.Log("table size is : " + tableSize);

        for (int i = 0; i < tableSize; i++)
        {
            wordList.words.Add(new Words());
            wordList.words[i].word = (data[2 * (i + 1)]);
            wordList.words[i].pronounciation = data[2 * (i + 1) + 1];
        }

        Debug.Log(glossary);
    }

    // Edit current glossary
    public void EditCSV()
    {
        if (wordList.words.Count > 0)
        {
            filename = Application.dataPath + "/" + glossary.name + ".csv";

            TextWriter tw = new StreamWriter(filename, false);
            tw.WriteLine("word, pronounciation");
            tw.Close();

            tw = new StreamWriter(filename, true);

            for (int i = 0; i < wordList.words.Count; i++)
            {
                tw.WriteLine(wordList.words[i].word + "," + wordList.words[i].pronounciation);
            }

            tw.Close();
            
        }
    }

    void CompareSentenceToGlossary()
    {
        string[] data = glossary.text.Split(new string[] { ",", "\n" }, StringSplitOptions.None);

        int tableSize = data.Length / 2 - 1;

        //Debug.Log("table size is : " + tableSize);

        for (int i = 0; i < tableSize; i++)
        {
            sentence = sentence.Replace(data[2 * (i + 1)], data[2 * (i + 1) + 1]);
            //Debug.Log("Data " + i + " is : " + data[2 * (i + 1)]);
        }

        Debug.Log(sentence);
    }
}
