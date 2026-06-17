using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public class CharacterMove : MonoBehaviour
{
    [Header("???")]
    public float moveSpeed = 5f;
    public float sprintMultiplier = 1.8f;
    public float jumpForce = 5f;

    [Header("???")]
    public Transform cameraTransform; // ???????��???? Camera ????????????
    public float mouseSensitivity = 2f;
    public float maxLookAngle = 80f; // ???????????????

    [Header("??????")]
    public float groundCheckDistance = 0.1f;
    public LayerMask groundMask = ~0; // ????????��?

    [Header("下蹲")]
    [Tooltip("按住该键进入下蹲，松开后若头顶无遮挡则起身。")]
    public KeyCode crouchKey = KeyCode.LeftControl;

    [Tooltip("额外的下蹲按键（同样按住进入下蹲）。")]
    public KeyCode crouchKeyAlt = KeyCode.RightControl;

    [Tooltip("下蹲时胶囊体的目标高度（米）。脚底位置保持不变，胶囊从顶部缩短。")]
    public float crouchHeight = 1.1f;

    [Tooltip("下蹲时移动速度倍率。")]
    [Range(0.1f, 1f)]
    public float crouchSpeedMultiplier = 0.5f;

    [Tooltip("下蹲 / 起身过渡的平滑时间（秒）。0 即瞬切。")]
    public float crouchTransitionSmoothTime = 0.12f;

    [Tooltip("起身碰撞检测时使用的半径冗余（米），从胶囊半径里扣掉这个值，避免误判墙体卡住起不来。")]
    public float standUpClearance = 0.05f;

    Rigidbody rb;
    float yaw;    // ?????
    float pitch;  // ??????
    bool wantJump;

    int mouseLookSuspendCount;

    // 下蹲状态
    CapsuleCollider capsule;
    float standHeight;
    Vector3 standCenter;
    float cameraStandLocalY;
    bool hasCameraBaseline;
    bool isCrouching;
    float crouchT;        // 0 = 站立，1 = 完全下蹲
    float crouchTVel;

    /// <summary>
    /// ?????????????? WorkTable ??????????????????????????��?
    /// ?��???????????????????????
    /// </summary>
    public void PushMouseLookSuspend()
    {
        mouseLookSuspendCount++;
    }

    public void PopMouseLookSuspend()
    {
        if (mouseLookSuspendCount > 0) mouseLookSuspendCount--;
    }

    public bool MouseLookSuspended => mouseLookSuspendCount > 0;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        // ????????????????????????????????????????��
        rb.freezeRotation = true;

        // ??��???? cameraTransform????????????????
        if (cameraTransform == null)
        {
            var cam = GetComponentInChildren<Camera>();
            if (cam != null) cameraTransform = cam.transform;
        }

        // ????????????????????????
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // ?????????
        yaw = transform.eulerAngles.y;
        if (cameraTransform != null)
            pitch = cameraTransform.localEulerAngles.x;

        // 缓存下蹲所需的基线：胶囊原始尺寸 + 相机原始本地高度
        capsule = GetComponent<CapsuleCollider>();
        if (capsule != null)
        {
            standHeight = capsule.height;
            standCenter = capsule.center;
            if (crouchHeight > standHeight)
                crouchHeight = standHeight;
        }
        if (cameraTransform != null)
        {
            cameraStandLocalY = cameraTransform.localPosition.y;
            hasCameraBaseline = true;
        }
    }

    void Update()
    {
        if (GameplayInputGate.IsBlocked)
            return;

        // ??????? -> ???????
        if (!MouseLookSuspended)
        {
            float mx = Input.GetAxis("Mouse X") * mouseSensitivity;
            float my = Input.GetAxis("Mouse Y") * mouseSensitivity;

            yaw += mx;
            pitch -= my;
            pitch = Mathf.Clamp(pitch, -maxLookAngle, maxLookAngle);

            if (cameraTransform != null)
                cameraTransform.localEulerAngles = new Vector3(pitch, 0f, 0f);
        }

        // ???????????? FixedUpdate ???????????��?
        if (Input.GetButtonDown("Jump"))
            wantJump = true;

        UpdateCrouchState();

        // ??????��???????????? Esc ????
    }

    /// <summary>
    /// 处理下蹲输入：按住 crouchKey/crouchKeyAlt 进入下蹲，松开后若头顶有空间则起身；
    /// 同时平滑插值 crouchT 并更新胶囊体高度与相机本地 Y。
    /// </summary>
    void UpdateCrouchState()
    {
        bool wantCrouch = Input.GetKey(crouchKey) || Input.GetKey(crouchKeyAlt);

        if (wantCrouch)
        {
            isCrouching = true;
        }
        else if (isCrouching && CanStandUp())
        {
            isCrouching = false;
        }

        float target = isCrouching ? 1f : 0f;
        if (crouchTransitionSmoothTime <= 0f)
        {
            crouchT = target;
            crouchTVel = 0f;
        }
        else
        {
            crouchT = Mathf.SmoothDamp(crouchT, target, ref crouchTVel, crouchTransitionSmoothTime);
        }

        ApplyCrouchToCapsuleAndCamera();
    }

    void ApplyCrouchToCapsuleAndCamera()
    {
        if (capsule != null && standHeight > 0f)
        {
            float h = Mathf.Lerp(standHeight, crouchHeight, crouchT);
            capsule.height = h;

            // 顶部缩短、脚底位置保持不变：胶囊中心向下平移 (standHeight - h)/2
            Vector3 c = standCenter;
            c.y = standCenter.y - (standHeight - h) * 0.5f;
            capsule.center = c;
        }

        if (cameraTransform != null && hasCameraBaseline && standHeight > 0f)
        {
            // 相机本地 Y 跟随胶囊顶部一起下移
            float drop = (standHeight - (capsule != null ? capsule.height : standHeight));
            Vector3 p = cameraTransform.localPosition;
            p.y = cameraStandLocalY - drop;
            cameraTransform.localPosition = p;
        }
    }

    /// <summary>
    /// 在“站立尺寸”的胶囊位置上做一次 OverlapCapsule，若没有任何非自身的 Collider 重叠
    /// 即认为可以起身（头顶没有桌底、低矮天花板等遮挡）。
    /// </summary>
    bool CanStandUp()
    {
        if (capsule == null) return true;
        if (capsule.height >= standHeight - 1e-3f) return true;

        float halfStand = standHeight * 0.5f;
        float bottomYLocal = standCenter.y - (halfStand - capsule.radius);
        float topYLocal = standCenter.y + (halfStand - capsule.radius);
        Vector3 p1 = transform.position + Vector3.up * bottomYLocal;
        Vector3 p2 = transform.position + Vector3.up * topYLocal;
        float radius = Mathf.Max(0.01f, capsule.radius - standUpClearance);

        Collider[] hits = Physics.OverlapCapsule(p1, p2, radius, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider h = hits[i];
            if (h == null || h == capsule) continue;
            if (h.transform == transform || h.transform.IsChildOf(transform)) continue;
            return false;
        }
        return true;
    }

    public bool IsCrouching => isCrouching;

    /// <summary>外部重置玩家朝向（如关卡开始时），同步内部 yaw 避免被 FixedUpdate 覆盖。</summary>
    public void SetYaw(float degrees)
    {
        yaw = degrees;
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
        transform.rotation = rot;
        if (rb == null)
            rb = GetComponent<Rigidbody>();
        if (rb != null)
            rb.rotation = rot;
    }

    void FixedUpdate()
    {
        if (GameplayInputGate.IsBlocked)
            return;

        // ?????????????��?????????
        rb.MoveRotation(Quaternion.Euler(0f, yaw, 0f));

        // ????????????????????????
        float h = Input.GetAxisRaw("Horizontal"); // A/D
        float v = Input.GetAxisRaw("Vertical");   // W/S
        Vector3 forward = transform.forward;
        Vector3 right = transform.right;
        Vector3 moveDir = (forward * v + right * h).normalized;

        // ????
        float speed = moveSpeed;
        bool sprintHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        // 下蹲时禁用冲刺，并叠加下蹲速度倍率
        if (isCrouching)
            speed *= crouchSpeedMultiplier;
        else if (sprintHeld)
            speed *= sprintMultiplier;

        Vector3 targetPos = rb.position + moveDir * speed * Time.fixedDeltaTime;
        rb.MovePosition(targetPos);

        // ??????????????????��
        bool isGrounded = Physics.Raycast(transform.position, Vector3.down, GetGroundCheckDistance(), groundMask);

        // ??????????????????????（下蹲时禁用跳跃）
        if (wantJump && isGrounded && !isCrouching)
        {
            // ??????????????????????????????????????
            Vector3 vel = rb.velocity;
            vel.y = 0f;
            rb.velocity = vel;
            rb.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);
        }

        wantJump = false;
    }

    // ????????????????????? CapsuleCollider ??��
    float GetGroundCheckDistance()
    {
        var capsule = GetComponent<CapsuleCollider>();
        if (capsule != null)
        {
            // ??????????????????? + ��???
            float halfHeight = Mathf.Max(0f, capsule.height * 0.5f - capsule.radius);
            return halfHeight + capsule.radius + groundCheckDistance;
        }

        return 1f + groundCheckDistance;
    }
}