using UnityEngine;
using UnityEngine.AI; // Для NavMeshAgent
using System.Collections; // Для корутин
using System.IO; // Для работы с файлами
using LitJson; // Для JSON-сериализации/десериализации
using UnityEngine.UI;

// Класс для сохранения данных NPC
[System.Serializable]
public class SimpleNPCSaveData
{
    public string id; // Уникальный ID для этого NPC

    // --- КЛЮЧЕВОЕ ИЗМЕНЕНИЕ ЗДЕСЬ ---
    // Сохраняем Vector3 как отдельные float компоненты
    public float posX, posY, posZ;
    // Сохраняем Quaternion как отдельные float компоненты
    public float rotX, rotY, rotZ, rotW;
    // --- КОНЕЦ КЛЮЧЕВЫХ ИЗМЕНЕНИЙ ЗДЕСЬ ---

    public string currentStateName; // Сохраняем текущее состояние для восстановления

    // Метод-помощник для преобразования Vector3 в компоненты
    public void SetPosition(Vector3 position)
    {
        posX = position.x;
        posY = position.y;
        posZ = position.z;
    }
    // Метод-помощник для получения Vector3 из компонентов
    public Vector3 GetPosition()
    {
        return new Vector3(posX, posY, posZ);
    }

    // Метод-помощник для преобразования Quaternion в компоненты
    public void SetRotation(Quaternion rotation)
    {
        rotX = rotation.x;
        rotY = rotation.y;
        rotZ = rotation.z;
        rotW = rotation.w;
    }
    // Метод-помощник для получения Quaternion из компонентов
    public Quaternion GetRotation()
    {
        return new Quaternion(rotX, rotY, rotZ, rotW);
    }
}

// Интерфейс, который NPC должен реализовать, чтобы принимать команды от LLM
/*public interface ILLMControllableNPC
{
    void MoveToPosition(Vector3 position);
    void MoveToObject(GameObject targetObject);
    void SetBehavior(string behaviorType); // Например, "Patrol", "Idle"
    void SayDialogue(string text);
}*/

// === ОСНОВНОЙ СКРИПТ УПРАВЛЯЕМОГО NPC ===
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
public class SimpleLLMControllableNPC : MonoBehaviour, ILLMControllableNPC
{
    [Header("Настройки NPC")]
    [Tooltip("Уникальный ID для сохранения этого NPC (например, 'Robot_001').")]
    public string npcID = "NPC_Default";
    [Tooltip("Скорость перемещения NPC.")]
    public float movementSpeed = 3.5f;

    public enum NPCState
    {
        Idle,
        Moving,
        Patrolling,
        Dialoguing
    }

    public NPCState currentState = NPCState.Idle;

    [Header("Настройки Патрулирования (опционально)")]
    [Tooltip("Точки, по которым NPC будет патрулировать в состоянии Patrol.")]
    public Transform[] patrolPoints;
    private int currentPatrolPointIndex = 0;
    [Tooltip("Расстояние до точки, когда NPC считает, что достиг ее и переходит к следующей.")]
    public float waypointThreshold = 0.5f;

    [Header("Настройки Аниматора")]
    [Tooltip("Имя Float-параметра скорости в Animator Controller (например, 'Speed').")]
    public string speedAnimatorParam = "Speed";
    [Tooltip("Имя Bool-параметра для движения в Animator Controller (например, 'IsMoving').")]
    public string isMovingAnimatorParam = "IsMoving";
    [Tooltip("Имя Trigger-параметра для начала диалога в Animator Controller (например, 'StartDialogue').")]
    public string startDialogueAnimatorParam = "StartDialogue";
    [Tooltip("Имя Trigger-параметра для остановки диалога (опционально, если есть).")]
    public string stopDialogueAnimatorParam = "StopDialogue";
    [Tooltip("Имя Trigger-параметра для перехода в состояние патрулирования (опционально, если есть).")]
    public string startPatrolAnimatorParam = "StartPatrol";
    [Tooltip("Имя Trigger-параметра для перехода в состояние бездействия (опционально, если есть).")]
    public string startIdleAnimatorParam = "StartIdle";

    public Text npcText;
    // --- Приватные переменные ---
    private NavMeshAgent navMeshAgent;
    private Animator animator;
    private Coroutine currentActionCoroutine;

    private const string SAVE_FOLDER_NAME = "GameSaves";
    private string saveFilePath;

    void Awake()
    {
        navMeshAgent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();

        if (navMeshAgent == null)
        {
            Debug.LogError($"SimpleLLMControllableNPC: NavMeshAgent не найден на {gameObject.name}. Отключаем скрипт.", this);
            enabled = false;
            return;
        }
        navMeshAgent.speed = movementSpeed;

        if (animator == null)
        {
            Debug.LogError($"SimpleLLMControllableNPC: Animator не найден на {gameObject.name}. Анимации работать не будут.", this);
        }

        string saveFolder = Path.Combine(Application.persistentDataPath, SAVE_FOLDER_NAME); // Убедитесь, что Application.persistentDataPath используется
        if (!Directory.Exists(saveFolder))
        {
            Directory.CreateDirectory(saveFolder);
        }
        saveFilePath = Path.Combine(saveFolder, $"{npcID}_SimpleNPC.json");
    }

