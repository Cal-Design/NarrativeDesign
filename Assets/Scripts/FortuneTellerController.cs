using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.Controls;
#endif

/// <summary>
/// Drives an interactive fortune-teller conversation using a local LLM and ElevenLabs TTS.
/// Instantiates a lightweight UI if none is provided.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class FortuneTellerController : MonoBehaviour
{
    private const string ConfigResourcePath = "LLMConfig";
    private const string FormatInstructions =
        "Respond ONLY with valid JSON. Format: {\"spoken\":\"...\",\"score\":<0-100>,\"insults\":true|false}. " +
        "\"spoken\" must contain only the words you will say aloud (no descriptive actions, no labels). " +
        "\"score\" must be an integer between 0 and 100 assessing the quality of the player's sentence (preferably expressed as a number). " +
        "\"insults\" must be a boolean set to true if the player's sentence contains insults. Do not include any extra text outside the JSON.";
    private const string RetryReminder =
        "That response was not valid JSON. Reply again using ONLY the schema {\"spoken\":\"...\",\"score\":<0-100>,\"insults\":true|false}.";
    private const int MaxRetries = 3;

    [SerializeField] private LLMConfig config;
    [SerializeField] private Text dialogueText;
    [SerializeField] private InputField playerInputField;
    [SerializeField] private Button submitButton;
    [SerializeField] private Animator talkingAnimator;
    [SerializeField] private string talkingParameter = "IsTalking";
    [SerializeField] private bool logResponses = false;
    [SerializeField] private KeyCode skipKey = KeyCode.Space;

    private readonly List<ChatMessage> _conversation = new();
    private readonly Regex _actionMarkupRegex = new(@"\*[^*]+\*|\[[^\]]+\]|\([^\)]+\)", RegexOptions.Compiled);

    private AudioSource _audioSource;
    private AudioSource _musicSource;
    private GameObject _uiRoot;
    private bool _uiHiddenByConfig;
    private string _voiceId = string.Empty;
    private string _apiKey = string.Empty;
    private bool _requestInFlight;
    private bool _awaitingIntroChoice;
    private GameObject _inputContainer;
    private int _talkingParameterHash = -1;

    private void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
        }
        _audioSource.playOnAwake = false;
        _audioSource.spatialize = false;

        EnsureMusicSource();

        EnsureUI();
        HookupInput();
    }

    private void Start()
    {
        if (config == null)
        {
            config = Resources.Load<LLMConfig>(ConfigResourcePath);
        }

        if (config == null)
        {
            Debug.LogError($"FortuneTellerController: Could not find LLMConfig at Resources/{ConfigResourcePath}.");
            return;
        }

        dialogueText.text = "The fortune teller prepares to speak...";
        SetInputInteractable(false);
        SetInputVisible(false);

        _voiceId = config.HasVoiceId ? config.elevenLabsVoiceId : Environment.GetEnvironmentVariable("ELEVENLABS_VOICE_ID");
        _apiKey = config.HasApiKey ? config.elevenLabsApiKey : Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");
        logResponses = config.logTranscript;

        if (talkingAnimator == null && !string.IsNullOrEmpty(config.skeletonAnimatorName))
        {
            var target = GameObject.Find(config.skeletonAnimatorName);
            if (target != null)
            {
                talkingAnimator = target.GetComponent<Animator>() ?? target.GetComponentInChildren<Animator>();
            }
            else if (logResponses)
            {
                Debug.LogWarning($"FortuneTellerController: Could not find GameObject '{config.skeletonAnimatorName}' to drive talking animation.");
            }
        }

        if (!string.IsNullOrEmpty(config.talkingParameter))
        {
            talkingParameter = config.talkingParameter;
        }

        if (talkingAnimator != null)
        {
            _talkingParameterHash = Animator.StringToHash(talkingParameter);
            SetTalkingState(false);
        }
        else if (!string.IsNullOrEmpty(config.skeletonAnimatorName))
        {
            Debug.LogWarning("FortuneTellerController: No Animator found for skeleton talking control.");
        }

        if (string.IsNullOrWhiteSpace(_voiceId))
        {
            Debug.LogWarning("FortuneTellerController: ElevenLabs voice ID not set (config or ELEVENLABS_VOICE_ID). Input will be shown but audio will be silent.");
        }

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            Debug.LogWarning("FortuneTellerController: ElevenLabs API key not set (config or ELEVENLABS_API_KEY). Input will be shown but audio will be silent.");
        }

        _conversation.Clear();
        var basePrompt = string.IsNullOrWhiteSpace(config.systemPrompt) ? string.Empty : config.systemPrompt.Trim();
        _conversation.Add(new ChatMessage
        {
            role = "system",
            content = string.IsNullOrWhiteSpace(basePrompt) ? FormatInstructions : $"{basePrompt}\n\n{FormatInstructions}"
        });

        ConfigureBackgroundMusic();
        ApplyUiVisibility(config != null && !config.hideDialogueUI);

        StartCoroutine(RunStartupSequence());
    }

    private void EnsureUI()
    {
        if (dialogueText != null && playerInputField != null && submitButton != null)
        {
            CacheUiRoot();
            if (EventSystem.current == null)
            {
                CreateEventSystem();
            }
            return;
        }

        if (EventSystem.current == null)
        {
            CreateEventSystem();
        }

        var canvasGo = new GameObject("FortuneTellerCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        DontDestroyOnLoad(canvasGo);
        _uiRoot = canvasGo;

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var panel = CreateRectTransform("DialoguePanel", canvas.transform);
        var panelRect = panel;
        panelRect.anchorMin = new Vector2(0.05f, 0.05f);
        panelRect.anchorMax = new Vector2(0.95f, 0.35f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        var panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.color = new Color(0.05f, 0.05f, 0.05f, 0.8f);

        var panelLayout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(20, 20, 20, 20);
        panelLayout.spacing = 16f;
        panelLayout.childAlignment = TextAnchor.UpperLeft;
        panelLayout.childControlHeight = true;
        panelLayout.childControlWidth = true;
        panelLayout.childForceExpandHeight = false;
        panelLayout.childForceExpandWidth = true;

        var textGo = CreateRectTransform("DialogueText", panel);
        dialogueText = textGo.gameObject.AddComponent<Text>();
        dialogueText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        dialogueText.fontSize = 26;
        dialogueText.color = new Color(0.92f, 0.87f, 0.76f);
        dialogueText.alignment = TextAnchor.UpperLeft;
        dialogueText.horizontalOverflow = HorizontalWrapMode.Wrap;
        dialogueText.verticalOverflow = VerticalWrapMode.Truncate;
        var textLayout = textGo.gameObject.AddComponent<LayoutElement>();
        textLayout.minHeight = 120f;
        textLayout.preferredHeight = 160f;
        textLayout.flexibleHeight = 1f;

        var inputContainer = CreateRectTransform("InputContainer", panel);
        _inputContainer = inputContainer.gameObject;
        var inputLayout = inputContainer.gameObject.AddComponent<HorizontalLayoutGroup>();
        inputLayout.spacing = 8f;
        inputLayout.childAlignment = TextAnchor.MiddleCenter;

        var inputGo = CreateRectTransform("PlayerInput", inputContainer);
        playerInputField = inputGo.gameObject.AddComponent<InputField>();
        var inputImage = inputGo.gameObject.AddComponent<Image>();
        inputImage.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);
        playerInputField.targetGraphic = inputImage;
        playerInputField.textComponent = CreateTextForInput(inputGo.transform, 18, "");

        var submitGo = CreateRectTransform("SubmitButton", inputContainer);
        var submitImage = submitGo.gameObject.AddComponent<Image>();
        submitImage.color = new Color(0.23f, 0.18f, 0.33f, 0.95f);
        submitButton = submitGo.gameObject.AddComponent<Button>();
        submitButton.targetGraphic = submitImage;
        var subLabel = CreateTextForInput(submitGo.transform, 18, "Send");
        var subLayout = submitGo.gameObject.AddComponent<LayoutElement>();
        subLayout.minWidth = 120f;

        SetInputVisible(false);
        CacheUiRoot();
    }

    private Text CreateTextForInput(Transform parent, int size, string text)
    {
        var labelGo = new GameObject("Text", typeof(RectTransform));
        var rect = labelGo.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0, 0);
        rect.anchorMax = new Vector2(1, 1);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var label = labelGo.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = size;
        label.color = new Color(0.95f, 0.94f, 0.9f);
        label.alignment = TextAnchor.MiddleCenter;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        label.text = text;
        return label;
    }

    private void CreateEventSystem()
    {
        var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        es.AddComponent<StandaloneInputModule>();
#endif
        DontDestroyOnLoad(es);
    }

    private static RectTransform CreateRectTransform(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0, 0);
        rect.anchorMax = new Vector2(1, 1);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }

    private void HookupButtons()
    {
        // legacy - replaced by input hookup
    }

    private void HookupInput()
    {
        if (submitButton != null)
        {
            submitButton.onClick.RemoveAllListeners();
            submitButton.onClick.AddListener(OnSubmitButtonClicked);
        }

        if (playerInputField != null)
        {
            playerInputField.onEndEdit.RemoveAllListeners();
            playerInputField.onEndEdit.AddListener(OnInputEndEdit);
        }
    }

    private void OnSubmitButtonClicked()
    {
        if (playerInputField == null)
        {
            return;
        }
        OnSubmitInput(playerInputField.text);
    }

    private void OnInputEndEdit(string text)
    {
        if (WasSubmitKeyPressed())
        {
            OnSubmitInput(text);
        }
    }

    private bool WasSubmitKeyPressed()
    {
        return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
    }

    private void OnSubmitInput(string text)
    {
        if (_requestInFlight || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var trimmed = text.Trim();
        int playerScore = CalculatePlayerInputScore(trimmed);
        bool playerHasInsults = DetectInsults(trimmed);

        string scoreDisplay = $"Score: {playerScore} | Insults: {playerHasInsults}";
        dialogueText.text = $"You: {trimmed}\n\n{scoreDisplay}\n\nThe fortune teller contemplates...";
        SetInputInteractable(false);
        AppendUserLine(trimmed);
        playerInputField.text = string.Empty;

        if (_awaitingIntroChoice)
        {
            _awaitingIntroChoice = false;
            string initialPrompt = string.IsNullOrWhiteSpace(config.userPrompt)
                ? $"The visitor says: \"{trimmed}\" (Quality score: {playerScore}/100, Contains insults: {playerHasInsults}). Continue the reading with eerie insight."
                : $"{config.userPrompt}\nThe visitor says: \"{trimmed}\" (Quality score: {playerScore}/100, Contains insults: {playerHasInsults}).";

            StartCoroutine(AdvanceConversation(initialPrompt));
            return;
        }

        StartCoroutine(AdvanceConversation($"The visitor says: \"{trimmed}\" (Quality score: {playerScore}/100, Contains insults: {playerHasInsults}). React in character."));
    }

    private IEnumerator RunStartupSequence()
    {
        _awaitingIntroChoice = false;
        if (config.introClip != null || !string.IsNullOrWhiteSpace(config.introSpoken))
        {
            yield return PlayIntroSequence(null);
        }

        if (HasIntroQuestion())
        {
            SetInputVisible(true);
            if (config.introQuestionClip != null)
            {
                bool questionRecorded = false;
                yield return PlayClipWithText(
                    config.introQuestionClip,
                    config.introQuestion,
                    null,
                    completedText =>
                    {
                        if (!string.IsNullOrWhiteSpace(completedText))
                        {
                            AppendAssistantLine(completedText);
                            questionRecorded = true;
                        }
                    });
                if (!questionRecorded && !string.IsNullOrWhiteSpace(config.introQuestion))
                {
                    AppendAssistantLine(config.introQuestion);
                }
            }
            else if (!string.IsNullOrWhiteSpace(config.introQuestion))
            {
                dialogueText.text = config.introQuestion;
                AppendAssistantLine(config.introQuestion);
            }
            else
            {
                dialogueText.text = string.Empty;
            }

            SetInputInteractable(true);
            _awaitingIntroChoice = true;
            yield break;
        }

        StartCoroutine(AdvanceConversation(config.userPrompt));
    }

    private IEnumerator AdvanceConversation(string userMessage)
    {
        if (_requestInFlight)
        {
            yield break;
        }

        _requestInFlight = true;

        if (!string.IsNullOrWhiteSpace(userMessage))
        {
            _conversation.Add(new ChatMessage { role = "user", content = userMessage });
        }

        SetInputVisible(false);

        int attempt = 0;
        FortuneResponse parsedResponse = null;
        string parsedJson = null;
        string spoken = null;

        while (attempt < MaxRetries)
        {
            using (var llmRequest = BuildChatRequest())
            {
                yield return llmRequest.SendWebRequest();

                if (llmRequest.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"FortuneTellerController: LLM request failed: {llmRequest.error}");
                    _requestInFlight = false;
                    SetTalkingState(false);
                    SetInputVisible(true);
                    SetInputInteractable(true);
                    yield break;
                }

                var raw = llmRequest.downloadHandler.text;
                if (logResponses)
                {
                    Debug.Log($"LLM raw response (attempt {attempt + 1}): {raw}");
                }

                if (TryParseResponse(raw, out parsedResponse, out parsedJson))
                {
                    spoken = SanitizeTranscript(parsedResponse.spoken);
                    break;
                }

                attempt++;
                if (attempt < MaxRetries)
                {
                    _conversation.Add(new ChatMessage { role = "user", content = RetryReminder });
                    continue;
                }

                Debug.LogError("FortuneTellerController: Failed to parse LLM response after multiple attempts.");
                _requestInFlight = false;
                SetTalkingState(false);
                SetInputVisible(true);
                SetInputInteractable(true);
                yield break;
            }
        }

        _conversation.Add(new ChatMessage { role = "assistant", content = parsedJson });

        if (string.IsNullOrWhiteSpace(spoken))
        {
            Debug.LogWarning("FortuneTellerController: Received empty spoken text.");
            dialogueText.text = string.Empty;
        }
        else
        {
            dialogueText.text = string.Empty;
            yield return PlaySpeech(spoken);
        }

        ApplyResponseMeta(parsedResponse);
        _requestInFlight = false;
    }

    private void ApplyResponseMeta(FortuneResponse parsedResponse)
    {
        if (parsedResponse == null)
        {
            SetInputVisible(true);
            SetInputInteractable(true);
            return;
        }

        // Show input again
        SetInputVisible(true);
        SetInputInteractable(true);
        // Display only the LLM's spoken response (score/insults were already shown for player input)
        if (!string.IsNullOrWhiteSpace(parsedResponse.spoken) && dialogueText != null)
        {
            dialogueText.text = parsedResponse.spoken;
        }
    }

    // Choices UI removed — input field is used instead.

    private bool HasIntroQuestion()
    {
        if (string.IsNullOrWhiteSpace(config.introQuestion) || config.introChoices == null)
        {
            return false;
        }

        int nonEmpty = 0;
        foreach (var choice in config.introChoices)
        {
            if (!string.IsNullOrWhiteSpace(choice))
            {
                nonEmpty++;
            }
        }

        return nonEmpty > 0;
    }

    private IEnumerator PlayIntroSequence(System.Action onSkipped)
    {
        if (config.introClip != null)
        {
            bool introRecorded = false;
            yield return PlayClipWithText(config.introClip, config.introSpoken, onSkipped, completedText =>
            {
                if (!string.IsNullOrWhiteSpace(completedText))
                {
                    AppendAssistantLine(completedText);
                    introRecorded = true;
                }
            });
            if (!introRecorded && !string.IsNullOrWhiteSpace(config.introSpoken))
            {
                AppendAssistantLine(config.introSpoken);
            }
        }
        else if (!string.IsNullOrWhiteSpace(config.introSpoken))
        {
            dialogueText.text = config.introSpoken;
            float duration = Mathf.Clamp(config.introSpoken.Length * 0.05f, 1f, 5f);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (WasSkipPressed())
                {
                    break;
                }
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            AppendAssistantLine(config.introSpoken);
        }
    }

    private IEnumerator PlayClipWithText(AudioClip clip, string textOverride, System.Action onSkipped = null, System.Action<string> onCompleted = null)
    {
        if (clip == null)
        {
            yield break;
        }

        if (config != null)
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, config.startDelay));
        }

        if (!string.IsNullOrWhiteSpace(textOverride))
        {
            dialogueText.text = textOverride;
        }

        _audioSource.clip = clip;
        SetTalkingState(true);
        _audioSource.Play();
        bool skipped = false;
        while (_audioSource.isPlaying)
        {
            if (WasSkipPressed())
            {
                _audioSource.Stop();
                skipped = true;
                onSkipped?.Invoke();
                break;
            }
            yield return null;
        }
        SetTalkingState(false);
        _audioSource.clip = null;

        if (!skipped && onCompleted != null)
        {
            onCompleted.Invoke(string.IsNullOrWhiteSpace(textOverride) ? string.Empty : textOverride);
        }
    }

    private IEnumerator PlaySpeech(string spoken)
    {
        if (string.IsNullOrWhiteSpace(spoken))
        {
            SetTalkingState(false);
            dialogueText.text = spoken;
            yield break;
        }

        if (string.IsNullOrWhiteSpace(_apiKey) || string.IsNullOrWhiteSpace(_voiceId))
        {
            SetTalkingState(false);
            dialogueText.text = spoken;
            yield break;
        }

        using (var ttsRequest = BuildTtsRequest(spoken))
        {
            yield return ttsRequest.SendWebRequest();

            if (ttsRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"FortuneTellerController: ElevenLabs TTS request failed: {ttsRequest.error}\n{TryGetResponseText(ttsRequest)}");
                SetTalkingState(false);
                dialogueText.text = spoken;
                yield break;
            }

            var clip = DownloadHandlerAudioClip.GetContent(ttsRequest);
            if (clip == null || clip.length <= 0f)
            {
                Debug.LogError($"FortuneTellerController: Received empty audio clip. Content-Type={ttsRequest.GetResponseHeader("Content-Type")}\n{TryGetResponseText(ttsRequest)}");
                SetTalkingState(false);
                dialogueText.text = spoken;
                yield break;
            }

            _audioSource.clip = clip;
            dialogueText.text = spoken;
            SetTalkingState(true);
            _audioSource.Play();
            while (_audioSource.isPlaying)
            {
                yield return null;
            }
            SetTalkingState(false);
        }
    }

    private UnityWebRequest BuildChatRequest()
    {
        var payload = new ChatRequest
        {
            model = config.llmModel,
            messages = _conversation.ToArray()
        };

        string json = JsonUtility.ToJson(payload);
        byte[] body = Encoding.UTF8.GetBytes(json);
        var request = new UnityWebRequest(config.llmEndpoint, UnityWebRequest.kHttpVerbPOST);
        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        return request;
    }

    private UnityWebRequest BuildTtsRequest(string text)
    {
        var payload = new ElevenLabsRequest
        {
            text = text,
            model_id = config.elevenLabsModelId,
            voice_settings = new ElevenLabsVoiceSettings
            {
                stability = GetSnappedStability(config.elevenLabsStability),
                similarity_boost = Mathf.Clamp01(config.elevenLabsSimilarityBoost)
            }
        };

        string json = JsonUtility.ToJson(payload);
        byte[] body = Encoding.UTF8.GetBytes(json);
        string uri = $"https://api.elevenlabs.io/v1/text-to-speech/{_voiceId}";

        var request = new UnityWebRequest(uri, UnityWebRequest.kHttpVerbPOST);
        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerAudioClip(uri, AudioType.MPEG);
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Accept", "audio/mpeg");
        request.SetRequestHeader("xi-api-key", _apiKey);
        return request;
    }

    private static float GetSnappedStability(float value)
    {
        float[] allowed = { 0f, 0.5f, 1f };
        float clamped = Mathf.Clamp01(value);
        float closest = allowed[0];
        float bestDistance = Mathf.Abs(clamped - allowed[0]);
        for (int i = 1; i < allowed.Length; i++)
        {
            float distance = Mathf.Abs(clamped - allowed[i]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                closest = allowed[i];
            }
        }
        return closest;
    }

    private string SanitizeTranscript(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var trimmed = raw.Trim();
        var withoutActions = _actionMarkupRegex.Replace(trimmed, string.Empty);
        return withoutActions.Replace("  ", " ").Trim();
    }

    private static string TryGetResponseText(UnityWebRequest request)
    {
        try
        {
            if (request.downloadHandler != null)
            {
                var data = request.downloadHandler.data;
                if (data != null && data.Length > 0)
                {
                    return Encoding.UTF8.GetString(data);
                }
            }
        }
        catch (Exception ex)
        {
            return $"(failed to read error body: {ex.Message})";
        }

        return string.Empty;
    }

    private bool TryParseResponse(string raw, out FortuneResponse response, out string parsedJson)
    {
        response = null;
        parsedJson = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string json = string.Empty;
        try
        {
            string content = null;
            try
            {
                var completion = JsonUtility.FromJson<ChatCompletionResponse>(raw);
                if (completion != null && completion.choices != null && completion.choices.Length > 0)
                {
                    content = completion.choices[0]?.message?.content;
                }
            }
            catch (Exception jsonUtilityEx)
            {
                if (logResponses)
                {
                    Debug.LogWarning($"FortuneTellerController: JsonUtility parse failed: {jsonUtilityEx.Message}");
                }
            }

            if (string.IsNullOrEmpty(content))
            {
                var top = MiniJson.Deserialize(raw) as Dictionary<string, object>;
                if (top == null || !top.TryGetValue("choices", out var choicesObjTop) || choicesObjTop is not IList choicesList || choicesList.Count == 0)
                {
                    if (logResponses)
                    {
                        Debug.LogWarning($"FortuneTellerController: top-level JSON missing choices array:\n{raw}");
                    }
                    return false;
                }

                if (choicesList[0] is not Dictionary<string, object> firstChoice ||
                    !firstChoice.TryGetValue("message", out var messageObj) ||
                    messageObj is not Dictionary<string, object> messageDict ||
                    !messageDict.TryGetValue("content", out var contentObj) ||
                    contentObj is not string extractedContent)
                {
                    if (logResponses)
                    {
                        Debug.LogWarning($"FortuneTellerController: could not find message.content in response:\n{raw}");
                    }
                    return false;
                }

                content = extractedContent;
            }

            json = ExtractJsonBlock(content);
            if (string.IsNullOrEmpty(json))
            {
                if (TryParseEnumeratedContent(content, out var fallbackResponse, out var synthesizedJson))
                {
                    response = fallbackResponse;
                    parsedJson = synthesizedJson;
                    return true;
                }

                if (logResponses)
                {
                    Debug.LogWarning($"FortuneTellerController: Could not locate JSON in assistant content:\n{content}");
                }
                return false;
            }
            parsedJson = json;

            // Primary parse via JsonUtility into expected schema
            FortuneJson fortune = null;
            try
            {
                fortune = JsonUtility.FromJson<FortuneJson>(json);
            }
            catch (Exception)
            {
                fortune = null;
            }

            if (fortune != null && !string.IsNullOrWhiteSpace(fortune.spoken))
            {
                response = new FortuneResponse
                {
                    spoken = fortune.spoken,
                    score = fortune.score,
                    insults = fortune.insults
                };
                return true;
            }

            // Fallback to MiniJson to handle odd formatting
            var obj = MiniJson.Deserialize(json) as Dictionary<string, object>;
            if (obj == null)
            {
                if (logResponses)
                {
                    Debug.LogWarning($"FortuneTellerController: Parsed JSON is not an object:\n{json}");
                }
                return false;
            }

            if (!obj.TryGetValue("spoken", out var spokenObj))
            {
                if (logResponses)
                {
                    Debug.LogWarning($"FortuneTellerController: JSON missing 'spoken' field:\n{json}");
                }
                return false;
            }

            var spoken = SanitizeTranscript(spokenObj as string);

            int score = 50;
            if (obj.TryGetValue("score", out var scoreObj))
            {
                try
                {
                    if (scoreObj is long l) score = (int)l;
                    else if (scoreObj is double d) score = (int)d;
                    else if (scoreObj is string s && int.TryParse(s, out var si)) score = si;
                }
                catch { score = 50; }
            }

            bool insults = false;
            if (obj.TryGetValue("insults", out var insultsObj))
            {
                try
                {
                    if (insultsObj is bool b) insults = b;
                    else if (insultsObj is string ss && bool.TryParse(ss, out var bs)) insults = bs;
                }
                catch { insults = false; }
            }

            response = new FortuneResponse
            {
                spoken = spoken,
                score = Mathf.Clamp(score, 0, 100),
                insults = insults
            };
            if (string.IsNullOrWhiteSpace(spoken))
            {
                if (logResponses)
                {
                    Debug.LogWarning($"FortuneTellerController: 'spoken' field empty after sanitization:\n{json}");
                }
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"FortuneTellerController: JSON parse failed: {ex.Message}\n{json}");
            parsedJson = json;
            return false;
        }
    }

    private string ExtractJsonBlock(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        int depth = 0;
        int start = -1;
        var inString = false;
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c == '"' && (i == 0 || raw[i - 1] != '\\'))
            {
                inString = !inString;
            }

            if (inString)
            {
                continue;
            }

            if (c == '{')
            {
                if (depth == 0)
                {
                    start = i;
                }
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0 && start != -1)
                {
                    int length = i - start + 1;
                    return raw.Substring(start, length);
                }
            }
        }

        return string.Empty;
    }

    private bool TryParseEnumeratedContent(string content, out FortuneResponse response, out string synthesizedJson)
    {
        response = null;
        synthesizedJson = string.Empty;

        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        // Fallback: treat the content as spoken text, synthesize a neutral score and detect insults heuristically.
        var spoken = SanitizeTranscript(content);
        if (string.IsNullOrWhiteSpace(spoken))
        {
            return false;
        }

        int score = 50;
        bool insults = DetectInsults(spoken);

        response = new FortuneResponse
        {
            spoken = spoken,
            score = score,
            insults = insults
        };

        synthesizedJson = BuildJsonPayload(response.spoken, response.score, response.insults);
        return true;
    }

    private string BuildJsonPayload(string spoken, int score, bool insults)
    {
        var sb = new StringBuilder();
        sb.Append("{\"spoken\":\"");
        sb.Append(EscapeForJson(spoken));
        sb.Append("\",\"score\":");
        sb.Append(score);
        sb.Append(",\"insults\":");
        sb.Append(insults ? "true" : "false");
        sb.Append('}');
        return sb.ToString();
    }

    private static string EscapeForJson(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");
    }

    private bool DetectInsults(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var lower = text.ToLowerInvariant();
        string[] badWords = new[] { "merde", "connard", "putain", "salope", "con", "pute", "encul", "fdp", "shit", "fuck", "idiot", "stupide" };
        foreach (var w in badWords)
        {
            if (lower.Contains(w)) return true;
        }
        return false;
    }

    private int CalculatePlayerInputScore(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        int score = 50; // baseline

        // Bonus for length (more thought = higher quality)
        if (text.Length > 50) score += 15;
        else if (text.Length > 30) score += 10;
        else if (text.Length < 5) score -= 20;

        // Bonus for French words (since this is targeted at French players)
        var lowerText = text.ToLowerInvariant();
        string[] frenchMarkers = new[] { "je ", "tu ", "il ", "elle ", "nous ", "vous ", "ils ", "elles ", "et ", "mais ", "donc ", "parce que ", "pourquoi", "comment", "quoi", "qui" };
        int frenchCount = 0;
        foreach (var marker in frenchMarkers)
        {
            if (lowerText.Contains(marker)) frenchCount++;
        }
        if (frenchCount >= 3) score += 20;
        else if (frenchCount >= 1) score += 10;

        // Penalty for insults
        if (DetectInsults(text)) score -= 30;

        // Penalty for excessive punctuation
        int punctCount = 0;
        foreach (char c in text)
        {
            if (c == '!' || c == '?') punctCount++;
        }
        if (punctCount > 3) score -= 10;

        // Clamp to 0-100
        return Mathf.Clamp(score, 0, 100);
    }

    private bool WasSkipPressed()
    {
#if ENABLE_LEGACY_INPUT_MANAGER || !ENABLE_INPUT_SYSTEM
        if (Input.GetKeyDown(skipKey))
        {
            return true;
        }
        return false;
#elif ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return false;
        }

        switch (skipKey)
        {
            case KeyCode.Space:
                return keyboard.spaceKey.wasPressedThisFrame;
            case KeyCode.Return:
                return keyboard.enterKey.wasPressedThisFrame;
            case KeyCode.KeypadEnter:
                return keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;
            case KeyCode.Escape:
                return keyboard.escapeKey.wasPressedThisFrame;
            default:
                var mappedKey = TryMapKey(skipKey, keyboard);
                return mappedKey != null && mappedKey.wasPressedThisFrame;
        }
