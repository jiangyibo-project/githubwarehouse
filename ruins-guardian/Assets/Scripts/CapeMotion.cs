using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 披风次级摆动：按角色根节点速度做延迟倾斜，并对卡面（MeshKit.Card）网格做逐顶点波浪位移。
/// 运行时实例化网格，不会改动共享资源；顶点不可读时自动降级为纯倾斜。
/// </summary>
[RequireComponent(typeof(MeshFilter))]
public sealed class CapeMotion : MonoBehaviour
{
    [SerializeField] private Transform followTarget;
    [Tooltip("最大后摆角度")]
    [SerializeField] private float tiltAngle = 18f;
    [Tooltip("顶点波浪振幅")]
    [SerializeField] private float waveAmplitude = 0.035f;
    [Tooltip("波浪速度")]
    [SerializeField] private float waveSpeed = 3.4f;
    [Tooltip("沿披风长度的波数系数")]
    [SerializeField] private float waveFrequency = 9f;
    [Tooltip("速度平滑速度")]
    [SerializeField] private float dampSpeed = 7f;

    private Mesh mesh;
    private List<Vector3> baseVertices;
    private readonly List<Vector3> workVertices = new List<Vector3>(64);
    private Vector3 lastTargetPosition;
    private Vector3 velocity;
    private Vector3 baseEuler;
    private float capeHeight = 1f;
    private float tilt;
    private float tiltSpeed;
    private bool vertexAnimated;

    private void Awake()
    {
        baseEuler = transform.localRotation.eulerAngles;

        MeshFilter filter = GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null)
        {
            mesh = Instantiate(filter.sharedMesh);
            mesh.name = filter.sharedMesh.name + "_Sway";
            filter.sharedMesh = mesh;
            if (mesh.isReadable)
            {
                baseVertices = new List<Vector3>(mesh.vertexCount);
                mesh.GetVertices(baseVertices);
                float lowest = 0f;
                for (int i = 0; i < baseVertices.Count; i++) lowest = Mathf.Min(lowest, baseVertices[i].y);
                capeHeight = Mathf.Max(0.001f, -lowest);
                vertexAnimated = true;
            }
        }

        if (followTarget == null) followTarget = transform.root;
        if (followTarget != null) lastTargetPosition = followTarget.position;
    }

    private void LateUpdate()
    {
        if (followTarget == null) return;

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 delta = followTarget.position - lastTargetPosition;
        delta.y = 0f;
        lastTargetPosition = followTarget.position;
        velocity = Vector3.Lerp(velocity, delta / dt, Mathf.Clamp01(dt * dampSpeed));

        Vector3 localVelocity = transform.parent != null ? transform.parent.InverseTransformDirection(velocity) : velocity;
        float target = Mathf.Clamp(localVelocity.z * 4f, -tiltAngle, tiltAngle);
        tilt = Mathf.SmoothDamp(tilt, target, ref tiltSpeed, 0.14f, Mathf.Infinity, dt);

        float sway = Mathf.Clamp(-localVelocity.x * 90f, -14f, 14f);
        transform.localRotation = Quaternion.Euler(baseEuler.x + tilt, baseEuler.y, baseEuler.z + sway);

        if (!vertexAnimated) return;

        float time = Time.time;
        float speedLag = Mathf.Clamp01(Mathf.Abs(localVelocity.z) / 6f) * 0.05f;
        workVertices.Clear();
        for (int i = 0; i < baseVertices.Count; i++)
        {
            Vector3 vertex = baseVertices[i];
            float weight = Mathf.Clamp01(-vertex.y / capeHeight);
            float wave = Mathf.Sin(time * waveSpeed + vertex.y * waveFrequency) * waveAmplitude * weight * weight;
            vertex.z += wave + speedLag * weight * weight;
            workVertices.Add(vertex);
        }

        mesh.SetVertices(workVertices);
        mesh.RecalculateNormals();
    }

    private void OnDestroy()
    {
        if (mesh == null) return;
        // 播放中销毁实例网格；退出播放模式时走到这里则用立即销毁，避免编辑器报错
        if (Application.isPlaying) Destroy(mesh);
        else DestroyImmediate(mesh);
    }
}
