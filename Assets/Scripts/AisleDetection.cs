using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class AisleDetection : MonoBehaviour
{
    [Tooltip("\u672C\u901A\u9053\u63A5\u53D7\u7684\u7269\u54C1\u7C7B\u522B")]
    public ItemInformation.ItemCategory aisleCategory = ItemInformation.ItemCategory.Metal;

    [Tooltip("\u901F\u5EA6\u5927\u4E8E\u8BE5\u9608\u503C\u65F6\u624D\u89C6\u4E3A\u6295\u63B7\u7269")]
    public float minImpactSpeed = 1f;

    [Tooltip("\u5206\u7C7B\u6B63\u786E\u65F6\u662F\u5426\u9500\u6BC1\u7269\u54C1")]
    public bool consumeItemOnAccept = true;

    [Tooltip("\u662F\u5426\u5728\u63A7\u5236\u53F0\u8F93\u51FA\u8C03\u8BD5\u65E5\u5FD7")]
    public bool debugLogging = true;

    HashSet<int> processedInstanceIds = new HashSet<int>();

    void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other == null) return;

        Rigidbody rb = other.attachedRigidbody ?? other.GetComponentInParent<Rigidbody>();
        if (rb == null)
        {
            if (debugLogging) Debug.Log($"[AisleDetection] {other.name}: no rigidbody, ignored.");
            return;
        }

        float speed = rb.velocity.magnitude;
        if (speed < minImpactSpeed)
        {
            if (debugLogging) Debug.Log($"[AisleDetection] {other.name} speed {speed:F2} < {minImpactSpeed:F2}, ignored.");
            return;
        }

        Cuttable cuttable = other.GetComponentInParent<Cuttable>();
        if (cuttable != null && cuttable.IsStillAssembled)
        {
            int assemblyId = cuttable.gameObject.GetInstanceID();
            if (processedInstanceIds.Contains(assemblyId))
            {
                if (debugLogging) Debug.Log($"[AisleDetection] {cuttable.name} assembly already processed.");
                return;
            }

            MarkAssemblyProcessed(cuttable.gameObject);
            if (debugLogging)
                Debug.Log($"[AisleDetection] {cuttable.name} abandoned mixture, +{cuttable.abandonedMixtureCredits} credits.");

            cuttable.HandleAbandonedMixtureInAisle();
            return;
        }

        var info = other.GetComponentInParent<ItemInformation>();
        if (info == null)
        {
            if (debugLogging) Debug.Log($"[AisleDetection] {other.name}: no ItemInformation, ignored.");
            return;
        }

        int id = info.gameObject.GetInstanceID();
        if (processedInstanceIds.Contains(id))
        {
            if (debugLogging) Debug.Log($"[AisleDetection] {info.gameObject.name} already processed, ignored.");
            return;
        }

        if (debugLogging)
            Debug.Log($"[AisleDetection] hit {info.gameObject.name} category={info.category} aisle={aisleCategory} speed={speed:F2}");

        if (info.category == aisleCategory)
        {
            if (debugLogging)
                Debug.Log($"[AisleDetection] match, +{info.creditsOnCorrectThrow} credits.");
            if (CreditManager.Instance != null && info.creditsOnCorrectThrow != 0)
                CreditManager.Instance.AddCredits(info.creditsOnCorrectThrow);

            if (consumeItemOnAccept)
                Destroy(info.gameObject);
        }
        else
        {
            if (debugLogging) Debug.Log($"[AisleDetection] mismatch, error subtitle. expected {aisleCategory}, got {info.category}.");
            if (CreditManager.Instance != null)
                CreditManager.Instance.ShowSubtitle("error", 2f);
            Destroy(info.gameObject);
        }

        processedInstanceIds.Add(id);
    }

    void MarkAssemblyProcessed(GameObject assemblyRoot)
    {
        processedInstanceIds.Add(assemblyRoot.GetInstanceID());
        ItemInformation[] parts = assemblyRoot.GetComponentsInChildren<ItemInformation>(true);
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] != null)
                processedInstanceIds.Add(parts[i].gameObject.GetInstanceID());
        }
    }
}
