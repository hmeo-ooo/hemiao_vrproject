using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ?????????????????????????????????????? AddForce ???????????
/// ??????????????????????????????????
/// - ?????????????Transform.forward / ??????? / ?????????
/// - ????????????????????????????
/// - ???? URP ?? _BaseMap ??? _MainTex ???????
/// </summary>
public class ConveyorBelt : MonoBehaviour
{
    public enum DirectionMode
    {
        TransformForward,
        FixedDirection,
        Path
    }

    [Tooltip("?????????m/s???????????????")]
    public float scrollSpeed = 1f;

    [Tooltip("?????????????????????????")]
    public float textureScrollMultiplier = 0.1f;

    [Tooltip("?????????????? V ??????????? U???????")]
    public bool scrollAlongV = false;

    [Tooltip("???????????")]
    public DirectionMode directionMode = DirectionMode.TransformForward;

    [Tooltip("FixedDirection ????????????????????????????????")]
    public Vector3 fixedDirection = Vector3.forward;

    [Header("Path ?????Path ????????")]
    [Tooltip("??????????????????? 2 ????????????????")]
    public Transform[] pathPoints;

    [Tooltip("???????????????????????????????????")]
    public bool loopPath = false;

    [Tooltip("?????????????????????????????????")]
    public bool invertPath = false;

    [Header("???????")]
    [Tooltip("???????????????????????? 0.08 ~ 0.12??????? 0 ??????????")]
    public float smoothTime = 0.1f;

    [Header("?????")]
    [Tooltip("??????????????????? transform.up ??????Q???????????????????????")]
    public bool useBeltUpAsPlaneNormal = true;

    [Tooltip("?????????????????????????????")]
    public bool suppressAngularVelocity = true;

    [Tooltip("????????????????????????????")]
    public bool freezeRotationWhileOnBelt = true;

    private readonly Dictionary<Rigidbody, ContactInfo> _bodyInfo = new Dictionary<Rigidbody, ContactInfo>();
    private readonly HashSet<Rigidbody> _suspendedBodies = new HashSet<Rigidbody>();

    private Renderer _renderer;
    private Material _material;
    private Vector2 _textureOffset = Vector2.zero;
    private string _texturePropertyName = null;
    private Vector3[] _cachedPathPositions;

    [SerializeField, HideInInspector]
    private bool _debugPrintCount = false;

    private class ContactInfo
    {
        public Vector3 contactNormal;
        public Vector3 smoothVelRef;
        public RigidbodyConstraints originalConstraints;
        public bool storedConstraints;
    }

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        if (_renderer != null)
        {
            _material = _renderer.material;
            if (_material == null)
            {
                Debug.LogWarning($"[{nameof(ConveyorBelt)}] ???????????????{name}");
            }
            else if (_material.HasProperty("_BaseMap"))
            {
                _texturePropertyName = "_BaseMap";
            }
            else if (_material.HasProperty("_MainTex"))
            {
                _texturePropertyName = "_MainTex";
            }
            else
            {
                Debug.LogWarning($"[{nameof(ConveyorBelt)}] ????????? _BaseMap ?? _MainTex??{name}");
                _texturePropertyName = null;
            }
        }
        else
        {
            Debug.LogWarning($"[{nameof(ConveyorBelt)}] ????? Renderer ?????{name}");
        }

