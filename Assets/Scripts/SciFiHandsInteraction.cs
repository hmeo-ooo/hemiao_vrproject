using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Animator))]
public class SciFiHandsInteraction : MonoBehaviour
{
    [Header("引用")]
    public CharacterInteraction interaction;
    public Transform cameraTransform;

    [Header("动画片段名（从 Animator Controller 自动匹配）")]
    public string idleClipKey = "Idle";
    public string grabClipKey = "Get";
    public string holdClipKey = "Idle_Fight";
    public string throwClipKey = "Attack_01";
    public string scrollUpClipKey = "Attack_02";
    public string scrollDownClipKey = "Attack_03";

    [Header("双手交错（滚轮）")]
    public float handStaggerOffset = 0.06f;
    public float handStaggerSmooth = 10f;

    [Header("视角同步")]
    [Tooltip("手部挂在摄像机下时应关闭；挂在角色身上时才需要同步俯仰。")]
    public bool syncPitchToCamera = false;

    Animator animator;
    readonly Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();

    AnimationClip idleClip;
    AnimationClip grabClip;
    AnimationClip holdClip;
    AnimationClip throwClip;
    AnimationClip scrollUpClip;
    AnimationClip scrollDownClip;

    Transform leftHandHub;
    Transform rightHandHub;
    Vector3 leftHubRest;
    Vector3 rightHubRest;
    float leftHubYOffset;
    float rightHubYOffset;
    int staggerSign;

    string activeClipKey;
    bool activeLoop;
    bool isHolding;
    Coroutine gestureRoutine;

    void Awake()
    {
        animator = GetComponent<Animator>();
        CacheClips();
        ResolveBones();

        if (interaction == null)
            interaction = GetComponentInParent<CharacterInteraction>();

        if (cameraTransform == null && interaction != null)
            cameraTransform = interaction.cameraTransform;

        if (cameraTransform == null)
        {
            var cam = GetComponentInParent<CharacterMove>()?.cameraTransform;
            if (cam == null)
            {
                var childCam = GetComponentInParent<CharacterInteraction>()?.GetComponentInChildren<Camera>();
                if (childCam != null) cameraTransform = childCam.transform;
            }
            else
            {
                cameraTransform = cam;
            }
        }
    }

    void OnEnable()
    {
        if (interaction != null)
        {
            interaction.Grabbed += OnGrabbed;
            interaction.Released += OnReleased;
            interaction.Thrown += OnThrown;
            interaction.ScrollWheel += OnScrollWheel;
        }

        PlayLoop(idleClipKey);
    }

    void OnDisable()
    {
        if (interaction != null)
        {
            interaction.Grabbed -= OnGrabbed;
            interaction.Released -= OnReleased;
            interaction.Thrown -= OnThrown;
            interaction.ScrollWheel -= OnScrollWheel;
        }
    }

    void Start()
    {
        PlayLoop(idleClipKey);
    }

    void Update()
    {
        if (interaction == null) return;

        if (interaction.IsGrabCharging && !isHolding && activeClipKey != grabClipKey)
            PlayOnce(grabClipKey, idleClipKey);

        if (interaction.IsHoldingObject && !isHolding)
        {
            isHolding = true;
            PlayLoop(holdClipKey);
        }
        else if (!interaction.IsHoldingObject && isHolding)
        {
            isHolding = false;
            staggerSign = 0;
            PlayLoop(idleClipKey);
        }

        MaintainHoldPose();
    }

    void LateUpdate()
    {
        SyncCameraPitch();
        ApplyHandStagger();
    }

    void CacheClips()
    {
        clips.Clear();
        if (animator == null || animator.runtimeAnimatorController == null) return;

        var controllerClips = animator.runtimeAnimatorController.animationClips;
        for (int i = 0; i < controllerClips.Length; i++)
        {
            AnimationClip clip = controllerClips[i];
            if (clip == null) continue;

            string key = ExtractClipKey(clip.name);
            if (!clips.ContainsKey(key))
                clips.Add(key, clip);
        }

        idleClip = GetClip(idleClipKey);
        grabClip = GetClip(grabClipKey);
        holdClip = GetClip(holdClipKey);
        throwClip = GetClip(throwClipKey);
        scrollUpClip = GetClip(scrollUpClipKey);
        scrollDownClip = GetClip(scrollDownClipKey);
    }

