using UnityEngine;

public class MazeGoal : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<PlayerController>() != null && MazeGenerator.Instance != null)
            MazeGenerator.Instance.CompleteMaze();
    }
}
