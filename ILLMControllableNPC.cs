using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface ILLMControllableNPC
{
    void MoveToPosition(Vector3 position);
    void MoveToObject(GameObject targetObject);
    void SetBehavior(string behaviorType); // ��������, "Patrol", "Idle"
    void SayDialogue(string text);
}