    void Start()
    {
        LoadState();
        ChangeState(currentState);
    }

    void Update()
    {
        if (currentState == NPCState.Patrolling)
        {
            if (patrolPoints == null || patrolPoints.Length == 0)
            {
                Debug.LogWarning($"{gameObject.name}: Нет патрульных точек. Переход в Idle.");
                ChangeState(NPCState.Idle);
                return;
            }

            if (!navMeshAgent.pathPending && navMeshAgent.remainingDistance < waypointThreshold)
            {
                GoToNextPatrolPoint();
            }
        }

      /*  if (animator != null && navMeshAgent.enabled)
        {
            animator.SetFloat(speedAnimatorParam, navMeshAgent.velocity.magnitude);
            animator.SetBool(isMovingAnimatorParam, navMeshAgent.velocity.magnitude > 0.1f);
        } */
    }

    void OnApplicationQuit()
    {
        SaveState();
    }

    private void ChangeState(NPCState newState)
    {
        if (currentState == newState) return;

        Debug.Log($"{gameObject.name}: Changing state from {currentState} to {newState}");
        currentState = newState;

        if (currentActionCoroutine != null)
        {
            StopCoroutine(currentActionCoroutine);
            currentActionCoroutine = null;
        }

        if (animator != null)
        {
            foreach (var param in animator.parameters)
            {
                if (param.type == AnimatorControllerParameterType.Trigger)
                {
                    animator.ResetTrigger(param.name);
                }
            }
        }

        switch (currentState)
        {
            case NPCState.Idle:
                navMeshAgent.isStopped = true;
                if (animator != null && !string.IsNullOrEmpty(startIdleAnimatorParam)) animator.SetTrigger(startIdleAnimatorParam);
                break;
            case NPCState.Moving:
                navMeshAgent.isStopped = false;
                break;
            case NPCState.Patrolling:
                navMeshAgent.isStopped = false;
                if (animator != null && !string.IsNullOrEmpty(startPatrolAnimatorParam)) animator.SetTrigger(startPatrolAnimatorParam);
                GoToNextPatrolPoint();
                break;
            case NPCState.Dialoguing:
                navMeshAgent.isStopped = true;
                if (animator != null && !string.IsNullOrEmpty(startDialogueAnimatorParam)) animator.SetTrigger(startDialogueAnimatorParam);
                break;
        }
    }

    public void MoveToPosition(Vector3 position)
    {
        if (navMeshAgent != null && navMeshAgent.enabled)
        {
            ChangeState(NPCState.Moving);
            navMeshAgent.SetDestination(position);
            currentActionCoroutine = StartCoroutine(WaitForDestination(position));
            Debug.Log($"{gameObject.name} получил команду: MoveToPosition к {position}.");
        }
        else
        {
            Debug.LogWarning($"{gameObject.name}: NavMeshAgent неактивен или отсутствует для MoveToPosition.");
            SayDialogue("Я не могу двигаться, мой навигационный модуль отключен.");
        }
    }

    public void MoveToObject(GameObject targetObject)
    {
        if (navMeshAgent != null && navMeshAgent.enabled)
        {
            if (targetObject != null)
            {
                ChangeState(NPCState.Moving);
                navMeshAgent.SetDestination(targetObject.transform.position);
                currentActionCoroutine = StartCoroutine(WaitForDestination(targetObject.transform.position));
                Debug.Log($"{gameObject.name} получил команду: MoveToObject к {targetObject.name}.");
            }
            else
            {
                Debug.LogWarning($"{gameObject.name}: Целевой объект для MoveToObject равен null.");
                SayDialogue("Я не могу найти цель, которую вы указали.");
            }
        }
        else
        {
            Debug.LogWarning($"{gameObject.name}: NavMeshAgent неактивен или отсутствует для MoveToObject.");
            SayDialogue("Я не могу двигаться, мой навигационный модуль отключен.");
        }
    }

    public void SetBehavior(string behaviorType)
    {
        try
        {
            NPCState newNPCState = (NPCState)System.Enum.Parse(typeof(NPCState), behaviorType, true);
            ChangeState(newNPCState);
            Debug.Log($"{gameObject.name} получил команду: SetBehavior к {behaviorType}.");
            SayDialogue($"Теперь я в режиме {behaviorType}.");
        }
        catch (System.ArgumentException)
        {
            Debug.LogError($"Неизвестный тип поведения для NPC: {behaviorType}");
            SayDialogue($"Извините, я не понял команду '{behaviorType}'.");
        }
    }

    public void SayDialogue(string text)
    {
        ChangeState(NPCState.Dialoguing);

        Debug.Log($"NPC {gameObject.name} (говорит): \"{text}\"");
        npcText.text = "- " + text;
        currentActionCoroutine = StartCoroutine(EndDialogueAfterDelay(3f));
    }

