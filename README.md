# MemGraph Analyzer

macOS `.memgraph` file analyzer for Unity iOS memory profiling.

## Features

- **Heap Analysis**: Allocation class breakdown with size distribution buckets
- **Virtual Memory Mapping**: vmmap region analysis with dirty/swapped memory tracking
- **Leak Detection**: Automated leak detection with root cycle identification
- **Call Tree Tracing**: malloc_history inverted call tree with IL2CPP demangling
- **Per-Address Drill-Down**: Trace top allocations back to exact call stacks
- **Unity Categorization**: Automatic grouping by Unity subsystem (Rendering, Scripting, Audio, etc.)
- **Export**: CSV and text report export

## Requirements

- **macOS only** (uses `heap`, `vmmap`, `leaks`, `malloc_history` system tools)
- Unity 6000.0+
- `.memgraph` files captured from iOS devices via Xcode

## Installation

### Via Git URL (Unity Package Manager)

In Unity, go to **Window > Package Manager > + > Add package from git URL**:

```
https://github.com/breadpack/memgraph-analyzer.git?path=Packages/dev.breadpack.memgraph-analyzer
```

### Manual (Local Package)

Clone this repository and open the root folder as a Unity project:

```bash
git clone https://github.com/breadpack/memgraph-analyzer.git
```

## Usage

1. Open **Tools > MemGraph Analyzer** in Unity Editor
2. Select a `.memgraph` file (captured via Xcode from an iOS device)
3. Click **Analyze**
4. Browse tabs: Summary, Heap, Vmmap, Leaks, Unity Categories

### Capturing .memgraph files

In Xcode:
1. Run your app on a real iOS device
2. Open **Debug > Simulate Memory Warning** or wait for high memory usage
3. **Debug Navigator > Memory** (or use Instruments)
4. **File > Export Memory Graph...**

## Project Structure

```
Packages/dev.breadpack.memgraph-analyzer/
└── Editor/
    ├── MemGraphAnalyzerWindow.cs          # Main EditorWindow + pipeline
    ├── MemGraphAnalyzerWindow.HeapTab.cs  # Heap allocations tab
    ├── MemGraphAnalyzerWindow.HeapTrace.cs # Per-address drill-down
    ├── MemGraphAnalyzerWindow.SummaryTab.cs
    ├── MemGraphAnalyzerWindow.VmmapTab.cs
    ├── MemGraphAnalyzerWindow.LeaksTab.cs
    ├── MemGraphAnalyzerWindow.UnityTab.cs
    ├── MemGraphAnalyzerWindow.Export.cs
    ├── MemGraphModels.cs                  # Data models
    ├── MemGraphCommandRunner.cs           # Process execution
    ├── HeapParser.cs                      # heap command parser
    ├── CallTreeParser.cs                  # malloc_history parser
    ├── AddressTraceParser.cs              # Per-address trace parser
    ├── LeaksParser.cs                     # leaks command parser
    └── VmmapParser.cs                     # vmmap command parser
```

## License

MIT
