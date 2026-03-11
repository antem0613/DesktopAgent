using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditorInternal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Asset_Cleaner {
    class BacklinkStore {
        public bool Initialized { get; private set; }

        public Dictionary<string, long> UnusedFiles { get; private set; }
        public Dictionary<string, long> UnusedScenes { get; private set; }
        public Dictionary<string, BwMeta> Backward { get; private set; }
        public Dictionary<string, UnusedQty> FoldersWithQty { get; private set; }

        Dictionary<string, FwMeta> _forward;
        HashSet<string> _folders;

        // Incremental processing
        List<string> _pendingPaths;
        int _processedCount;
        int _totalCount;
        int _progressId = -1;

        // Disk cache
        static string CachePath => "Library/AssetCleanerCache.bin";
        const string CacheVersion = "1.50";
        int _currentPathCount;

        // Cache loading
        Task<CacheReadResult> _cacheReadTask;

        // Incremental path filtering
        string[] _allRawPaths;
        int _pathFilterIndex;

        // Incremental UpdateUnusedAssets
        int _updatePhase;
        string[] _unusedCandidates;
        int _updateIndex;
        List<string> _scenesList;
        List<string> _filesList;
        string[] _foldersArray;

        class CacheReadResult {
            public bool Success;
            public int PathCount;
            public string Version;
            public Dictionary<string, FwMeta> Forward;
            public Dictionary<string, BwMeta> Backward;
            public HashSet<string> Folders;
            public Dictionary<string, long> UnusedFiles;
            public Dictionary<string, long> UnusedScenes;
            public Dictionary<string, UnusedQty> FoldersWithQty;
        }

        /// <summary>
        /// True while paths are being processed in background.
        /// </summary>
        public bool IsProcessingInBackground =>
            (_cacheReadTask != null && !_cacheReadTask.IsCompleted) ||
            (_allRawPaths != null && _pathFilterIndex < _allRawPaths.Length) ||
            (_pendingPaths != null && _processedCount < _pendingPaths.Count) ||
            _updatePhase > 0;

        /// <summary>
        /// Returns progress (0.0 to 1.0).
        /// </summary>
        public float ProcessingProgress {
            get {
                if (!IsProcessingInBackground)
                    return 1f;
                // Filtering paths
                if (_allRawPaths != null && _allRawPaths.Length > 0)
                    return (float)_pathFilterIndex / _allRawPaths.Length;
                // Building cache
                if (_pendingPaths != null && _totalCount > 0)
                    return (float)_processedCount / _totalCount;
                // Update phases
                if (_updatePhase == 1 && _unusedCandidates != null && _unusedCandidates.Length > 0)
                    return (float)_updateIndex / _unusedCandidates.Length;
                if (_updatePhase == 2 && _filesList != null && _filesList.Count > 0)
                    return (float)_updateIndex / _filesList.Count;
                if (_updatePhase == 3 && _scenesList != null && _scenesList.Count > 0)
                    return (float)_updateIndex / _scenesList.Count;
                if (_updatePhase == 4 && _foldersArray != null && _foldersArray.Length > 0)
                    return (float)_updateIndex / _foldersArray.Length;
                return 0f;
            }
        }

        /// <summary>
        /// Returns current processing status message.
        /// </summary>
        public string ProcessingStatus {
            get {
                if (!IsProcessingInBackground)
                    return null;
                if (_cacheReadTask != null)
                    return "Loading cache...";
                if (_allRawPaths != null)
                    return $"Filtering... {_pathFilterIndex}/{_allRawPaths.Length}";
                if (_pendingPaths != null)
                    return $"Building cache... {_processedCount}/{_totalCount}";
                if (_updatePhase > 0)
                    return $"Finalizing... ({_updatePhase}/4)";
                return null;
            }
        }

        public void Init() {
            var comparer = StringComparer.Ordinal;
            FoldersWithQty = new Dictionary<string, UnusedQty>(comparer);
            _forward = new Dictionary<string, FwMeta>(comparer);
            Backward = new Dictionary<string, BwMeta>(comparer);
            _folders = new HashSet<string>(StringComparer.Ordinal);
            UnusedFiles = new Dictionary<string, long>(comparer);
            UnusedScenes = new Dictionary<string, long>(comparer);

            _allRawPaths = AssetDatabase.GetAllAssetPaths();
            _currentPathCount = _allRawPaths.Length;

            _cacheReadTask = Task.Run(() => TryReadCacheFile());
            _pathFilterIndex = 0;
            _pendingPaths = null;
            _processedCount = 0;
            _totalCount = 0;
            Initialized = true;
        }

        /// <summary>
        /// Processes paths incrementally. Call this from EditorApplication.update.
        /// Returns true while processing is in progress.
        /// </summary>
        public bool ProcessIncremental(double frameBudgetMs = 16.0) {
            bool stillProcessing = DoProcessIncremental(frameBudgetMs);

            // Update Progress indicator (unified)
            var status = ProcessingStatus;
            if (status != null && stillProcessing) {
                var progress = ProcessingProgress;
                if (_progressId < 0) {
                    var options = Progress.Options.Managed;
                    if (progress <= 0f)
                        options |= Progress.Options.Indefinite;
                    _progressId = Progress.Start("Asset Cleaner", status, options);
                }
                else {
                    Progress.Report(_progressId, progress, status);
                }
            }
            else if (_progressId >= 0) {
                Progress.Remove(_progressId);
                _progressId = -1;
            }

            if (!stillProcessing) {
                EditorApplication.RepaintProjectWindow();
                InternalEditorUtility.RepaintAllViews();
            }

            return stillProcessing;
        }

        bool DoProcessIncremental(double frameBudgetMs) {
            // Wait for cache read first
            if (_cacheReadTask != null) {
                if (!_cacheReadTask.IsCompleted)
                    return true;

                var cacheResult = _cacheReadTask.Result;
                _cacheReadTask = null;

                // Quick check: path count and version must match
                if (cacheResult != null && cacheResult.Success &&
                    cacheResult.PathCount == _currentPathCount &&
                    cacheResult.Version == "v3") {
                    // Cache valid - just assign pre-built structures!
                    _forward = cacheResult.Forward;
                    Backward = cacheResult.Backward;
                    _folders = cacheResult.Folders;
                    UnusedFiles = cacheResult.UnusedFiles;
                    UnusedScenes = cacheResult.UnusedScenes;
                    FoldersWithQty = cacheResult.FoldersWithQty;
                    _allRawPaths = null;
                    return false; // Done!
                }
            }

            // Update unused assets phase
            if (_updatePhase > 0)
                return ProcessUpdateUnusedAssets(frameBudgetMs);

            // Filtering paths incrementally (cache miss)
            if (_allRawPaths != null)
                return ProcessPathFiltering(frameBudgetMs);

            // Building fresh
            if (_pendingPaths == null || _processedCount >= _pendingPaths.Count)
                return false;

            var startTime = EditorApplication.timeSinceStartup;
            var budgetSec = frameBudgetMs / 1000.0;

            while (_processedCount < _pendingPaths.Count) {
                var path = _pendingPaths[_processedCount];
                _FillFwAndBacklinks(path);

                var folders = GetAllFoldersFromPath(path);
                foreach (var folder in folders)
                    _folders.Add(folder);

                _processedCount++;

                if (EditorApplication.timeSinceStartup - startTime >= budgetSec)
                    break;
            }

            if (_processedCount >= _pendingPaths.Count) {
                _pendingPaths = null;
                StartUpdateUnusedAssets();
            }

            return true;
        }

        bool ProcessPathFiltering(double frameBudgetMs) {
            var defaultAss = typeof(DefaultAsset);
            var asmdefAss = typeof(AssemblyDefinitionAsset);

            if (_pendingPaths == null)
                _pendingPaths = new List<string>();

            var startTime = EditorApplication.timeSinceStartup;
            var budgetSec = frameBudgetMs / 1000.0;

            while (_pathFilterIndex < _allRawPaths.Length) {
                var path = _allRawPaths[_pathFilterIndex];
                _pathFilterIndex++;

                if (!path.StartsWith("Assets", StringComparison.OrdinalIgnoreCase) &&
                    !path.StartsWith("ProjectSettings", StringComparison.OrdinalIgnoreCase) &&
                    !path.StartsWith("Packages", StringComparison.OrdinalIgnoreCase))
                    continue;

                var t = AssetDatabase.GetMainAssetTypeAtPath(path);
                if (t != null && (t.IsAssignableFromInverse(defaultAss) || t.IsAssignableFromInverse(asmdefAss)))
                    continue;

                _pendingPaths.Add(path);

                if (EditorApplication.timeSinceStartup - startTime >= budgetSec)
                    break;
            }

            if (_pathFilterIndex >= _allRawPaths.Length) {
                _allRawPaths = null;
                _totalCount = _pendingPaths.Count;
                _processedCount = 0;
                return true; // Continue to building phase
            }

            return true;
        }

        void StartUpdateUnusedAssets() {
            var all = new HashSet<string>(_forward.Keys);
            var withBacklinks = new HashSet<string>(
                Backward.Where(kv => kv.Value.Lookup.Count > 0).Select(kv => kv.Key));
            all.ExceptWith(withBacklinks);
            all.RemoveWhere(SearchUtils.IsFileIgrnoredBySettings);

            _unusedCandidates = all.ToArray();
            _scenesList = new List<string>();
            _filesList = new List<string>();
            _updateIndex = 0;
            _updatePhase = 1;

            UnusedFiles = new Dictionary<string, long>();
            UnusedScenes = new Dictionary<string, long>();
        }

        bool ProcessUpdateUnusedAssets(double frameBudgetMs) {
            var startTime = EditorApplication.timeSinceStartup;
            var budgetSec = frameBudgetMs / 1000.0;

            // Phase 1: Separate scenes from files (GetMainAssetTypeAtPath is slow)
            if (_updatePhase == 1) {
                var sceneType = typeof(SceneAsset);
                while (_updateIndex < _unusedCandidates.Length) {
                    var path = _unusedCandidates[_updateIndex];
                    var assetType = AssetDatabase.GetMainAssetTypeAtPath(path);
                    if (assetType != null && assetType.IsAssignableFromInverse(sceneType))
                        _scenesList.Add(path);
                    else
                        _filesList.Add(path);

                    _updateIndex++;
                    if (EditorApplication.timeSinceStartup - startTime >= budgetSec)
                        return true;
                }
                _updateIndex = 0;
                _updatePhase = 2;
            }

            // Phase 2: Get file sizes (disk I/O)
            if (_updatePhase == 2) {
                while (_updateIndex < _filesList.Count) {
                    var file = _filesList[_updateIndex];
                    try { UnusedFiles[file] = new FileInfo(file).Length; }
                    catch { UnusedFiles[file] = 0; }

                    _updateIndex++;
                    if (EditorApplication.timeSinceStartup - startTime >= budgetSec)
                        return true;
                }
                _updateIndex = 0;
                _updatePhase = 3;
            }

            // Phase 3: Get scene sizes
            if (_updatePhase == 3) {
                while (_updateIndex < _scenesList.Count) {
                    var scene = _scenesList[_updateIndex];
                    try { UnusedScenes[scene] = new FileInfo(scene).Length; }
                    catch { UnusedScenes[scene] = 0; }

                    _updateIndex++;
                    if (EditorApplication.timeSinceStartup - startTime >= budgetSec)
                        return true;
                }
                _updateIndex = 0;
                _updatePhase = 4;
            }

            // Phase 4: Calculate folder quantities
            if (_updatePhase == 4) {
                if (_foldersArray == null)
                    _foldersArray = _folders.ToArray();

                while (_updateIndex < _foldersArray.Length) {
                    var folder = _foldersArray[_updateIndex];

                    int unusedFilesQty = 0;
                    long filesSize = 0;
                    foreach (var kvp in UnusedFiles) {
                        if (kvp.Key.StartsWith(folder) &&
                            (kvp.Key.Length == folder.Length || kvp.Key[folder.Length] == '/')) {
                            unusedFilesQty++;
                            filesSize += kvp.Value;
                        }
                    }

                    int unusedScenesQty = 0;
                    long scenesSize = 0;
                    foreach (var kvp in UnusedScenes) {
                        if (kvp.Key.StartsWith(folder) &&
                            (kvp.Key.Length == folder.Length || kvp.Key[folder.Length] == '/')) {
                            unusedScenesQty++;
                            scenesSize += kvp.Value;
                        }
                    }

                    FoldersWithQty[folder] = new UnusedQty(unusedFilesQty, unusedScenesQty, filesSize + scenesSize);

                    _updateIndex++;
                    if (EditorApplication.timeSinceStartup - startTime >= budgetSec)
                        return true;
                }

                // Done!
                _updatePhase = 0;
                _unusedCandidates = null;
                _scenesList = null;
                _filesList = null;
                _foldersArray = null;

                SaveCache(); // Save after all data is computed
                return false;
            }

            return false;
        }

        /// <summary>
        /// Cancels processing and cleans up Progress indicator.
        /// </summary>
        public void CancelProcessing() {
            if (_progressId >= 0) {
                Progress.Remove(_progressId);
                _progressId = -1;
            }
            _pendingPaths = null;
            _cacheReadTask = null;
            _allRawPaths = null;
            _updatePhase = 0;
            _unusedCandidates = null;
            _scenesList = null;
            _filesList = null;
            _foldersArray = null;
        }

        #region Disk Cache

        public static void ClearCache() {
            try {
                if (File.Exists(CachePath))
                    File.Delete(CachePath);
            }
            catch (Exception e) {
                Debug.LogWarning($"[AssetCleaner] Failed to clear cache: {e.Message}");
            }
        }

        void SaveCache() {
            var forward = _forward;
            var backward = Backward;
            var folders = _folders;
            var unusedFiles = UnusedFiles;
            var unusedScenes = UnusedScenes;
            var foldersWithQty = FoldersWithQty;
            var pathCount = _currentPathCount;

            Task.Run(() => {
                try {
                    using (var fs = File.Create(CachePath))
                    using (var bw = new BinaryWriter(fs, Encoding.UTF8)) {
                        bw.Write(pathCount);
                        bw.Write("v3"); // Version marker

                        // Forward links
                        bw.Write(forward.Count);
                        foreach (var kvp in forward) {
                            bw.Write(kvp.Key);
                            var deps = kvp.Value.Dependencies;
                            bw.Write(deps.Count);
                            foreach (var dep in deps)
                                bw.Write(dep);
                        }

                        // Backward links
                        bw.Write(backward.Count);
                        foreach (var kvp in backward) {
                            bw.Write(kvp.Key);
                            var lookup = kvp.Value.Lookup;
                            bw.Write(lookup.Count);
                            foreach (var item in lookup)
                                bw.Write(item);
                        }

                        // Folders
                        bw.Write(folders.Count);
                        foreach (var f in folders)
                            bw.Write(f);

                        // Unused files (v3)
                        bw.Write(unusedFiles.Count);
                        foreach (var kvp in unusedFiles) {
                            bw.Write(kvp.Key);
                            bw.Write(kvp.Value);
                        }

                        // Unused scenes (v3)
                        bw.Write(unusedScenes.Count);
                        foreach (var kvp in unusedScenes) {
                            bw.Write(kvp.Key);
                            bw.Write(kvp.Value);
                        }

                        // Folders with qty (v3)
                        bw.Write(foldersWithQty.Count);
                        foreach (var kvp in foldersWithQty) {
                            bw.Write(kvp.Key);
                            bw.Write(kvp.Value.UnusedFilesQty);
                            bw.Write(kvp.Value.UnusedScenesQty);
                            bw.Write(kvp.Value.UnusedSize);
                        }
                    }
                }
                catch (Exception ex) {
                    Debug.LogError($"[AssetCleaner] Cache save error: {ex.Message}");
                }
            });
        }

        CacheReadResult TryReadCacheFile() {
            var result = new CacheReadResult { Success = false };
            try {
                if (!File.Exists(CachePath)) return result;

                var comparer = StringComparer.Ordinal;

                using (var fs = File.OpenRead(CachePath))
                using (var br = new BinaryReader(fs, Encoding.UTF8)) {
                    var pathCount = br.ReadInt32();
                    var version = br.ReadString();
                    result.PathCount = pathCount;
                    result.Version = version;

                    // Forward links -> Dictionary<string, FwMeta>
                    var forwardCount = br.ReadInt32();
                    var forward = new Dictionary<string, FwMeta>(comparer);
                    for (int i = 0; i < forwardCount; i++) {
                        var key = br.ReadString();
                        var depCount = br.ReadInt32();
                        var deps = new HashSet<string>(comparer);
                        for (int j = 0; j < depCount; j++)
                            deps.Add(br.ReadString());
                        forward[key] = new FwMeta { Dependencies = deps };
                    }

                    // Backward links (v3) -> Dictionary<string, BwMeta>
                    Dictionary<string, BwMeta> backward = null;
                    if (version == "v3") {
                        var backwardCount = br.ReadInt32();
                        backward = new Dictionary<string, BwMeta>(comparer);
                        for (int i = 0; i < backwardCount; i++) {
                            var key = br.ReadString();
                            var lookupCount = br.ReadInt32();
                            var lookup = new HashSet<string>(comparer);
                            for (int j = 0; j < lookupCount; j++)
                                lookup.Add(br.ReadString());
                            backward[key] = new BwMeta { Lookup = lookup };
                        }
                    }

                    // Folders -> HashSet<string>
                    var foldersCount = br.ReadInt32();
                    var folders = new HashSet<string>(comparer);
                    for (int i = 0; i < foldersCount; i++)
                        folders.Add(br.ReadString());

                    // Unused files (v3+)
                    Dictionary<string, long> unusedFiles = null;
                    Dictionary<string, long> unusedScenes = null;
                    Dictionary<string, UnusedQty> foldersWithQty = null;
                    if (version == "v3") {
                        var unusedFilesCount = br.ReadInt32();
                        unusedFiles = new Dictionary<string, long>(comparer);
                        for (int i = 0; i < unusedFilesCount; i++) {
                            var key = br.ReadString();
                            var size = br.ReadInt64();
                            unusedFiles[key] = size;
                        }

                        var unusedScenesCount = br.ReadInt32();
                        unusedScenes = new Dictionary<string, long>(comparer);
                        for (int i = 0; i < unusedScenesCount; i++) {
                            var key = br.ReadString();
                            var size = br.ReadInt64();
                            unusedScenes[key] = size;
                        }

                        var foldersWithQtyCount = br.ReadInt32();
                        foldersWithQty = new Dictionary<string, UnusedQty>(comparer);
                        for (int i = 0; i < foldersWithQtyCount; i++) {
                            var key = br.ReadString();
                            var filesQty = br.ReadInt32();
                            var scenesQty = br.ReadInt32();
                            var size = br.ReadInt64();
                            foldersWithQty[key] = new UnusedQty(filesQty, scenesQty, size);
                        }
                    }

                    result.Success = true;
                    result.Forward = forward;
                    result.Backward = backward;
                    result.Folders = folders;
                    result.UnusedFiles = unusedFiles;
                    result.UnusedScenes = unusedScenes;
                    result.FoldersWithQty = foldersWithQty;
                    return result;
                }
            }
            catch (Exception ex) {
                Debug.LogError($"[AssetCleaner] Cache read error: {ex.Message}");
                return result;
            }
        }

        #endregion

        void _FillFwAndBacklinks(string path) {
            var dependencies = _Dependencies(path);
            var hs = new FwMeta {Dependencies = new HashSet<string>(dependencies)};
            _forward.Add(path, hs);
            foreach (var backPath in dependencies) {
                if (!Backward.TryGetValue(backPath, out var val)) {
                    val = new BwMeta();
                    val.Lookup = new HashSet<string>();
                    Backward.Add(backPath, val);
                }

                val.Lookup.Add(path);
            }
        }


        void UpdateFoldersWithQtyByPath(string path) {
            var folders = GetAllFoldersFromPath(path);
            foreach (var folder in folders)
                _folders.Add(folder);
        }


        static List<string> GetAllFoldersFromPath(string p) {
            var result = new List<string>();
            var i = p.IndexOf('/', 0);
            while (i > 0) {
                var item = p.Substring(0, i);
                result.Add(item);
                i = p.IndexOf('/', i + 1);
            }

            return result.Distinct().ToList();
        }

        public void UpdateUnusedAssets() {
            var all = new HashSet<string>(_forward.Keys);
            var withBacklinks =
                new HashSet<string>(Backward.Where(kv => kv.Value.Lookup.Count > 0).Select(kv => kv.Key));

            all.ExceptWith(withBacklinks);
            all.RemoveWhere(SearchUtils.IsFileIgrnoredBySettings);

            var unusedAssets = all;

            var scenes = unusedAssets.Where(s =>
                AssetDatabase.GetMainAssetTypeAtPath(s).IsAssignableFromInverse(typeof(SceneAsset))).ToArray();

            unusedAssets.ExceptWith(scenes);
            var files = unusedAssets;
            UnusedFiles = new Dictionary<string, long>();
            foreach (var file in files) UnusedFiles[file] = new FileInfo(file).Length;

            UnusedScenes = new Dictionary<string, long>();
            foreach (var scene in scenes) UnusedScenes[scene] = new FileInfo(scene).Length;

            // UpdateFoldersWithQty();
            foreach (var folder in _folders) {
                var unusedFilesQty = UnusedFiles.Count(p => p.Key.StartsWith(folder));
                var unusedScenesQty = UnusedScenes.Count(p => p.Key.StartsWith(folder));
                long size = 0;
                size = UnusedFiles.Where((p => p.Key.StartsWith(folder))).Sum(p => p.Value);
                size += UnusedScenes.Where(p => p.Key.StartsWith(folder)).Sum(p => p.Value);

                FoldersWithQty.TryGetValue(folder, out var folderWithQty);
                if (folderWithQty == null) {
                    FoldersWithQty.Add(folder, new UnusedQty(unusedFilesQty, unusedScenesQty, size));
                }
                else {
                    folderWithQty.UnusedFilesQty = unusedFilesQty;
                    folderWithQty.UnusedScenesQty = unusedScenesQty;
                    folderWithQty.UnusedSize = size;
                }
            }

            // Refresh all windows
            EditorApplication.RepaintProjectWindow();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }


        public void Remove(string path) {
            if (!_forward.TryGetValue(path, out var fwMeta))
                return;

            foreach (var dependency in fwMeta.Dependencies) {
                if (!Backward.TryGetValue(dependency, out var dep)) continue;

                dep.Lookup.Remove(path);
            }

            _forward.Remove(path);
            UpdateFoldersWithQtyByPath(path);
        }

        public void Replace(string src, string dest) {
            _Upd(_forward);
            _Upd(Backward);
            UpdateFoldersWithQtyByPath(dest);

            void _Upd<T>(Dictionary<string, T> dic) {
                if (!dic.TryGetValue(src, out var refs)) return;

                dic.Remove(src);
                dic.Add(dest, refs);
            }
        }

        public void RebuildFor(string path, bool remove) {
            if (!_forward.TryGetValue(path, out var fwMeta)) {
                fwMeta = new FwMeta();
                _forward.Add(path, fwMeta);
            }
            else if (remove) {
                foreach (var dependency in fwMeta.Dependencies) {
                    if (!Backward.TryGetValue(dependency, out var backDep)) continue;

                    backDep.Lookup.Remove(path);
                }

                fwMeta.Dependencies = null;
            }

            var dependencies = _Dependencies(path);
            fwMeta.Dependencies = new HashSet<string>(dependencies);

            foreach (var backPath in dependencies) {
                if (!Backward.TryGetValue(backPath, out var bwMeta)) {
                    bwMeta = new BwMeta {Lookup = new HashSet<string>()};
                    Backward.Add(backPath, bwMeta);
                }
                else if (remove)
                    bwMeta.Lookup.Remove(path);

                bwMeta.Lookup.Add(path);
            }

            if (!remove) {
                UpdateFoldersWithQtyByPath(path);
            }
        }


        static string[] _Dependencies(string s) {
            // Only ProjectSettings uses SerializedProperty traversal
            if (!s.StartsWith("ProjectSettings", StringComparison.Ordinal))
                return AssetDatabase.GetDependencies(s, false);

            var obj = LoadAllOrMain(s)[0];
            return GetDependenciesManualPaths().ToArray();

            Object[] LoadAllOrMain(string assetPath) {
                // prevents error "Do not use readobjectthreaded on scene objects!"
                return typeof(SceneAsset) == AssetDatabase.GetMainAssetTypeAtPath(assetPath)
                    ? new[] {AssetDatabase.LoadMainAssetAtPath(assetPath)}
                    : AssetDatabase.LoadAllAssetsAtPath(assetPath);
            }

            IEnumerable<string> GetDependenciesManualPaths() {
                if (obj is EditorBuildSettings) {
                    foreach (var scene in EditorBuildSettings.scenes)
                        yield return scene.path;
                }

                if (!obj) {
                    yield break;
                }
                
                using (var so = new SerializedObject(obj)) {
                    var props = so.GetIterator();
                    int depth = 0;
                    const int maxDepth = 10_000;

                    while (props.Next(true) && depth++ < maxDepth) {
                        switch (props.propertyType) {
                            case SerializedPropertyType.ObjectReference:
                                var propsObjectReferenceValue = props.objectReferenceValue;
                                if (!propsObjectReferenceValue) continue;

                                var assetPath = AssetDatabase.GetAssetPath(propsObjectReferenceValue);
                                yield return assetPath;
                                break;
#if later
                        case SerializedPropertyType.Generic:
                        case SerializedPropertyType.ExposedReference:
                        case SerializedPropertyType.ManagedReference:
                            break;
#endif
                            default:
                                continue;
                        }
                    }

                }
            }
        }


        class FwMeta {
            public HashSet<string> Dependencies;
        }

        public class BwMeta {
            public HashSet<string> Lookup;
        }

        public class UnusedQty {
            public int UnusedFilesQty;
            public int UnusedScenesQty;

            public long UnusedSize;

            public UnusedQty() {
                Init(0, 0, 0);
            }

            public UnusedQty(int unusedFilesQty, int unusedScenesQty, long unusedSize) {
                Init(unusedFilesQty, unusedScenesQty, unusedSize);
            }

            private void Init(int unusedFilesQty, int unusedScenesQty, long unusedSize) {
                UnusedFilesQty = unusedFilesQty;
                UnusedScenesQty = unusedScenesQty;
                UnusedSize = unusedSize;
            }
        }
    }
}