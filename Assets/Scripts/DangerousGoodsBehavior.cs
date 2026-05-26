using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 危险品行为：
/// 1. 持续闪烁红光（通过 MaterialPropertyBlock 修改材质色与发光，不污染共享材质）。
/// 2. 玩家触碰（拾取或物理接触）后，剩余 <see cref="touchedFuseSeconds"/> 秒爆炸。
/// 3. 若玩家始终不接触，<see cref="untouchedFuseSeconds"/> 秒后爆炸。
/// 4. 爆炸会销毁自身以及 <see cref="explosionRadius"/> 范围内的其它物品。
/// </summary>
[DisallowMultipleComponent]
public class DangerousGoodsBehavior : MonoBehaviour
{
    [Header("引信")]
    [Tooltip("玩家从未触碰时的爆炸时长（秒）。")]
    public float untouchedFuseSeconds = 10f;

    [Tooltip("玩家触碰后的爆炸时长（秒）。")]
    public float touchedFuseSeconds = 5f;

    [Header("爆炸")]
    [Tooltip("爆炸波及范围（米）。")]
    public float explosionRadius = 2.5f;

    [Tooltip("对附近 Rigidbody 施加的爆炸冲量。0 表示不施加。")]
    public float explosionImpulse = 8f;

    [Tooltip("爆炸销毁判定层。")]
    public LayerMask affectedLayers = ~0;

    [Tooltip("是否只销毁带 ItemInformation 的物品（推荐开启，避免误伤场景固件）。")]
    public bool onlyAffectItems = true;

    [Header("视觉")]
    [Tooltip("基础闪烁色（叠加到材质 _Color/_BaseColor 上）。")]
    public Color blinkColor = new Color(1f, 0.1f, 0.1f, 1f);

    [Tooltip("发光强度倍率。需要材质支持 _EmissionColor。")]
    public float blinkEmissionIntensity = 3f;

    [Tooltip("闪烁的总周期（秒）。0 表示恒定红光。")]
    public float blinkInterval = 0.4f;

    [Tooltip("引信过半后闪烁加速比例（数值越小越快）。1 = 不加速。")]
    [Range(0.1f, 1f)]
    public float panicSpeedMultiplier = 0.3f;

    [Header("触碰检测")]
    [Tooltip("被玩家碰撞体撞到也视为触碰。需要玩家身上挂有 CharacterMove 或 CharacterInteraction。")]
    public bool detectPlayerCollision = true;

    [Header("调试")]
    public bool debugLog;

    float _fuseEndTime;
    bool _touched;
    bool _exploded;
    bool _initialized;

    Renderer[] _renderers;
    MaterialPropertyBlock _mpb;
    Color[] _originalColors;
    Color[] _originalEmissions;

    CharacterInteraction _character;
    static readonly Collider[] s_OverlapBuffer = new Collider[32];

