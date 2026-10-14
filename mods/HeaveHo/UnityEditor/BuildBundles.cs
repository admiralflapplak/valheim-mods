// Copy this file into your Unity project at Assets/Editor/BuildBundles.cs
// (not part of the mod build). Then use the menu: Assets -> Build AssetBundles.
using UnityEditor;

public static class BuildBundles
{
    [MenuItem("Assets/Build AssetBundles")]
    private static void Build()
    {
        System.IO.Directory.CreateDirectory("AssetBundles");
        BuildPipeline.BuildAssetBundles("AssetBundles",
            BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
        EditorUtility.RevealInFinder("AssetBundles/heaveho");
    }
}
