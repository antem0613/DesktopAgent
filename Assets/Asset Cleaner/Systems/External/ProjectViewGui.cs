using UnityEditor;
using UnityEngine;

namespace Asset_Cleaner {
    static class ProjectViewGui {
        public static void OnProjectWindowItemOnGui(string guid, Rect rect) {
            var config = Globals<Config>.Value;
            if (config == null || !config.MarkRed) return;

            var store = Globals<BacklinkStore>.Value;
            if (store == null || !store.Initialized) return;
            if (store.UnusedFiles == null || store.UnusedScenes == null) return;

            var wd = Globals<WindowData>.Value;
            if (wd?.Style == null) return;
            var style = wd.Style;

            var path = AssetDatabase.GUIDToAssetPath(guid);
            ShowRowQuantity(rect, path, store, style);

            long size = 0;
            var _ = store.UnusedFiles.TryGetValue(path, out size) || store.UnusedScenes.TryGetValue(path, out size);

            if (SearchUtils.IsUnused(path)) {
                var buf = GUI.color;
                {
                    GUI.color = style.RedHighlight;
                    GUI.Box(rect, string.Empty);
                }
                GUI.color = buf;
                GUI.Label(rect, CommonUtils.BytesToString(size), style.ProjectViewCounterLabel);
            }
        }


        static void ShowRowQuantity(Rect rect, string path, BacklinkStore backlinkStore, CleanerStyleAsset.Style style) {
            if (!AssetDatabase.IsValidFolder(path))
                return;

            backlinkStore.FoldersWithQty.TryGetValue(path, out var folderWithQty);

            var cntFiles = folderWithQty?.UnusedFilesQty ?? 0;
            var cntScenes = folderWithQty?.UnusedScenesQty ?? 0;
            long size = folderWithQty?.UnusedSize ?? 0;

            if (cntFiles == 0 && cntScenes == 0) return;
            var countStr = cntFiles + cntScenes > 0 ? $"{cntFiles} | {cntScenes} ({CommonUtils.BytesToString(size)})" : "";
            GUI.Label(rect, countStr, style.ProjectViewCounterLabel);
        }
    }
}