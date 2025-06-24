using UnityEngine;
using UnityEngine.UI;
using System.Collections; // ��� �������
using UnityEngine.Networking; // ��� UnityWebRequest (HTTP �������)
using LitJson; // ��� �������� JSON ������� �� LLM (��������� LitJson.dll � Assets/Plugins)
using System.Text; // ��� ������ � ����������� �����

// ���������, ������� ��� NPC ������ �����������, ����� ��������� ������� �� LLM
/*public interface ILLMControllableNPC
{
    void MoveToPosition(Vector3 position);
    void MoveToObject(GameObject targetObject);
    void SetBehavior(string behaviorType); // ��������, "Patrol", "Idle", "ChasePlayer"
    void SayDialogue(string text);
    // ... ������ �������, ������� �� ������, ����� NPC ��������
}*/

public class LLM_NPC_Controller : MonoBehaviour
{
    [Header("UI ������")]
    [Tooltip("���� ����� ������ ��� ������ NPC.")]
    public InputField commandInputField;
    [Tooltip("������ ��� �������� ������� NPC.")]
    public Button sendCommandButton;
    [Tooltip("���� ��� ����������� ������� �� LLM/�������.")]
    public Text responseText;

    [Header("��������� LLM API")]
    [Tooltip("URL ������ LLM API (��������, Gemini API).")]
    public string llmApiUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key=";
    [Tooltip("��� API-���� ��� LLM (�� ������� � �������� ���� � ����������!).")]
    public string apiKey = ""; // ����� ������ ���� ��� API-����

    [Header("������ �� NPC")]
    [Tooltip("������ �� NPC, ������� ����� ��������� LLM. � ���� ������ ���� ���������, ����������� ILLMControllableNPC.")]
    public GameObject targetNPCGameObject;
    private ILLMControllableNPC targetNPC;

    void Start()
    {
       /* if (commandInputField == null || sendCommandButton == null || responseText == null)
        {
            Debug.LogError("LLM_NPC_Controller: UI �������� �� ���������!");
            enabled = false;
            return;
        }

        if (targetNPCGameObject == null)
        {
            Debug.LogError("LLM_NPC_Controller: Target NPC GameObject �� ��������!");
            enabled = false;
            return;
        }

        targetNPC = targetNPCGameObject.GetComponent<ILLMControllableNPC>();
        if (targetNPC == null)
        {
            Debug.LogError($"LLM_NPC_Controller: Target NPC GameObject '{targetNPCGameObject.name}' �� ��������� ��������� ILLMControllableNPC!");
            enabled = false;
            return;
        } */

        sendCommandButton.onClick.AddListener(OnSendCommand);
        responseText.text = "����� � ��������...";
    }

    /// <summary>
    /// ���������� ��� ������� ������ �������� �������.
    /// </summary>
    public void OnSendCommand()
    {
        string commandText = commandInputField.text;
        if (string.IsNullOrWhiteSpace(commandText))
        {
            responseText.text = "������� �������!";
            return;
        }

        responseText.text = "�������� ������� LLM...";
        StartCoroutine(SendToLLM(commandText)); // ��������� �������� ��� �������� �������
        commandInputField.text = ""; // ������� ���� �����
    }