    static readonly int kBaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int kColor = Shader.PropertyToID("_Color");
    static readonly int kEmissionColor = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        CacheRenderers();
    }

    void OnEnable()
    {
        _character = FindObjectOfType<CharacterInteraction>();
        if (_character != null)
            _character.Grabbed += HandleGrabbed;

        _fuseEndTime = Time.time + Mathf.Max(0.1f, untouchedFuseSeconds);
        _touched = false;
        _exploded = false;
        _initialized = true;
    }

    void OnDisable()
    {
        if (_character != null)
            _character.Grabbed -= HandleGrabbed;

        RestoreOriginalLook();
        _initialized = false;
    }

    void Update()
    {
        if (!_initialized || _exploded) return;

        UpdateBlink();

        if (Time.time >= _fuseEndTime)
            Explode();
    }

    void HandleGrabbed(GameObject obj)
    {
        if (obj == gameObject)
            MarkTouched("grabbed");
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!detectPlayerCollision || _touched || _exploded) return;
        if (collision == null || collision.collider == null) return;

        if (IsPlayerCollider(collision.collider))
            MarkTouched("collision");
    }

    void OnTriggerEnter(Collider other)
    {
        if (!detectPlayerCollision || _touched || _exploded) return;
        if (other == null) return;

        if (IsPlayerCollider(other))
            MarkTouched("trigger");
    }

    static bool IsPlayerCollider(Collider col)
    {
        if (col == null) return false;
        if (col.GetComponentInParent<CharacterMove>() != null) return true;
        if (col.GetComponentInParent<CharacterInteraction>() != null) return true;
        return false;
    }

    public void MarkTouched(string reason)
    {
        if (_touched || _exploded) return;
        _touched = true;
        float newEnd = Time.time + Mathf.Max(0.1f, touchedFuseSeconds);
        if (newEnd < _fuseEndTime)
            _fuseEndTime = newEnd;
        if (debugLog)
            Debug.Log($"[DangerousGoods] {name} touched ({reason}); explodes in {touchedFuseSeconds}s", this);
    }

    void CacheRenderers()
    {
        _renderers = GetComponentsInChildren<Renderer>(true);
        _mpb = new MaterialPropertyBlock();
        _originalColors = new Color[_renderers.Length];
        _originalEmissions = new Color[_renderers.Length];

        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer r = _renderers[i];
            if (r == null || r.sharedMaterial == null) continue;
            Material mat = r.sharedMaterial;
            _originalColors[i] = mat.HasProperty(kBaseColor) ? mat.GetColor(kBaseColor)
                : mat.HasProperty(kColor) ? mat.GetColor(kColor)
                : Color.white;
            _originalEmissions[i] = mat.HasProperty(kEmissionColor) ? mat.GetColor(kEmissionColor) : Color.black;
        }
    }

    void UpdateBlink()
    {
        if (_renderers == null || _renderers.Length == 0) return;

        float remaining = Mathf.Max(0f, _fuseEndTime - Time.time);
        float totalFuse = _touched ? touchedFuseSeconds : untouchedFuseSeconds;
        float urgency = totalFuse > 0f ? 1f - Mathf.Clamp01(remaining / totalFuse) : 1f;

        float interval = blinkInterval;
        if (interval > 0f)
            interval = Mathf.Lerp(blinkInterval, blinkInterval * panicSpeedMultiplier, urgency);

        float t = interval > 0f
            ? Mathf.PingPong(Time.time, interval) / interval
            : 1f;

        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer r = _renderers[i];
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);

            Color baseCol = Color.Lerp(_originalColors[i], blinkColor, t);
            if (r.sharedMaterial != null)
            {
                if (r.sharedMaterial.HasProperty(kBaseColor))
                    _mpb.SetColor(kBaseColor, baseCol);
                if (r.sharedMaterial.HasProperty(kColor))
                    _mpb.SetColor(kColor, baseCol);
                if (r.sharedMaterial.HasProperty(kEmissionColor))
                    _mpb.SetColor(kEmissionColor, blinkColor * (blinkEmissionIntensity * t));
            }

            r.SetPropertyBlock(_mpb);
        }
    }

    void RestoreOriginalLook()
    {
        if (_renderers == null) return;
        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer r = _renderers[i];
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            if (r.sharedMaterial != null)
            {
                if (r.sharedMaterial.HasProperty(kBaseColor))
                    _mpb.SetColor(kBaseColor, _originalColors[i]);
                if (r.sharedMaterial.HasProperty(kColor))
                    _mpb.SetColor(kColor, _originalColors[i]);
                if (r.sharedMaterial.HasProperty(kEmissionColor))
                    _mpb.SetColor(kEmissionColor, _originalEmissions[i]);
            }
            r.SetPropertyBlock(_mpb);
        }
    }

    void Explode()
    {
        if (_exploded) return;
        _exploded = true;

        if (debugLog)
            Debug.Log($"[DangerousGoods] {name} exploded at radius {explosionRadius}", this);

        Vector3 center = transform.position;

        if (_character != null && _character.IsHoldingObject)
            _character.ForceReleaseIfHolding(gameObject);

        if (CreditManager.Instance != null)
            CreditManager.Instance.ShowSubtitle("Dangerous goods detonated!", 1.5f, new Color(1f, 0.3f, 0.3f));

        HashSet<GameObject> toDestroy = new HashSet<GameObject> { gameObject };

        int count = Physics.OverlapSphereNonAlloc(
            center, explosionRadius, s_OverlapBuffer, affectedLayers, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider col = s_OverlapBuffer[i];
            if (col == null) continue;

            GameObject root = ResolveItemRoot(col);
            if (root == null) continue;
            if (root == gameObject) continue;
            if (onlyAffectItems && root.GetComponent<ItemInformation>() == null) continue;

            toDestroy.Add(root);
        }

        // 先施加冲量再销毁，让爆炸效果有视觉反馈
        foreach (GameObject go in toDestroy)
        {
            if (go == null) continue;
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic && explosionImpulse > 0f)
                rb.AddExplosionForce(explosionImpulse, center, explosionRadius, 0.3f, ForceMode.Impulse);
        }

        foreach (GameObject go in toDestroy)
        {
            if (go != null)
                Destroy(go);
        }
    }

    static GameObject ResolveItemRoot(Collider col)
    {
        if (col == null) return null;
        ItemInformation info = col.GetComponentInParent<ItemInformation>();
        if (info != null) return info.gameObject;

        // 没有 ItemInformation 时使用所在物体（仅用于非"only items"分支）
        return col.attachedRigidbody != null ? col.attachedRigidbody.gameObject : col.gameObject;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
    }
}