    void ResolveBones()
    {
        leftHandHub = FindDeepChild(transform, "CATRigHub001");
        rightHandHub = FindDeepChild(transform, "CATRigHub002");

        if (leftHandHub != null) leftHubRest = leftHandHub.localPosition;
        if (rightHandHub != null) rightHubRest = rightHandHub.localPosition;
    }

    void OnGrabbed(GameObject _)
    {
        isHolding = true;
        PlayOnce(grabClipKey, holdClipKey);
    }

    void OnReleased()
    {
        isHolding = false;
        staggerSign = 0;
        PlayLoop(idleClipKey);
    }

    void OnThrown()
    {
        isHolding = false;
        staggerSign = 0;
        PlayOnce(throwClipKey, idleClipKey);
    }

    void OnScrollWheel(float scroll)
    {
        if (!isHolding) return;

        staggerSign = scroll > 0f ? 1 : -1;
        string gestureKey = scroll > 0f ? scrollUpClipKey : scrollDownClipKey;
        PlayOnce(gestureKey, holdClipKey);
    }

    void PlayLoop(string clipKey)
    {
        if (animator == null || string.IsNullOrEmpty(clipKey)) return;

        activeClipKey = clipKey;
        activeLoop = true;
        animator.CrossFade(clipKey, 0.12f, 0, 0f);
    }

    void PlayOnce(string clipKey, string followClipKey)
    {
        if (animator == null || string.IsNullOrEmpty(clipKey)) return;

        activeClipKey = clipKey;
        activeLoop = false;
        animator.CrossFade(clipKey, 0.1f, 0, 0f);

        if (gestureRoutine != null)
            StopCoroutine(gestureRoutine);
        gestureRoutine = StartCoroutine(PlayOnceRoutine(clipKey, followClipKey));
    }

    IEnumerator PlayOnceRoutine(string clipKey, string followClipKey)
    {
        AnimationClip clip = GetClip(clipKey);
        float duration = clip != null ? clip.length : 0.35f;
        yield return new WaitForSeconds(duration * 0.95f);

        if (interaction != null && interaction.IsHoldingObject)
            PlayLoop(holdClipKey);
        else
            PlayLoop(followClipKey);

        gestureRoutine = null;
    }

    void MaintainHoldPose()
    {
        if (!activeLoop || !isHolding || animator == null) return;

        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        if (info.IsName(holdClipKey)) return;

        animator.CrossFade(holdClipKey, 0.08f, 0);
        activeClipKey = holdClipKey;
    }

    void SyncCameraPitch()
    {
        if (!syncPitchToCamera || cameraTransform == null) return;
        if (transform.parent == cameraTransform) return;

        Vector3 euler = transform.localEulerAngles;
        euler.x = cameraTransform.localEulerAngles.x;
        transform.localEulerAngles = euler;
    }

    void ApplyHandStagger()
    {
        if (leftHandHub == null || rightHandHub == null) return;

        float targetLeft = staggerSign > 0 ? handStaggerOffset : (staggerSign < 0 ? -handStaggerOffset : 0f);
        float targetRight = -targetLeft;

        float step = handStaggerSmooth * Time.deltaTime;
        leftHubYOffset = Mathf.MoveTowards(leftHubYOffset, targetLeft, step);
        rightHubYOffset = Mathf.MoveTowards(rightHubYOffset, targetRight, step);

        leftHandHub.localPosition = leftHubRest + Vector3.up * leftHubYOffset;
        rightHandHub.localPosition = rightHubRest + Vector3.up * rightHubYOffset;
    }

    AnimationClip GetClip(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        clips.TryGetValue(key, out AnimationClip clip);
        return clip;
    }

    static string ExtractClipKey(string clipName)
    {
        if (string.IsNullOrEmpty(clipName)) return string.Empty;

        int at = clipName.LastIndexOf('@');
        return at >= 0 ? clipName.Substring(at + 1) : clipName;
    }

    static Transform FindDeepChild(Transform root, string childName)
    {
        if (root == null) return null;
        if (root.name == childName) return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeepChild(root.GetChild(i), childName);
            if (found != null) return found;
        }

        return null;
    }
}
