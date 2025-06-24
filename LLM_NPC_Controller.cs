using UnityEngine;
using UnityEngine.UI;
using System.Collections; // Для корутин
using UnityEngine.Networking; // Для UnityWebRequest (HTTP запросы)
using LitJson; // Для парсинга JSON ответов от LLM (требуется LitJson.dll в Assets/Plugins)
using System.Text; // Для работы с кодировками строк

// Интерфейс, который ваш NPC должен реализовать, чтобы принимать команды от LLM
/*public interface ILLMControllableNPC
{
    void MoveToPosition(Vector3 position);
    void MoveToObject(GameObject targetObject);
    void SetBehavior(string behaviorType); // Например, "Patrol", "Idle", "ChasePlayer"
    void SayDialogue(string text);
    // ... другие команды, которые вы хотите, чтобы NPC выполнял
}*/

public class LLM_NPC_Controller : MonoBehaviour
{
    [Header("UI ссылки")]
    [Tooltip("Поле ввода текста для команд NPC.")]
    public InputField commandInputField;
    [Tooltip("Кнопка для отправки команды NPC.")]
    public Button sendCommandButton;
    [Tooltip("Поле для отображения ответов от LLM/статуса.")]
    public Text responseText;

    [Header("Настройки LLM API")]
    [Tooltip("URL вашего LLM API (например, Gemini API).")]
    public string llmApiUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key=";
    [Tooltip("Ваш API-ключ для LLM (не храните в открытом виде в продакшене!).")]
    public string apiKey = ""; // Здесь должен быть ваш API-ключ

    [Header("Ссылки на NPC")]
    [Tooltip("Ссылка на NPC, которым будет управлять LLM. У него должен быть компонент, реализующий ILLMControllableNPC.")]
    public GameObject targetNPCGameObject;
    private ILLMControllableNPC targetNPC;

    void Start()
    {
       /* if (commandInputField == null || sendCommandButton == null || responseText == null)
        {
            Debug.LogError("LLM_NPC_Controller: UI элементы не назначены!");
            enabled = false;
            return;
        }

        if (targetNPCGameObject == null)
        {
            Debug.LogError("LLM_NPC_Controller: Target NPC GameObject не назначен!");
            enabled = false;
            return;
        }

        targetNPC = targetNPCGameObject.GetComponent<ILLMControllableNPC>();
        if (targetNPC == null)
        {
            Debug.LogError($"LLM_NPC_Controller: Target NPC GameObject '{targetNPCGameObject.name}' не реализует интерфейс ILLMControllableNPC!");
            enabled = false;
            return;
        } */

        sendCommandButton.onClick.AddListener(OnSendCommand);
        responseText.text = "Готов к командам...";
    }

    /// <summary>
    /// Вызывается при нажатии кнопки отправки команды.
    /// </summary>
    public void OnSendCommand()
    {
        string commandText = commandInputField.text;
        if (string.IsNullOrWhiteSpace(commandText))
        {
            responseText.text = "Введите команду!";
            return;
        }

        responseText.text = "Отправка команды LLM...";
        StartCoroutine(SendToLLM(commandText)); // Запускаем корутину для отправки запроса
        commandInputField.text = ""; // Очищаем поле ввода
    }

