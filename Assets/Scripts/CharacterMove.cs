using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public class CharacterMove : MonoBehaviour
{
    [Header("移动")]
    public float moveSpeed = 5f;
    public float sprintMultiplier = 1.8f;
    public float jumpForce = 5f;

    [Header("视角")]
    public Transform cameraTransform; // 将场景中角色的 Camera 作为子对象拖入
    public float mouseSensitivity = 2f;
    public float maxLookAngle = 80f; // 上下视角限制（度）

    [Header("地面检测")]
    public float groundCheckDistance = 0.1f;
    public LayerMask groundMask = ~0; // 默认检测所有层

    Rigidbody rb;
    float yaw;    // 水平旋转
    float pitch;  // 垂直旋转
    bool wantJump;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        // 使用物理驱动角色移动，同时避免刚体因物理力矩倾斜
        rb.freezeRotation = true;

        // 若未设置 cameraTransform，尝试查找子摄像机
        if (cameraTransform == null)
        {
            var cam = GetComponentInChildren<Camera>();
            if (cam != null) cameraTransform = cam.transform;
        }

        // 初始锁定光标（可根据需求取消）
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // 初始化朝向
        yaw = transform.eulerAngles.y;
        if (cameraTransform != null)
            pitch = cameraTransform.localEulerAngles.x;
    }

    void Update()
    {
        // 鼠标输入 -> 更新朝向
        float mx = Input.GetAxis("Mouse X") * mouseSensitivity;
        float my = Input.GetAxis("Mouse Y") * mouseSensitivity;

        yaw += mx;
        pitch -= my;
        pitch = Mathf.Clamp(pitch, -maxLookAngle, maxLookAngle);

        if (cameraTransform != null)
            cameraTransform.localEulerAngles = new Vector3(pitch, 0f, 0f);

        // 跳跃输入（记录到 FixedUpdate 用物理方式执行）
        if (Input.GetButtonDown("Jump"))
            wantJump = true;

        // 可选：切换光标锁定（按 Esc 释放）
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    void FixedUpdate()
    {
        // 应用水平旋转到刚体（物理安全）
        rb.MoveRotation(Quaternion.Euler(0f, yaw, 0f));

        // 获取移动输入（基于角色本地坐标）
        float h = Input.GetAxisRaw("Horizontal"); // A/D
        float v = Input.GetAxisRaw("Vertical");   // W/S
        Vector3 forward = transform.forward;
        Vector3 right = transform.right;
        Vector3 moveDir = (forward * v + right * h).normalized;

        // 奔跑
        float speed = moveSpeed;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            speed *= sprintMultiplier;

        Vector3 targetPos = rb.position + moveDir * speed * Time.fixedDeltaTime;
        rb.MovePosition(targetPos);

        // 地面检测（使用射线检测到地面）
        bool isGrounded = Physics.Raycast(transform.position, Vector3.down, GetGroundCheckDistance(), groundMask);

        // 跳跃（用速度改变，物理驱动）
        if (wantJump && isGrounded)
        {
            // 确保垂直速度清零后再添加跳跃速度，避免连续累加
            Vector3 vel = rb.velocity;
            vel.y = 0f;
            rb.velocity = vel;
            rb.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);
        }

        wantJump = false;
    }

    // 计算用于地面检测的距离（基于 CapsuleCollider 尺寸）
    float GetGroundCheckDistance()
    {
        var capsule = GetComponent<CapsuleCollider>();
        if (capsule != null)
        {
            // 从物体中心到底部的距离 + 小偏移
            float halfHeight = Mathf.Max(0f, capsule.height * 0.5f - capsule.radius);
            return halfHeight + capsule.radius + groundCheckDistance;
        }

        return 1f + groundCheckDistance;
    }
}