    private IEnumerator WaitForDestination(Vector3 targetPos)
    {
        yield return new WaitUntil(() => !navMeshAgent.pathPending && navMeshAgent.remainingDistance <= navMeshAgent.stoppingDistance + 0.1f);

        if (currentState == NPCState.Moving)
        {
            ChangeState(NPCState.Idle);
            Debug.Log($"{gameObject.name} достиг цели. Возвращаюсь в Idle.");
        }
    }

    private IEnumerator EndDialogueAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (currentState == NPCState.Dialoguing)
        {
            if (animator != null && !string.IsNullOrEmpty(stopDialogueAnimatorParam)) animator.SetTrigger(stopDialogueAnimatorParam);
            ChangeState(NPCState.Idle);
            Debug.Log($"{gameObject.name}: Диалог завершен. Возвращаюсь в Idle.");
        }
    }

    private void GoToNextPatrolPoint()
    {
        if (patrolPoints == null || patrolPoints.Length == 0)
        {
            Debug.LogWarning($"{gameObject.name}: Нет патрульных точек. Переход в Idle.");
            ChangeState(NPCState.Idle);
            return;
        }

        navMeshAgent.SetDestination(patrolPoints[currentPatrolPointIndex].position);
        currentPatrolPointIndex = (currentPatrolPointIndex + 1) % patrolPoints.Length;
        Debug.Log($"{gameObject.name} движется к патрульной точке {patrolPoints[currentPatrolPointIndex].name}.");
    }

    // ====================================================================
    // Методы СОХРАНЕНИЯ и ЗАГРУЗКИ состояния
    // ====================================================================

    /// <summary>
    /// Сохраняет текущее состояние NPC в JSON файл.
    /// </summary>
    public void SaveState()
    {
        try
        {
            SimpleNPCSaveData saveData = new SimpleNPCSaveData(); // Создаем экземпляр
            saveData.id = this.npcID;

            // --- Используем методы-помощники для преобразования ---
            saveData.SetPosition(transform.position);
            saveData.SetRotation(transform.rotation);
            // --- Конец использования методов-помощников ---

            saveData.currentStateName = currentState.ToString();
            string json = JsonMapper.ToJson(saveData);
            File.WriteAllText(saveFilePath, json);
            Debug.Log($"NPC '{npcID}' state saved. State: {currentState}.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error saving NPC '{npcID}' state: {e.Message}");
        }
    }

    /// <summary>
    /// Загружает состояние NPC из JSON файла.
    /// </summary>
    public void LoadState()
    {
        try
        {
            if (File.Exists(saveFilePath))
            {
                string json = File.ReadAllText(saveFilePath);
                SimpleNPCSaveData loadedData = JsonMapper.ToObject<SimpleNPCSaveData>(json);

                this.npcID = loadedData.id;

                navMeshAgent.enabled = false; // Временно отключаем агент
                // --- Используем методы-помощники для получения ---
                transform.position = loadedData.GetPosition();
                transform.rotation = loadedData.GetRotation();
                // --- Конец использования методов-помощников ---
                navMeshAgent.enabled = true;

                currentState = (NPCState)System.Enum.Parse(typeof(NPCState), loadedData.currentStateName);

                Debug.Log($"NPC '{npcID}' state loaded. State: {currentState}.");
            }
            else
            {
                Debug.Log($"NPC '{npcID}' save file not found. Starting at default State {currentState}.");
                SaveState();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error loading NPC '{npcID}' state: {e.Message}");
            currentState = NPCState.Idle;
            SaveState();
        }
    }

    // === Отладка в редакторе ===
#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        UnityEditor.Handles.Label(transform.position + Vector3.up * 2f,
                                 $"NPC: {npcID}\nState: {currentState}");

        if (navMeshAgent != null && navMeshAgent.hasPath)
        {
            Gizmos.color = Color.blue;
            Vector3[] pathCorners = navMeshAgent.path.corners;
            for (int i = 0; i < pathCorners.Length - 1; i++)
            {
                Gizmos.DrawLine(pathCorners[i], pathCorners[i + 1]);
            }
        }

        if (patrolPoints != null && patrolPoints.Length > 0)
        {
            Gizmos.color = Color.green;
            for (int i = 0; i < patrolPoints.Length; i++)
            {
                if (patrolPoints[i] != null)
                {
                    Gizmos.DrawSphere(patrolPoints[i].position, 0.3f);
                    if (i < patrolPoints.Length - 1 && patrolPoints[i + 1] != null)
                    {
                        Gizmos.DrawLine(patrolPoints[i].position, patrolPoints[i + 1].position);
                    }
                    else if (i == patrolPoints.Length - 1 && patrolPoints.Length > 1 && patrolPoints[0] != null)
                    {
                        Gizmos.DrawLine(patrolPoints[i].position, patrolPoints[0].position);
                    }
                }
            }
        }
    }
#endif
}
