using UnityEngine;
using UnityEngine.AI; // ��� NavMeshAgent
using System.Collections; // ��� �������
using System.IO; // ��� ������ � �������
using LitJson; // ��� JSON-������������/��������������
using UnityEngine.UI;

// ����� ��� ���������� ������ NPC
[System.Serializable]
public class SimpleNPCSaveData
{
    public string id; // ���������� ID ��� ����� NPC

    // --- �������� ��������� ����� ---
    // ��������� Vector3 ��� ��������� float ����������
    public float posX, posY, posZ;
    // ��������� Quaternion ��� ��������� float ����������
    public float rotX, rotY, rotZ, rotW;
    // --- ����� �������� ��������� ����� ---

    public string currentStateName; // ��������� ������� ��������� ��� ��������������

    // �����-�������� ��� �������������� Vector3 � ����������
    public void SetPosition(Vector3 position)
    {
        posX = position.x;
        posY = position.y;
        posZ = position.z;
    }
    // �����-�������� ��� ��������� Vector3 �� �����������
    public Vector3 GetPosition()
    {
        return new Vector3(posX, posY, posZ);
    }

    // �����-�������� ��� �������������� Quaternion � ����������
    public void SetRotation(Quaternion rotation)
    {
        rotX = rotation.x;
        rotY = rotation.y;
        rotZ = rotation.z;
        rotW = rotation.w;
    }
    // �����-�������� ��� ��������� Quaternion �� �����������
    public Quaternion GetRotation()
    {
        return new Quaternion(rotX, rotY, rotZ, rotW);
    }
}

// ���������, ������� NPC ������ �����������, ����� ��������� ������� �� LLM
/*public interface ILLMControllableNPC
{
    void MoveToPosition(Vector3 position);
    void MoveToObject(GameObject targetObject);
    void SetBehavior(string behaviorType); // ��������, "Patrol", "Idle"
    void SayDialogue(string text);
}*/

// === �������� ������ ������������ NPC ===
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
public class SimpleLLMControllableNPC : MonoBehaviour, ILLMControllableNPC
{
    [Header("��������� NPC")]
    [Tooltip("���������� ID ��� ���������� ����� NPC (��������, 'Robot_001').")]
    public string npcID = "NPC_Default";
    [Tooltip("�������� ����������� NPC.")]
    public float movementSpeed = 3.5f;

    public enum NPCState
    {
        Idle,
        Moving,
        Patrolling,
        Dialoguing
    }

    public NPCState currentState = NPCState.Idle;

    [Header("��������� �������������� (�����������)")]
    [Tooltip("�����, �� ������� NPC ����� ������������� � ��������� Patrol.")]
    public Transform[] patrolPoints;
    private int currentPatrolPointIndex = 0;
    [Tooltip("���������� �� �����, ����� NPC �������, ��� ������ �� � ��������� � ���������.")]
    public float waypointThreshold = 0.5f;

    [Header("��������� ���������")]
    [Tooltip("��� Float-��������� �������� � Animator Controller (��������, 'Speed').")]
    public string speedAnimatorParam = "Speed";
    [Tooltip("��� Bool-��������� ��� �������� � Animator Controller (��������, 'IsMoving').")]
    public string isMovingAnimatorParam = "IsMoving";
    [Tooltip("��� Trigger-��������� ��� ������ ������� � Animator Controller (��������, 'StartDialogue').")]
    public string startDialogueAnimatorParam = "StartDialogue";
    [Tooltip("��� Trigger-��������� ��� ��������� ������� (�����������, ���� ����).")]
    public string stopDialogueAnimatorParam = "StopDialogue";
    [Tooltip("��� Trigger-��������� ��� �������� � ��������� �������������� (�����������, ���� ����).")]
    public string startPatrolAnimatorParam = "StartPatrol";
    [Tooltip("��� Trigger-��������� ��� �������� � ��������� ����������� (�����������, ���� ����).")]
    public string startIdleAnimatorParam = "StartIdle";

