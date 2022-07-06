using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using UnityEngine.Rendering;
using System.Threading.Tasks;

public class ImportHints 
{
    static string[] hints = new string[]
    {
        "-prefab",
        "-inactive",
        "-reflectionprobe",
        "-lightprobe",
        "-reverbzone",
        "-triggerzone",
        "-volume"
    };

    const float lightProbeInitialSize = 2f; // 2 meters

    static List<GameObject> GetAllGameObjects()
    {
        var gameObjects = Resources.FindObjectsOfTypeAll(typeof(GameObject)) as GameObject[];
        var validGameObjects = gameObjects.ToList().Where(o => o.scene.IsValid()).ToList();
        return validGameObjects;
    }

    static string StripAllHints(string name)
    {
        return Regex.Replace(name, string.Join("|", hints), "");
    }

    // Generic method to sync all sorts of objects
    static void SyncObjects
    (
        string containerName,
        string hint,
        bool syncScale = true,
        Action<GameObject, GameObject> onCreateObject = null,
        Action<GameObject, GameObject> onUpdateObject = null,
        Func<GameObject, GameObject, GameObject> createObjectOverride = null
    )
    {
        var gameObjects = GetAllGameObjects();
        foreach (var obj in gameObjects)
        {
            // If a container, remove derived objects if their source is gone
            if (obj.name == containerName)
            {
                var parent = obj.transform.parent;
                // Convert to a list so that we can destroy objects while iterating
                foreach (Transform t in obj.transform.Cast<Transform>().ToList())
                {
                    var go = t.gameObject;
                    var children = parent.Cast<Transform>().ToList();

                    var sourceFound = children.Any(g =>
                    {
                        return g.name.Contains(hint) && StripAllHints(g.name) == go.name;
                    });

                    if (!sourceFound)
                    {
                        UnityEngine.Object.DestroyImmediate(go);
                    }
                }
                // Destroy container if it's empty
                if (obj.transform.childCount == 0)
                {
                    UnityEngine.Object.DestroyImmediate(obj);
                }
            }
            // If not a container, look for a hint
            else if (obj.name.Contains(hint))
            {
                var parent = obj.transform.parent;

                // Find or create a container
                var container = parent.Find(containerName)?.gameObject;
                if (container == null)
                {
                    container = new GameObject();
                    container.name = containerName;
                    container.transform.SetParent(parent);
                }

                GameObject derivedObject = null;

                if (createObjectOverride == null)
                {
                    var derivedObjectName = StripAllHints(obj.name);

                    // Check if an object with such name already exists
                    derivedObject = container.transform.Find(derivedObjectName)?.gameObject;
                    if (derivedObject == null)
                    {
                        // Create a new object
                        derivedObject = new GameObject();
                        derivedObject.name = derivedObjectName;
                        derivedObject.transform.SetParent(container.transform, false);
                        onCreateObject?.Invoke(obj, derivedObject);
                        obj.SetActive(false);
                    }
                }
                else
                {
                    derivedObject = createObjectOverride.Invoke(obj, container);
                    if (derivedObject == null) continue;
                }

                // Sync position
                var transform = obj.transform;
                derivedObject.transform.position = transform.position;
                derivedObject.transform.rotation = transform.rotation;
                if (syncScale)
                {
                    derivedObject.transform.localScale = transform.localScale;
                }

                onUpdateObject?.Invoke(obj, derivedObject);
            }
        }
    }

    // Convenient shortcut to sync everything in the project
    [MenuItem("Tools/Import Hints/Sync Everything")]
    public static void SyncEverything()
    {
        SyncPrefabs();
        SyncInactive();
        SyncReflectionProbes();
        SyncLightProbes();
        SyncReverbZones();
        SyncTriggerZones();
        SyncVolumes();
    }

    // Make prefabs out of all of the objects with number suffixes
    [MenuItem("Tools/Import Hints/Sync Prefabs")]
    public static void SyncPrefabs()
    {
        var prefabGuids = AssetDatabase.FindAssets("t:Prefab");
        var prefabPaths = prefabGuids.Select(AssetDatabase.GUIDToAssetPath);
        var prefabs = prefabPaths.Select(AssetDatabase.LoadAssetAtPath<GameObject>).ToList();

        SyncObjects("Synced Prefabs", "-prefab",
        createObjectOverride: (GameObject sourceObject, GameObject container) =>
        {
            var derivedObjectName = sourceObject.name.Replace("-prefab", "");

            // Check if an object with such name already exists
            var derivedObject = container.transform.Find(derivedObjectName)?.gameObject;
            if (derivedObject == null)
            {
                // Create a new object
                var prefab = prefabs.Find(p => {
                    return p.name.ToLower() == Regex.Replace(derivedObjectName, @"\.\d+", "").ToLower();
                });

                if (prefab == null) return null;

                derivedObject = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                derivedObject.name = derivedObjectName;
                derivedObject.transform.SetParent(container.transform, false);
                sourceObject.SetActive(false);
            }
            return derivedObject;
        }
        );
    }

