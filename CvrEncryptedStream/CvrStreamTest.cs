using System.Collections;
using System.IO;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zettai;

public class CvrStreamTest : MonoBehaviour
{
    static readonly ProfilerMarker s_CtorPerfMarker = new ProfilerMarker(ProfilerCategory.Scripts, "Zettai.CvrStream.Ctor");
    static readonly ProfilerMarker s_FileReadPerfMarker = new ProfilerMarker(ProfilerCategory.Scripts, "Zettai.CvrStream.FileRead");

    public string guid;
    public string path;
    public string keyFragB64;
    public bool run = false;
    private static int offset = 0;
    private CvrEncryptedStream stream;
    private void Update()
    {
        if (run)
        {
            run = false;
            StartCoroutine(LoadBundle());
        }
    }
    private IEnumerator LoadBundle()
    {
        yield return null;
        s_FileReadPerfMarker.Begin();
        var file = File.OpenRead(path);
        s_FileReadPerfMarker.End();
        yield return null;
        s_CtorPerfMarker.Begin();
        stream = new CvrEncryptedStream(guid, file, keyFragB64);
        s_CtorPerfMarker.End();
        yield return null;
        var bundleLoad = UnityEngine.AssetBundle.LoadFromStreamAsync(stream);
        yield return bundleLoad;
        if (!bundleLoad.isDone)
            yield break;
        
        var bundle = bundleLoad.assetBundle;
        var scenePaths = bundle.GetAllScenePaths();
        if (!bundle.isStreamedSceneAssetBundle || scenePaths == null || scenePaths.Length == 0)
        {
            var allAssets = bundle.LoadAllAssetsAsync();
            yield return allAssets;
            var instance = GameObject.Instantiate((GameObject)allAssets.asset);
            instance.transform.position = Vector3.zero + Vector3.right * offset;
            offset++;
            yield return bundle.UnloadAsync(false);
        }
        else
        {
            bundle.hideFlags = HideFlags.None;
            string sceneName = Path.GetFileNameWithoutExtension(scenePaths[0]);
            Debug.Log($"Loading scene '{sceneName}'...");
            yield return SceneManager.LoadSceneAsync(sceneName);
            var scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.Log("Failed to load scene.");
                bundle.Unload(true);
                yield break;
            }
            Debug.Log("Scene loaded.");
        }
    }
    private static byte[] ReadToMemory(string guid, Stream originalStream, string keyFragBase64) 
    {
        var stream = new CvrEncryptedStream(guid, originalStream, keyFragBase64);
        return stream.ReadAll();
    }
}