#else
        return false;
#endif
    }

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
    private static KeyControl TryMapKey(KeyCode keyCode, Keyboard keyboard)
    {
        // Handles common alphanumeric keys when using the new Input System only.
        if (keyCode >= KeyCode.A && keyCode <= KeyCode.Z)
        {
            int offset = keyCode - KeyCode.A;
            return keyboard[(Key)((int)Key.A + offset)];
        }

        if (keyCode >= KeyCode.Alpha0 && keyCode <= KeyCode.Alpha9)
        {
            int offset = keyCode - KeyCode.Alpha0;
            return keyboard[(Key)((int)Key.Digit0 + offset)];
        }

        return null;
    }
#endif

    private void OnDestroy()
    {
        if (_musicSource != null && _musicSource.isPlaying)
        {
            _musicSource.Stop();
        }
    }

    private void SetTalkingState(bool talking)
    {
        if (talkingAnimator == null || _talkingParameterHash == -1)
        {
            return;
        }

        try
        {
            talkingAnimator.SetBool(_talkingParameterHash, talking);
        }
        catch (Exception ex)
        {
            if (logResponses)
            {
                Debug.LogWarning($"FortuneTellerController: Failed to set talking parameter: {ex.Message}");
            }
        }
    }

    private void SetInputVisible(bool visible)
    {
        if (_inputContainer != null)
        {
            _inputContainer.SetActive(!_uiHiddenByConfig && visible);
        }
    }

    private void SetInputInteractable(bool value)
    {
        if (playerInputField != null)
        {
            playerInputField.interactable = value && !_uiHiddenByConfig;
        }
        if (submitButton != null)
        {
            submitButton.interactable = value && !_uiHiddenByConfig;
        }
    }

    private void ApplyUiVisibility(bool visible)
    {
        _uiHiddenByConfig = !visible;

        if (_uiRoot != null && _uiRoot != gameObject)
        {
            if (_uiRoot.activeSelf != visible)
            {
                _uiRoot.SetActive(visible);
            }
        }
        else
        {
            if (dialogueText != null)
            {
                dialogueText.gameObject.SetActive(visible);
            }
            if (_inputContainer != null)
            {
                _inputContainer.SetActive(visible);
            }
        }

        if (!visible)
        {
            SetInputInteractable(false);
        }
        else if (_inputContainer != null)
        {
            _inputContainer.SetActive(true);
        }
    }

    private void CacheUiRoot()
    {
        if (_uiRoot != null)
        {
            return;
        }

        if (dialogueText != null)
        {
            var canvas = dialogueText.GetComponentInParent<Canvas>(true);
            if (canvas != null)
            {
                _uiRoot = canvas.gameObject;
                return;
            }

            _uiRoot = dialogueText.gameObject;
            return;
        }

        if (_inputContainer != null)
        {
            _uiRoot = _inputContainer;
        }
    }

    private void EnsureMusicSource()
    {
        if (_musicSource != null)
        {
            return;
        }

        var existingSources = GetComponents<AudioSource>();
        foreach (var source in existingSources)
        {
            if (source != null && source != _audioSource)
            {
                _musicSource = source;
                break;
            }
        }

        if (_musicSource == null)
        {
            _musicSource = gameObject.AddComponent<AudioSource>();
        }

        _musicSource.playOnAwake = false;
        _musicSource.loop = true;
        _musicSource.spatialize = false;
        _musicSource.volume = 0.35f;
        _musicSource.priority = Mathf.Clamp((_audioSource != null ? _audioSource.priority + 1 : 129), 0, 256);
    }

    private void ConfigureBackgroundMusic()
    {
        EnsureMusicSource();
        if (_musicSource == null)
        {
            return;
        }

        if (config != null && config.HasBackgroundMusic)
        {
            var desiredVolume = Mathf.Clamp01(config.backgroundMusicVolume);
            var shouldRestart = _musicSource.clip != config.backgroundMusic;

            _musicSource.clip = config.backgroundMusic;
            _musicSource.volume = desiredVolume;
            _musicSource.loop = config.backgroundMusicLoop;

            if (config.playBackgroundMusicOnStart)
            {
                if (shouldRestart && _musicSource.isPlaying)
                {
                    _musicSource.Stop();
                }

                if (!_musicSource.isPlaying && _musicSource.clip != null)
                {
                    _musicSource.Play();
                }
            }
            else if (shouldRestart && _musicSource.isPlaying)
            {
                _musicSource.Stop();
            }
        }
        else
        {
            if (_musicSource.isPlaying)
            {
                _musicSource.Stop();
            }
            _musicSource.clip = null;
        }
    }

    public void PlayBackgroundMusic(AudioClip clip = null, bool loop = true, float volume = -1f)
    {
        EnsureMusicSource();
        if (_musicSource == null)
        {
            return;
        }

        if (clip != null && _musicSource.clip != clip)
        {
            if (_musicSource.isPlaying)
            {
                _musicSource.Stop();
            }
            _musicSource.clip = clip;
        }

        if (_musicSource.clip == null)
        {
            return;
        }

        _musicSource.loop = loop;
        if (volume >= 0f)
        {
            _musicSource.volume = Mathf.Clamp01(volume);
        }

        if (!_musicSource.isPlaying)
        {
            _musicSource.Play();
        }
    }

    public void StopBackgroundMusic()
    {
        if (_musicSource != null && _musicSource.isPlaying)
        {
            _musicSource.Stop();
        }
    }

    private void AppendAssistantLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _conversation.Add(new ChatMessage
        {
            role = "assistant",
            content = text.Trim()
        });
    }

    private void AppendUserLine(string choice)
    {
        if (string.IsNullOrWhiteSpace(choice))
        {
            return;
        }

        _conversation.Add(new ChatMessage
        {
            role = "user",
            content = choice.Trim()
        });
    }

    [Serializable]
    private class ChatRequest
    {
        public string model;
        public ChatMessage[] messages;
    }

    [Serializable]
    private class ChatMessage
    {
        public string role;
        public string content;
    }

    [Serializable]
    private class FortuneResponse
    {
        public string spoken;
        public int score;
        public bool insults;
    }

    [Serializable]
    private class ChatCompletionResponse
    {
        public Choice[] choices;
    }

    [Serializable]
    private class Choice
    {
        public Message message;
    }

    [Serializable]
    private class Message
    {
        public string content;
    }

    [Serializable]
    private class FortuneJson
    {
        public string spoken;
        public int score;
        public bool insults;
    }

    [Serializable]
    private class ElevenLabsRequest
    {
        public string text;
        public string model_id;
        public ElevenLabsVoiceSettings voice_settings;
    }

    [Serializable]
    private class ElevenLabsVoiceSettings
    {
        public float stability;
        public float similarity_boost;
    }
}