    [MenuItem("Tools/Import Hints/Sync Inactive")]
    public static void SyncInactive()
    {
        var gameObjects = GetAllGameObjects();
        foreach (var obj in gameObjects)
        {
            if (obj.name.Contains("-inactive"))
            {
                obj.SetActive(false);
            }
        }
    }

    [MenuItem("Tools/Import Hints/Sync Reflection Probes")]
    public static void SyncReflectionProbes()
    {
        SyncObjects("Synced Reflection Probes", "-reflectionprobe",
        syncScale: false,
        onCreateObject: (GameObject sourceObject, GameObject derivedObject) =>
        {
            derivedObject.AddComponent<ReflectionProbe>();
        },
        onUpdateObject: (GameObject sourceObject, GameObject derivedObject) =>
        {
            var bounds = sourceObject.GetComponent<MeshFilter>().sharedMesh.bounds;
            var probe = derivedObject.GetComponent<ReflectionProbe>();
            var sourceScale = sourceObject.transform.localScale;
            // Probe size should have positive values
            var size = new Vector3(
                bounds.size.x * Mathf.Abs(sourceScale.x),
                bounds.size.y * Mathf.Abs(sourceScale.y),
                bounds.size.z * Mathf.Abs(sourceScale.z)
            );
            // "Rotate" the size to make sure it's properly aligned
            probe.size = sourceObject.transform.rotation * size;
        }
        );
    }

    [MenuItem("Tools/Import Hints/Sync Light Probes")]
    public static void SyncLightProbes()
    {
        SyncObjects("Synced Light Probes", "-lightprobe",
        syncScale: false,
        onCreateObject: (GameObject sourceObject, GameObject derivedObject) =>
        {
            derivedObject.AddComponent<LightProbeGroup>();
        },
        onUpdateObject: (GameObject sourceObject, GameObject derivedObject) =>
        {
            var bounds = sourceObject.GetComponent<MeshFilter>().sharedMesh.bounds;
            var probe = derivedObject.GetComponent<LightProbeGroup>();
            var sourceScale = sourceObject.transform.localScale;
            // Probe scale should have positive values and
            // take into account initial probe size
            var scale = new Vector3(
                bounds.size.x * Mathf.Abs(sourceScale.x),
                bounds.size.y * Mathf.Abs(sourceScale.y),
                bounds.size.z * Mathf.Abs(sourceScale.z)
            );
            derivedObject.transform.localScale = scale / lightProbeInitialSize;
        }
        );
    }

    [MenuItem("Tools/Import Hints/Sync Reverb Zones")]
    public static void SyncReverbZones()
    {
        SyncObjects("Synced Reverb Zones", "-reverbzone",
        syncScale: false,
        onCreateObject: (GameObject sourceObject, GameObject derivedObject) =>
        {
            derivedObject.AddComponent<AudioReverbZone>();
        },
        onUpdateObject: (GameObject sourceObject, GameObject derivedObject) =>
        {
            var bounds = sourceObject.GetComponent<MeshFilter>().sharedMesh.bounds;
            var reverbZone = derivedObject.GetComponent<AudioReverbZone>();
            var sourceScale = sourceObject.transform.localScale;
            var distance = Mathf.Max(new float[] {
                bounds.extents.x * sourceScale.x,
                bounds.extents.y * sourceScale.y,
                bounds.extents.z * sourceScale.z
            });
            reverbZone.minDistance = distance;
            reverbZone.maxDistance = reverbZone.minDistance * 1.2f;
        }
        );
    }

    [MenuItem("Tools/Import Hints/Sync Trigger Zones")]
    public static void SyncTriggerZones()
    {
        SyncObjects("Synced Trigger Zones", "-triggerzone",
        onCreateObject: (GameObject sourceObject, GameObject derivedObject) =>
        {
            var collider = derivedObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
        },
        onUpdateObject: (GameObject sourceObject, GameObject derivedObject) =>
        {
            var bounds = sourceObject.GetComponent<MeshFilter>().sharedMesh.bounds;
            var collider = derivedObject.GetComponent<BoxCollider>();
            collider.size = bounds.size;
        }
        );
    }

    [MenuItem("Tools/Import Hints/Sync Volumes")]
    public static void SyncVolumes()
    {
        SyncObjects("Synced Volumes", "-volume",
        onCreateObject: (GameObject sourceObject, GameObject derivedObject) =>
        {
            var collider = derivedObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;

            var volume = derivedObject.AddComponent<Volume>();
            volume.isGlobal = false;
        },
        onUpdateObject: (GameObject sourceObject, GameObject derivedObject) =>
        {
            var bounds = sourceObject.GetComponent<MeshFilter>().sharedMesh.bounds;
            var collider = derivedObject.GetComponent<BoxCollider>();
            collider.size = bounds.size;
        }
        );
    }
}

public class ImportHintsPostprocessor : AssetPostprocessor
{
    void OnPostprocessModel(GameObject g)
    {
        // Initiating sync asyncronously to avoid problems:
        Sync();
    }

    async void Sync()
    {
        await Task.Delay(100);
        ImportHints.SyncEverything();
    }
}