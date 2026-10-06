using UnityEngine;

public enum MazeItem { None, Hammer, Fly, Shield, Heal }

public class PlayerInventory : MonoBehaviour
{
    public MazeItem HeldItem { get; private set; }
    public string ItemLabel => HeldItem == MazeItem.None ? "空" : HeldItem == MazeItem.Hammer ? "破坏锤" : HeldItem == MazeItem.Fly ? "飞起" : HeldItem == MazeItem.Shield ? "护盾" : "回血药";

    public bool TryPickup(MazeItem item)
    {
        if (HeldItem != MazeItem.None) return false;
        HeldItem = item;
        return true;
    }

    public void ResetInventory() { HeldItem = MazeItem.None; }

    public void UseItem()
    {
        if (HeldItem == MazeItem.None)
        {
            if (MazeGenerator.Instance != null) MazeGenerator.Instance.ShowNotice("当前没有携带道具");
            return;
        }
        if (HeldItem == MazeItem.Hammer)
        {
            Camera cam = Camera.main;
            Vector3 origin = transform.position + Vector3.up * 0.5f;
            Vector3 direction = cam != null ? cam.transform.forward : transform.forward;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) direction = transform.forward;
            direction.Normalize();
            RaycastHit[] hits = Physics.RaycastAll(origin, direction, 3.2f);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider.transform == transform || hits[i].collider.transform.IsChildOf(transform)) continue;
                FakeWall wall = hits[i].collider.GetComponentInParent<FakeWall>();
                if (wall != null)
                {
                    wall.Break(); HeldItem = MazeItem.None; GameSfx.Play(gameObject, GameSound.Hammer);
                    if (MazeGenerator.Instance != null) MazeGenerator.Instance.ShowNotice("假墙已击碎 · 道具已消耗");
                }
                else if (MazeGenerator.Instance != null)
                    MazeGenerator.Instance.ShowNotice("锤子只能击碎带裂纹的假墙");
                break;
            }
            if (hits.Length == 0 && MazeGenerator.Instance != null)
                MazeGenerator.Instance.ShowNotice("朝向裂纹假墙使用锤子");
        }
        else if (HeldItem == MazeItem.Fly)
        {
            PlayerController controller = GetComponent<PlayerController>();
            if (controller != null && controller.BeginFlight())
            {
                HeldItem = MazeItem.None;
                GameSfx.Play(gameObject, GameSound.Fly);
                if (MazeGenerator.Instance != null) MazeGenerator.Instance.ShowNotice("高跳起飞 · 空中可移动，圆形也能使用");
            }
        }
        else if (HeldItem == MazeItem.Shield)
        {
            PlayerHealth health = GetComponent<PlayerHealth>();
            if (health != null) health.ActivateShield(60f);
            HeldItem = MazeItem.None;
            if (health != null) GameSfx.Play(gameObject, GameSound.Shield);
            if (MazeGenerator.Instance != null) MazeGenerator.Instance.ShowNotice("护盾启动 · 60 秒免疫攻击，坠落仍会受伤");
        }
        else if (HeldItem == MazeItem.Heal)
        {
            PlayerHealth health = GetComponent<PlayerHealth>();
            if (health != null) { health.Heal(1); GameSfx.Play(gameObject, GameSound.Heal); }
            HeldItem = MazeItem.None;
            if (health != null && MazeGenerator.Instance != null) MazeGenerator.Instance.ShowNotice("生命 +1");
        }
    }
}
