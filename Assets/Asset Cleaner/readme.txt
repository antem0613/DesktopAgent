Asset Cleaner Pro - Quick Start Guide
1. Import the Asset
Open the Package Manager in Unity (Window > Package Manager), select My Assets, search for "Asset Cleaner Pro," and click Download then Import.

2. Open the Tool
Once imported, go to the top menu bar and navigate to: Window > Asset Cleaner Pro
- Note: Selecting this will open the main dockable dashboard and activate the real-time project scanning features.

3. What’s Next: How to Use
A. Enhanced Project View (Real-time Analysis)
As soon as the tool is active, your Project Window will be enhanced:
- Red Marking: All unused assets and scenes are automatically highlighted in RED, making them easy to spot at a glance.
- Size & Counters: Folders will now display the number of unused assets they contain and their total disk size, helping you identify which folders are "bloating" your project.

B. Cleaning Unused Assets
To perform a bulk cleanup, use the Asset Cleaner Pro window:
	1. Review the List: The window displays all detected unused assets.
	2. Selective Cleaning: You can select specific folders or multiselect individual assets you want to remove.
	3. Perform Cleanup: Click the Clean button (trash icon) to safely remove the selected files.

C. Finding References
To see exactly where an asset is being used before you decide to delete it:
	1. Right-Click any asset in your Project view.
	2. Select Find References.
	3. A window will pop up showing every Scene, Prefab, and AnimatorController that references that asset. This works instantly even for nested prefabs!

D. Customization (Settings)
If there are assets you never want to delete (like specific materials or "Resources" folders):
- Open the Settings tab in the Asset Cleaner Pro window.
- Add folders to the Ignored Folders list.
- Toggle the "Ignore Materials" or "Ignore ScriptableObjects" options if you want the tool to skip those specific types.

Pro Tips:
- Dock the Window: For the best experience, dock the Asset Cleaner Pro window next to your Inspector or Console so you can monitor project size in real-time.
- Safe Deletion: Always make sure your scenes are saved before a bulk clean to ensure the tool has the most up-to-date reference data!

Additional materials:
- FAQ: https://docs.google.com/document/d/1RZA8Rf3QHdzTq6HFa7moA0G9W1uZjXOrSMJ25QwaNgk
- On the Product page, you can find the video showcase of how to use the asset https://assetstore.unity.com/packages/tools/utilities/asset-cleaner-pro-clean-find-references-167990
- If you have any question or bug report - or just want to discuss your use-cases with fellow Asset Cleaner users - visit our Discord channel  https://discord.gg/qqmd2fES