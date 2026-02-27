using System.Collections.Generic;

namespace Tools {
    public class CrawlerResult {
        public readonly List<ManagedObjectInstance> Objects = new();
        public readonly List<ReferenceEdge> References = new();
        public readonly Dictionary<int, List<int>> ObjectsByTypeIndex = new();
        public int TotalCrawled;
        public int TotalSkipped;
    }

    public class ManagedObjectInstance {
        public int ObjectIndex;
        public ulong Address;
        public int TypeIndex;
        public long Size;
        public bool IsGcRoot;
        public int GcHandleIndex = -1;
        public int NativeObjectIndex = -1;
    }

    public class ReferenceEdge {
        public int FromObjectIndex;
        public int ToObjectIndex;
        public string FieldName;
    }

    public class ReferenceChain {
        public readonly List<ReferenceChainNode> Nodes = new();

        public bool IsValid => Nodes.Count > 0;
    }

    public class ReferenceChainNode {
        public int ObjectIndex;
        public int TypeIndex;
        public string TypeName;
        public string FieldName;
        public bool IsGcRoot;
    }
}