    public Text npcText;
    // --- ��������� ���������� ---
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
            Debug.LogError($"SimpleLLMControllableNPC: NavMeshAgent �� ������ �� {gameObject.name}. ��������� ������.", this);
            enabled = false;
            return;
        }
        navMeshAgent.speed = movementSpeed;

        if (animator == null)
        {
            Debug.LogError($"SimpleLLMControllableNPC: Animator �� ������ �� {gameObject.name}. �������� �������� �� �����.", this);
        }

        string saveFolder = Path.Combine(Application.persistentDataPath, SAVE_FOLDER_NAME); // ���������, ��� Application.persistentDataPath ������������
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
                Debug.LogWarning($"{gameObject.name}: ��� ���������� �����. ������� � Idle.");
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
            Debug.Log($"{gameObject.name} ������� �������: MoveToPosition � {position}.");
        }
        else
        {
            Debug.LogWarning($"{gameObject.name}: NavMeshAgent ��������� ��� ����������� ��� MoveToPosition.");
            SayDialogue("� �� ���� ���������, ��� ������������� ������ ��������.");
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
                Debug.Log($"{gameObject.name} ������� �������: MoveToObject � {targetObject.name}.");
            }
            else
            {
                Debug.LogWarning($"{gameObject.name}: ������� ������ ��� MoveToObject ����� null.");
                SayDialogue("� �� ���� ����� ����, ������� �� �������.");
            }
        }
        else
        {
            Debug.LogWarning($"{gameObject.name}: NavMeshAgent ��������� ��� ����������� ��� MoveToObject.");
            SayDialogue("� �� ���� ���������, ��� ������������� ������ ��������.");
        }
    }

    public void SetBehavior(string behaviorType)
    {
        try
        {
            NPCState newNPCState = (NPCState)System.Enum.Parse(typeof(NPCState), behaviorType, true);
            ChangeState(newNPCState);
            Debug.Log($"{gameObject.name} ������� �������: SetBehavior � {behaviorType}.");
            SayDialogue($"������ � � ������ {behaviorType}.");
        }
        catch (System.ArgumentException)
        {
            Debug.LogError($"����������� ��� ��������� ��� NPC: {behaviorType}");
            SayDialogue($"��������, � �� ����� ������� '{behaviorType}'.");
        }
    }

    public void SayDialogue(string text)
    {
        ChangeState(NPCState.Dialoguing);

        Debug.Log($"NPC {gameObject.name} (�������): \"{text}\"");
        npcText.text = "- " + text;
        currentActionCoroutine = StartCoroutine(EndDialogueAfterDelay(3f));
    }

    private IEnumerator WaitForDestination(Vector3 targetPos)
    {
        yield return new WaitUntil(() => !navMeshAgent.pathPending && navMeshAgent.remainingDistance <= navMeshAgent.stoppingDistance + 0.1f);

        if (currentState == NPCState.Moving)
        {
            ChangeState(NPCState.Idle);
            Debug.Log($"{gameObject.name} ������ ����. ����������� � Idle.");
        }
    }

    private IEnumerator EndDialogueAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (currentState == NPCState.Dialoguing)
        {
            if (animator != null && !string.IsNullOrEmpty(stopDialogueAnimatorParam)) animator.SetTrigger(stopDialogueAnimatorParam);
            ChangeState(NPCState.Idle);
            Debug.Log($"{gameObject.name}: ������ ��������. ����������� � Idle.");
        }
    }

    private void GoToNextPatrolPoint()
    {
        if (patrolPoints == null || patrolPoints.Length == 0)
        {
            Debug.LogWarning($"{gameObject.name}: ��� ���������� �����. ������� � Idle.");
            ChangeState(NPCState.Idle);
            return;
        }

        navMeshAgent.SetDestination(patrolPoints[currentPatrolPointIndex].position);
        currentPatrolPointIndex = (currentPatrolPointIndex + 1) % patrolPoints.Length;
        Debug.Log($"{gameObject.name} �������� � ���������� ����� {patrolPoints[currentPatrolPointIndex].name}.");
    }

    // ====================================================================
    // ������ ���������� � �������� ���������
    // ====================================================================

    /// <summary>
    /// ��������� ������� ��������� NPC � JSON ����.
    /// </summary>
    public void SaveState()
    {
        try
        {
            SimpleNPCSaveData saveData = new SimpleNPCSaveData(); // ������� ���������
            saveData.id = this.npcID;

            // --- ���������� ������-��������� ��� �������������� ---
            saveData.SetPosition(transform.position);
            saveData.SetRotation(transform.rotation);
            // --- ����� ������������� �������-���������� ---

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
    /// ��������� ��������� NPC �� JSON �����.
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

                navMeshAgent.enabled = false; // �������� ��������� �����
                // --- ���������� ������-��������� ��� ��������� ---
                transform.position = loadedData.GetPosition();
                transform.rotation = loadedData.GetRotation();
                // --- ����� ������������� �������-���������� ---
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

    // === ������� � ��������� ===
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