        if (fixedDirection == Vector3.zero) fixedDirection = transform.forward;
        CachePathPositions();
    }

    private void OnValidate()
    {
        if (fixedDirection == Vector3.zero) fixedDirection = transform.forward;
        CachePathPositions();
    }

    private void CachePathPositions()
    {
        if (pathPoints == null || pathPoints.Length == 0)
        {
            _cachedPathPositions = null;
            return;
        }

        _cachedPathPositions = new Vector3[pathPoints.Length];
        for (int i = 0; i < pathPoints.Length; i++)
        {
            _cachedPathPositions[i] = pathPoints[i] != null ? pathPoints[i].position : Vector3.zero;
        }
    }

    private void FixedUpdate()
    {
        if (_bodyInfo.Count == 0) return;

        float fixedDt = Time.fixedDeltaTime;
        var bodies = new Rigidbody[_bodyInfo.Count];
        _bodyInfo.Keys.CopyTo(bodies, 0);

        foreach (var rb in bodies)
        {
            if (rb == null)
            {
                _bodyInfo.Remove(rb);
                continue;
            }

            if (_suspendedBodies.Contains(rb))
                continue;

            if (rb.isKinematic)
            {
                TryRemoveRigidbody(rb);
                continue;
            }

            ContactInfo info = _bodyInfo[rb];
            Vector3 currentVel = rb.velocity;
            float verticalSpeed = currentVel.y;

            Vector3 desiredDir = GetDesiredDirectionForRigidbody(rb);

            Vector3 planeNormal = useBeltUpAsPlaneNormal ? transform.up : info.contactNormal;
            if (planeNormal.sqrMagnitude < 1e-6f) planeNormal = transform.up;

            Vector3 tangent = Vector3.ProjectOnPlane(desiredDir, planeNormal);
            if (tangent.sqrMagnitude < 1e-6f)
                tangent = Vector3.ProjectOnPlane(transform.forward, planeNormal);
            tangent.Normalize();
            if (tangent.sqrMagnitude < 1e-6f) tangent = transform.forward;

            Vector3 targetHoriz = tangent * scrollSpeed;

            Vector3 newHoriz;
            if (smoothTime <= 0f)
            {
                newHoriz = targetHoriz;
                info.smoothVelRef = Vector3.zero;
            }
            else
            {
                Vector3 currentHoriz = Vector3.ProjectOnPlane(currentVel, planeNormal);
                newHoriz = Vector3.SmoothDamp(currentHoriz, targetHoriz, ref info.smoothVelRef, smoothTime, Mathf.Infinity, fixedDt);
            }

            rb.velocity = new Vector3(newHoriz.x, verticalSpeed, newHoriz.z);

            if (suppressAngularVelocity)
                rb.angularVelocity = Vector3.zero;

            if (_debugPrintCount)
                Debug.Log($"[ConveyorBelt] ???? {rb.name} newHoriz={newHoriz}");
        }
    }

    private Vector3 GetDesiredDirectionForRigidbody(Rigidbody rb)
    {
        if (directionMode == DirectionMode.FixedDirection)
        {
            if (fixedDirection == Vector3.zero) return transform.forward;
            return fixedDirection.normalized;
        }

        if (directionMode == DirectionMode.Path)
        {
            if (_cachedPathPositions == null || _cachedPathPositions.Length < 2) return transform.forward;

            Vector3 pos = rb.worldCenterOfMass;
            float bestDistSqr = float.PositiveInfinity;
            int bestIndex = 0;

            int segmentCount = loopPath ? _cachedPathPositions.Length : _cachedPathPositions.Length - 1;
            for (int i = 0; i < segmentCount; i++)
            {
                Vector3 a = _cachedPathPositions[i];
                Vector3 b = _cachedPathPositions[(i + 1) % _cachedPathPositions.Length];
                Vector3 ab = b - a;
                float abLenSqr = ab.sqrMagnitude;
                if (abLenSqr <= Mathf.Epsilon) continue;

                float t = Vector3.Dot(pos - a, ab) / abLenSqr;
                t = Mathf.Clamp01(t);
                Vector3 proj = a + t * ab;
                float distSqr = (pos - proj).sqrMagnitude;
                if (distSqr < bestDistSqr)
                {
                    bestDistSqr = distSqr;
                    bestIndex = i;
                }
            }

            Vector3 segStart = _cachedPathPositions[bestIndex];
            Vector3 segEnd = _cachedPathPositions[(bestIndex + 1) % _cachedPathPositions.Length];
            Vector3 pathTangent = (segEnd - segStart).normalized;
            if (pathTangent == Vector3.zero) return transform.forward;
            return invertPath ? -pathTangent : pathTangent;
        }

        return transform.forward;
    }

    private void Update()
    {
        if (_material == null || string.IsNullOrEmpty(_texturePropertyName)) return;

        float delta = scrollSpeed * textureScrollMultiplier * Time.deltaTime;
        if (scrollAlongV) _textureOffset.y += delta;
        else _textureOffset.x += delta;

        if (_textureOffset.x > 1000f) _textureOffset.x -= 1000f;
        if (_textureOffset.y > 1000f) _textureOffset.y -= 1000f;

        _material.SetTextureOffset(_texturePropertyName, _textureOffset);
    }

    public void SetSpeed(float newSpeed) => scrollSpeed = newSpeed;

    public void SetDirection(Vector3 worldDirection)
    {
        if (worldDirection == Vector3.zero) return;
        fixedDirection = worldDirection.normalized;
        directionMode = DirectionMode.FixedDirection;
    }

    public void SetPathPoints(Transform[] points, bool loop = false)
    {
        pathPoints = points;
        loopPath = loop;
        CachePathPositions();
        directionMode = DirectionMode.Path;
    }

    public void SetDirectionMode(DirectionMode mode)
    {
        directionMode = mode;
        if (mode != DirectionMode.Path)
            _cachedPathPositions = null;
        else
            CachePathPositions();
    }

    public void SuspendRigidbody(Rigidbody rb)
    {
        if (rb == null) return;
        TryRemoveRigidbody(rb);
        rb.constraints &= ~RigidbodyConstraints.FreezeRotation;
        rb.angularVelocity = Vector3.zero;
        _suspendedBodies.Add(rb);
    }

    public void UnsuspendRigidbody(Rigidbody rb)
    {
        if (rb == null) return;
        _suspendedBodies.Remove(rb);
    }

    #region ??? / ????

    private void OnCollisionEnter(Collision collision)
    {
        var rb = collision.rigidbody ?? collision.gameObject.GetComponent<Rigidbody>();
        TryAddRigidbody(rb);
        UpdateContactFromCollision(collision);
    }

    private void OnCollisionStay(Collision collision) => UpdateContactFromCollision(collision);

    private void OnCollisionExit(Collision collision)
    {
        var rb = collision.rigidbody ?? collision.gameObject.GetComponent<Rigidbody>();
        TryRemoveRigidbody(rb);
    }

    private void OnTriggerEnter(Collider other)
    {
        var rb = other.attachedRigidbody ?? other.GetComponent<Rigidbody>();
        TryAddRigidbody(rb);
        if (rb != null && _bodyInfo.ContainsKey(rb))
            _bodyInfo[rb].contactNormal = transform.up;
    }

    private void OnTriggerExit(Collider other)
    {
        var rb = other.attachedRigidbody ?? other.GetComponent<Rigidbody>();
        TryRemoveRigidbody(rb);
    }

    private void UpdateContactFromCollision(Collision collision)
    {
        var rb = collision.rigidbody ?? collision.gameObject.GetComponent<Rigidbody>();
        if (rb == null) return;
        if (!_bodyInfo.ContainsKey(rb)) TryAddRigidbody(rb);
        if (!_bodyInfo.ContainsKey(rb)) return;

        Vector3 avgNormal = Vector3.zero;
        for (int i = 0; i < collision.contactCount; i++)
            avgNormal += collision.GetContact(i).normal;

        avgNormal /= Mathf.Max(1, collision.contactCount);
        if (avgNormal.sqrMagnitude > 1e-6f)
            _bodyInfo[rb].contactNormal = avgNormal.normalized;
    }

    private void TryAddRigidbody(Rigidbody rb)
    {
        if (rb == null || rb.isKinematic) return;
        if (_suspendedBodies.Contains(rb)) return;
        if (_bodyInfo.ContainsKey(rb)) return;

        var info = new ContactInfo
        {
            contactNormal = transform.up,
            smoothVelRef = Vector3.zero
        };

        if (freezeRotationWhileOnBelt)
        {
            info.originalConstraints = rb.constraints;
            info.storedConstraints = true;
            rb.constraints |= RigidbodyConstraints.FreezeRotation;
        }

        _bodyInfo.Add(rb, info);
    }

    private void TryRemoveRigidbody(Rigidbody rb)
    {
        if (rb == null) return;

        if (_bodyInfo.TryGetValue(rb, out ContactInfo info) && info.storedConstraints && freezeRotationWhileOnBelt)
            rb.constraints = info.originalConstraints;

        _bodyInfo.Remove(rb);
    }

    #endregion

    private void OnDestroy()
    {
        if (_material == null) return;
#if UNITY_EDITOR
        DestroyImmediate(_material);
#else
        Destroy(_material);
#endif
    }

    private void OnDrawGizmosSelected()
    {
        if (pathPoints == null || pathPoints.Length == 0) return;

        Gizmos.color = Color.cyan;
        for (int i = 0; i < pathPoints.Length; i++)
        {
            if (pathPoints[i] == null) continue;
            Gizmos.DrawSphere(pathPoints[i].position, 0.05f);
            int next = i + 1;
            if (next < pathPoints.Length)
            {
                if (pathPoints[next] != null)
                    Gizmos.DrawLine(pathPoints[i].position, pathPoints[next].position);
            }
            else if (loopPath && pathPoints[0] != null)
            {
                Gizmos.DrawLine(pathPoints[i].position, pathPoints[0].position);
            }
        }
    }
}
