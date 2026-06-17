using System.Collections.Generic;
using UnityEngine;

namespace Hemiao.Rendering
{
    /// <summary>
    /// 全局描边订阅表。游戏逻辑通过 <see cref="Register"/> 注册需要描边的 Renderer
    /// 与颜色，由 <see cref="JumpFloodOutlineFeature"/> 在 URP 渲染流程中合成屏幕空间描边。
    /// </summary>
    public static class JumpFloodOutlineRegistry
    {
        public readonly struct Entry
        {
            public readonly Renderer Renderer;
            public readonly Color Color;
            /// <summary>同组 Renderer 合并为一个 silhouette，避免相邻物体描边接死。</summary>
            public readonly int GroupId;

            public Entry(Renderer renderer, Color color, int groupId)
            {
                Renderer = renderer;
                Color = color;
                GroupId = groupId;
            }
        }

        static readonly Dictionary<int, Entry> s_Entries = new Dictionary<int, Entry>(64);
        static readonly List<Entry> s_Snapshot = new List<Entry>(64);
        static readonly List<int> s_PurgeBuffer = new List<int>(16);

        public static int Count => s_Entries.Count;

        public static void Register(Renderer renderer, Color color, int groupId = 0)
        {
            if (!JumpFloodOutlineFeature.SystemEnabled) return;
            if (renderer == null) return;
            if (groupId == 0) groupId = renderer.GetInstanceID();
            s_Entries[renderer.GetInstanceID()] = new Entry(renderer, color, groupId);
        }

        public static void Unregister(Renderer renderer)
        {
            if (renderer == null) return;
            s_Entries.Remove(renderer.GetInstanceID());
        }

        public static void Clear() => s_Entries.Clear();

        internal static List<Entry> GetSnapshot()
        {
            s_Snapshot.Clear();
            s_PurgeBuffer.Clear();

            foreach (KeyValuePair<int, Entry> kv in s_Entries)
            {
                Renderer r = kv.Value.Renderer;
                if (r == null)
                {
                    s_PurgeBuffer.Add(kv.Key);
                    continue;
                }

                if (!r.enabled || !r.gameObject.activeInHierarchy)
                    continue;

                s_Snapshot.Add(kv.Value);
            }

            for (int i = 0; i < s_PurgeBuffer.Count; i++)
                s_Entries.Remove(s_PurgeBuffer[i]);

            return s_Snapshot;
        }

        public static int RegisterAllRenderers(GameObject root, Color color, List<Renderer> outAdded = null, int groupId = 0)
        {
            if (!JumpFloodOutlineFeature.SystemEnabled) return 0;
            if (root == null) return 0;
            if (groupId == 0) groupId = root.GetInstanceID();

            int n = 0;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer rd = renderers[i];
                if (rd == null || !rd.enabled || !rd.gameObject.activeInHierarchy) continue;
                if (rd is ParticleSystemRenderer or TrailRenderer or LineRenderer) continue;

                Register(rd, color, groupId);
                outAdded?.Add(rd);
                n++;
            }

            return n;
        }

        public static void UnregisterRange(List<Renderer> renderers)
        {
            if (renderers == null) return;
            for (int i = 0; i < renderers.Count; i++)
                Unregister(renderers[i]);
        }
    }
}
