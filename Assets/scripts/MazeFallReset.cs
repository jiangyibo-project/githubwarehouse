using UnityEngine;

public class MazeFallReset : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player != null && MazeGenerator.Instance != null)
            MazeGenerator.Instance.HandlePlayerFall(player.transform.position);
    }
}
