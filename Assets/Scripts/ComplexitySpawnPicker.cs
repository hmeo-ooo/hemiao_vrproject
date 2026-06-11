using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 按 <see cref="LevelComplexityComposition"/> 从候选 prefab 索引池中抽取一件。
/// <see cref="ItemSpawner"/> 与 <see cref="TrashHeap"/> 共用。
/// </summary>
public class ComplexitySpawnPicker
{
    readonly List<int> _bucketBasic = new List<int>();
    readonly List<int> _bucketComposite = new List<int>();
    readonly List<int> _bucketDangerous = new List<int>();
    bool _dirty = true;
    int _entryCount;
    Func<int, GameObject> _resolvePrefab;

    public void Configure(Func<int, GameObject> resolvePrefab, int entryCount)
    {
        _resolvePrefab = resolvePrefab;
        _entryCount = Mathf.Max(0, entryCount);
        _dirty = true;
    }

    public void Invalidate() => _dirty = true;

    public GameObject PickPrefab(LevelComplexityComposition composition, Predicate<int> isEntryAvailable = null)
    {
        int index = PickEntryIndex(composition, isEntryAvailable);
        if (index < 0) return null;
        return _resolvePrefab(index);
    }

    public int PickEntryIndex(LevelComplexityComposition composition, Predicate<int> isEntryAvailable = null)
    {
        if (_entryCount <= 0 || _resolvePrefab == null) return -1;
        if (isEntryAvailable == null) isEntryAvailable = _ => true;

        EnsureBuckets();

        if (composition == null || !composition.HasAnyProbability)
            return PickUniform(isEntryAvailable);

        if (composition.TryPickComplexity(
                complexity => BucketHasAvailable(complexity, isEntryAvailable),
                out ItemInformation.ItemComplexity pick))
        {
            int index = PickFromBucket(GetBucket(pick), isEntryAvailable);
            if (index >= 0) return index;
        }

        return PickUniform(isEntryAvailable);
    }

    int PickFromBucket(List<int> bucket, Predicate<int> isEntryAvailable)
    {
        int availableCount = 0;
        for (int i = 0; i < bucket.Count; i++)
        {
            if (isEntryAvailable(bucket[i]))
                availableCount++;
        }

        if (availableCount <= 0) return -1;

        int choice = UnityEngine.Random.Range(0, availableCount);
        for (int i = 0; i < bucket.Count; i++)
        {
            int index = bucket[i];
            if (!isEntryAvailable(index)) continue;
            if (choice == 0) return index;
            choice--;
        }

        return -1;
    }

    int PickUniform(Predicate<int> isEntryAvailable)
    {
        int availableCount = 0;
        for (int i = 0; i < _entryCount; i++)
        {
            if (_resolvePrefab(i) == null) continue;
            if (!isEntryAvailable(i)) continue;
            availableCount++;
        }

        if (availableCount <= 0) return -1;

        int choice = UnityEngine.Random.Range(0, availableCount);
        for (int i = 0; i < _entryCount; i++)
        {
            if (_resolvePrefab(i) == null) continue;
            if (!isEntryAvailable(i)) continue;
            if (choice == 0) return i;
            choice--;
        }

        return -1;
    }

    bool BucketHasAvailable(ItemInformation.ItemComplexity complexity, Predicate<int> isEntryAvailable)
    {
        List<int> bucket = GetBucket(complexity);
        for (int i = 0; i < bucket.Count; i++)
        {
            if (isEntryAvailable(bucket[i]))
                return true;
        }
        return false;
    }

    List<int> GetBucket(ItemInformation.ItemComplexity complexity)
    {
        switch (complexity)
        {
            case ItemInformation.ItemComplexity.Basic: return _bucketBasic;
            case ItemInformation.ItemComplexity.Composite: return _bucketComposite;
            case ItemInformation.ItemComplexity.Dangerous: return _bucketDangerous;
            default: return _bucketBasic;
        }
    }

    void EnsureBuckets()
    {
        if (!_dirty) return;
        _dirty = false;

        _bucketBasic.Clear();
        _bucketComposite.Clear();
        _bucketDangerous.Clear();

        for (int i = 0; i < _entryCount; i++)
        {
            GameObject prefab = _resolvePrefab(i);
            if (prefab == null) continue;

            ItemInformation info = prefab.GetComponent<ItemInformation>();
            ItemInformation.ItemComplexity complexity = info != null
                ? info.complexity
                : ItemInformation.ItemComplexity.Basic;
            GetBucket(complexity).Add(i);
        }
    }
}