    /// <summary>
    /// ���������� ������ LLM API � ������������ �����.
    /// </summary>
    /// <param name="userPrompt">��������� ������� ������������.</param>
    IEnumerator SendToLLM(string userPrompt)
    {
        // === ��� 1: ������������ ������� ��� LLM ===
        // ����� �� ������ LLM �������� � ����������������� JSON-�������
        // ��� ����� �����, ����� LLM ������������ ����������� �����.
        string llmPrompt = $"You are an AI assistant controlling a game NPC. The user will give you a command for the NPC. " +
                           $"Your task is to convert this command into a JSON object with a 'command' field and relevant 'parameters'. " +
                           $"Possible commands: 'moveToObject' (parameters: objectName:string), 'moveToPosition' (parameters: x:float, y:float, z:float), " +
                           $"'setBehavior' (parameters: behaviorType:string - e.g., 'Patrol', 'Idle', 'ChasePlayer'), " +
                           $"'say' (parameters: text:string). " +
                           $"If the command is unclear, use 'say' and ask for clarification. " +
                           $"Strictly respond ONLY with the JSON object. Do not include any other text.\n" +
                           $"User command: \"{userPrompt}\"";

        // ���� �� ����������� ������, ������� ������������ responseSchema, ����������� ��� ������ ���������� �������:
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

        // ��� gemini-2.0-flash, ������� �� ����������, ����� ����� ��������� ������ � ������
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

        yield return request.SendWebRequest(); // ���������� ������ � ���� ������

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"Error sending to LLM: {request.error}");
            responseText.text = $"������ LLM: {request.error}";
        }
        else
        {
            string jsonResponse = request.downloadHandler.text;
            Debug.Log($"Received from LLM: {jsonResponse}");
            ProcessLLMResponse(jsonResponse); // ������������ �����
        }
    }

    /// <summary>
    /// ��������� JSON-����� �� LLM � �������� ��������������� ������� �� NPC.
    /// </summary>
    /// <param name="jsonResponse">JSON-������ ������ �� LLM.</param>
    void ProcessLLMResponse(string jsonResponse)
    {
        try
        {
            // JsonMapper �������, ��� ������ JSON ����� �������� ��� ��������.
            // ����� �� Gemini API ����� ������� � ���������: { candidates: [ { content: { parts: [ { text: "..." } ] } } ] }
            JsonData rootData = JsonMapper.ToObject(jsonResponse);

            if (rootData == null || !rootData.ContainsKey("candidates") || rootData["candidates"].Count == 0 ||
                !rootData["candidates"][0].ContainsKey("content") || !rootData["candidates"][0]["content"].ContainsKey("parts") ||
                rootData["candidates"][0]["content"]["parts"].Count == 0)
            {
                responseText.text = "������: ������������ ����� �� LLM.";
                Debug.LogError("LLM response structure invalid: " + jsonResponse);
                return;
            }

            // ��������� ����� JSON-������� �� ������ LLM
            string commandJsonText = rootData["candidates"][0]["content"]["parts"][0]["text"].ToString();
            Debug.Log("Parsed command JSON text: " + commandJsonText);

            // ������ ������ ��� JSON-������ �������
            JsonData commandData = JsonMapper.ToObject(commandJsonText);

            string command = commandData["command"].ToString();
            JsonData parameters = commandData.ContainsKey("parameters") ? commandData["parameters"] : null;

            switch (command)
            {
                case "moveToObject":
                    if (parameters != null && parameters.ContainsKey("objectName"))
                    {
                        string objectName = parameters["objectName"].ToString();
                        GameObject targetObject = GameObject.Find(objectName); // ���� ������ �� �����
                        if (targetObject != null)
                        {
                            targetNPC.MoveToObject(targetObject);
                            responseText.text = $"NPC �������� � {objectName}.";
                        }
                        else
                        {
                            responseText.text = $"�� ���� ����� ������: {objectName}.";
                        }
                    }
                    else
                    {
                        responseText.text = "������: ��� moveToObject ��������� 'objectName'.";
                    }
                    break;

                case "moveToPosition":
                    if (parameters != null && parameters.ContainsKey("x") && parameters.ContainsKey("y") && parameters.ContainsKey("z"))
                    {
                        float x = (float)parameters["x"];
                        float y = (float)parameters["y"];
                        float z = (float)parameters["z"];
                        targetNPC.MoveToPosition(new Vector3(x, y, z));
                        responseText.text = $"NPC �������� � ������� ({x}, {y}, {z}).";
                    }
                    else
                    {
                        responseText.text = "������: ��� moveToPosition ��������� 'x', 'y', 'z'.";
                    }
                    break;

                case "setBehavior":
                    if (parameters != null && parameters.ContainsKey("behaviorType"))
                    {
                        string behaviorType = parameters["behaviorType"].ToString();
                        targetNPC.SetBehavior(behaviorType);
                        responseText.text = $"NPC ��������� � �����: {behaviorType}.";
                    }
                    else
                    {
                        responseText.text = "������: ��� setBehavior ��������� 'behaviorType'.";
                    }
                    break;

                case "say":
                    if (parameters != null && parameters.ContainsKey("text"))
                    {
                        string text = parameters["text"].ToString();
                        targetNPC.SayDialogue(text);
                        responseText.text = $"NPC �������: \"{text}\"";
                    }
                    else
                    {
                        responseText.text = "������: ��� say ��������� 'text'.";
                    }
                    break;

                default:
                    responseText.text = $"����������� ������� LLM: {command}.";
                    break;
            }
        }
        catch (System.Exception e)
        {
            responseText.text = "������ �������� ������ LLM.";
            Debug.LogError($"Error processing LLM response: {e.Message}\nResponse: {jsonResponse}");
        }
    }
}