    /// <summary>
    /// Отправляет запрос LLM API и обрабатывает ответ.
    /// </summary>
    /// <param name="userPrompt">Текстовая команда пользователя.</param>
    IEnumerator SendToLLM(string userPrompt)
    {
        // === Шаг 1: Формирование запроса для LLM ===
        // Здесь мы просим LLM ответить в структурированном JSON-формате
        // Это ОЧЕНЬ ВАЖНО, чтобы LLM генерировала парсируемый ответ.
        string llmPrompt = $"You are an AI assistant controlling a game NPC. The user will give you a command for the NPC. " +
                           $"Your task is to convert this command into a JSON object with a 'command' field and relevant 'parameters'. " +
                           $"Possible commands: 'moveToObject' (parameters: objectName:string), 'moveToPosition' (parameters: x:float, y:float, z:float), " +
                           $"'setBehavior' (parameters: behaviorType:string - e.g., 'Patrol', 'Idle', 'ChasePlayer'), " +
                           $"'say' (parameters: text:string). " +
                           $"If the command is unclear, use 'say' and ask for clarification. " +
                           $"Strictly respond ONLY with the JSON object. Do not include any other text.\n" +
                           $"User command: \"{userPrompt}\"";

        // Если вы используете модель, которая поддерживает responseSchema, используйте это вместо текстового промпта:
        /*
        const string payload = JsonMapper.ToJson(new {
            contents = new[] {
                new {
                    role = "user",
                    parts = new[] {
                        new { text = userPrompt }
                    }
                }
            },
            generationConfig = new {
                responseMimeType = "application/json",
                responseSchema = new {
                    type = "OBJECT",
                    properties = new {
                        command = new { type = "STRING" },
                        parameters = new {
                            type = "OBJECT",
                            additionalProperties = true
                        }
                    },
                    required = new[] { "command" }
                }
            }
        });
        */

        // Для gemini-2.0-flash, который мы используем, лучше всего сработает промпт в тексте
        string requestBody = JsonMapper.ToJson(new
        {
            contents = new[] {
                new {
                    role = "user",
                    parts = new[] {
                        new { text = llmPrompt }
                    }
                }
            }
        });

        // Debug.Log($"Sending to LLM: {requestBody}");

        UnityWebRequest request = new UnityWebRequest(llmApiUrl + apiKey, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(requestBody);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest(); // Отправляем запрос и ждем ответа

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"Error sending to LLM: {request.error}");
            responseText.text = $"Ошибка LLM: {request.error}";
        }
        else
        {
            string jsonResponse = request.downloadHandler.text;
            Debug.Log($"Received from LLM: {jsonResponse}");
            ProcessLLMResponse(jsonResponse); // Обрабатываем ответ
        }
    }

    /// <summary>
    /// Разбирает JSON-ответ от LLM и вызывает соответствующие команды на NPC.
    /// </summary>
    /// <param name="jsonResponse">JSON-строка ответа от LLM.</param>
    void ProcessLLMResponse(string jsonResponse)
    {
        try
        {
            // JsonMapper ожидает, что корень JSON будет объектом или массивом.
            // Ответ от Gemini API часто обернут в структуру: { candidates: [ { content: { parts: [ { text: "..." } ] } } ] }
            JsonData rootData = JsonMapper.ToObject(jsonResponse);

            if (rootData == null || !rootData.ContainsKey("candidates") || rootData["candidates"].Count == 0 ||
                !rootData["candidates"][0].ContainsKey("content") || !rootData["candidates"][0]["content"].ContainsKey("parts") ||
                rootData["candidates"][0]["content"]["parts"].Count == 0)
            {
                responseText.text = "Ошибка: Некорректный ответ от LLM.";
                Debug.LogError("LLM response structure invalid: " + jsonResponse);
                return;
            }

            // Извлекаем текст JSON-команды из ответа LLM
            string commandJsonText = rootData["candidates"][0]["content"]["parts"][0]["text"].ToString();
            Debug.Log("Parsed command JSON text: " + commandJsonText);

            // Теперь парсим сам JSON-объект команды
            JsonData commandData = JsonMapper.ToObject(commandJsonText);

            string command = commandData["command"].ToString();
            JsonData parameters = commandData.ContainsKey("parameters") ? commandData["parameters"] : null;

            switch (command)
            {
                case "moveToObject":
                    if (parameters != null && parameters.ContainsKey("objectName"))
                    {
                        string objectName = parameters["objectName"].ToString();
                        GameObject targetObject = GameObject.Find(objectName); // Ищем объект по имени
                        if (targetObject != null)
                        {
                            targetNPC.MoveToObject(targetObject);
                            responseText.text = $"NPC движется к {objectName}.";
                        }
                        else
                        {
                            responseText.text = $"Не могу найти объект: {objectName}.";
                        }
                    }
                    else
                    {
                        responseText.text = "Ошибка: Для moveToObject требуется 'objectName'.";
                    }
                    break;

                case "moveToPosition":
                    if (parameters != null && parameters.ContainsKey("x") && parameters.ContainsKey("y") && parameters.ContainsKey("z"))
                    {
                        float x = (float)parameters["x"];
                        float y = (float)parameters["y"];
                        float z = (float)parameters["z"];
                        targetNPC.MoveToPosition(new Vector3(x, y, z));
                        responseText.text = $"NPC движется к позиции ({x}, {y}, {z}).";
                    }
                    else
                    {
                        responseText.text = "Ошибка: Для moveToPosition требуются 'x', 'y', 'z'.";
                    }
                    break;

                case "setBehavior":
                    if (parameters != null && parameters.ContainsKey("behaviorType"))
                    {
                        string behaviorType = parameters["behaviorType"].ToString();
                        targetNPC.SetBehavior(behaviorType);
                        responseText.text = $"NPC переходит в режим: {behaviorType}.";
                    }
                    else
                    {
                        responseText.text = "Ошибка: Для setBehavior требуется 'behaviorType'.";
                    }
                    break;

                case "say":
                    if (parameters != null && parameters.ContainsKey("text"))
                    {
                        string text = parameters["text"].ToString();
                        targetNPC.SayDialogue(text);
                        responseText.text = $"NPC говорит: \"{text}\"";
                    }
                    else
                    {
                        responseText.text = "Ошибка: Для say требуется 'text'.";
                    }
                    break;

                default:
                    responseText.text = $"Неизвестная команда LLM: {command}.";
                    break;
            }
        }
        catch (System.Exception e)
        {
            responseText.text = "Ошибка парсинга ответа LLM.";
            Debug.LogError($"Error processing LLM response: {e.Message}\nResponse: {jsonResponse}");
        }
    }
}
