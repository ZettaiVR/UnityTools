using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Animations;

namespace Zettai
{
    public static class Sanitizer
    {
        public enum AssetType
        {
            Avatar = 1,
            Scene = 2,
            Prop = 4,
            Other = 8,
            Unknown = 128
        }
        private class ComponentDependency
        {
            public bool hasDependency;
            public Type[] types = new Type[3];
            private static readonly ComponentDependency[] m_emptyArray = new ComponentDependency[0];
            public static ComponentDependency[] EmptyArray { get { return m_emptyArray; } }
        }
        /// <summary>
        /// Removes all components from gameObject and its children that are not on a whitelist.  
        /// </summary>
        /// <param name="gameObject">The GameObject to sanitize.</param>
        /// <param name="assetType">The type of asset to sanitize that determines the allowed components.</param>
        /// <param name="removeFromAllowedList">Component types to remove from the default allowed components lists for the asset type.</param>
        /// <returns>The time taken in milliseconds.</returns>
        public static uint SanitizeGameObject(GameObject gameObject, AssetType assetType, List<Type> removeFromAllowedList = null)
        {
            var previous = Application.GetStackTraceLogType(LogType.Error);
            Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.None);
            stopWatch.Stop();
            stopWatch.Reset();
            stopWatch.Start();

            Debug.Log($"Starting SanitizeGameObject for '{gameObject.name}'");
            var allowedTypes = GetAllowedTypesHashSet(assetType);
            Debug.Log("allowedTypes count: " + allowedTypes.Count);
            if (removeFromAllowedList != null) 
            {
                foreach (var item in removeFromAllowedList)
                {
                    allowedTypes.Remove(item);
                }
            }

            removedComponents.Clear();

            if (ShaderBlacklistSet.Count == 0)
                ShaderBlacklistSet.UnionWith(Whitelists.ShaderBlacklist);

            uint materialCount = 0;
            gameObject.GetComponentsInChildren(true, components);
            for (int i = 0; i < components.Count; i++)
            {
                Component component = components[i];
                if (component == null)
                    continue;
                var renderer = component as Renderer;
                if (renderer)
                {
                    materialCount += LimitRenderer(renderer);
                }
                Type compType = component.GetType();
                if (compType == transformType || allowedTypes.Contains(compType))
                {
                    if (greyList.Contains(compType))
                    {
                        var mono = component as MonoBehaviour;
                        if (mono)
                            mono.enabled = false;
                    }                    
                    continue;
                }
                RemoveComponent(component, compType);
            }
            if (removedComponents.Length > 0)
            {
                Debug.LogError(removedComponents.ToString(), gameObject);
            }
            stopWatch.Stop();
            Application.SetStackTraceLogType(LogType.Error, previous);
            return (uint)(stopWatch.Elapsed.TotalMilliseconds * 1000);
        }
        private static uint LimitRenderer(Renderer renderer)
        {
            renderer.GetSharedMaterials(materials);
            if (materials.Count > MaxMaterialsOnRenderer)
            {
                var distinctMaterials = materials.Distinct();
                materials.RemoveAll(m => m == null);
                if (distinctMaterials.Count() == 1)
                {                    
                    renderer.sharedMaterials = distinctMaterials.ToArray();
                }
                else
                {
                    materials.Clear();
                    materials.AddRange(distinctMaterials); 
                    int remove = Math.Max(0, materials.Count - MaxMaterialsOnRenderer - 1);
                   
                    materials.RemoveRange(MaxMaterialsOnRenderer, remove); 
                    renderer.sharedMaterials = materials.ToArray();
                }
            }
            for (int j = 0; j < materials.Count; j++)
            {
                if (!materials[j])
                    continue;
                if (ShaderBlacklistSet.Contains(materials[j].shader.name))
                {
                    materials[j].shader = fallbackShader;
                    materials[j].enableInstancing = true;
                }
                int rq = materials[j].renderQueue;
                if (rq >= 5000)
                {
                    materials[j].renderQueue = 4999;
                }
                if (rq < 1000)
                {
                    materials[j].renderQueue = 1000;
                }
            }
            return (uint)materials.Count;
        }
        private static void RemoveComponent(Component component, Type type, Component[] componentsOnGameObject = null)
        {
            if (componentsOnGameObject == null)
                componentsOnGameObject = GetComponentsOnGameObject(component.gameObject);
            for (int i = 0; i < componentsOnGameObject.Length; i++)
            {
                Component thisComp = componentsOnGameObject[i];
                if (thisComp == null || thisComp == component)
                    continue;
                Type thisType = thisComp.GetType();
                var dep = GetComponentDependency(thisType);
                for (int j = 0; j < dep.Length; j++)
                {
                    if (!dep[j].hasDependency)
                        continue;
                    for (int k = 0; k < 3; k++)
                        if (dep[j].types[k] == type)
                            RemoveComponent(thisComp, thisType, componentsOnGameObject);
                }
            }
            AppendToRemoveLog(component, type);
            UnityEngine.Object.DestroyImmediate(component, true);
        }
        private static void AppendToRemoveLog(Component component, Type type)
        {
            removedComponents.Append("Removed component '");
            removedComponents.Append(type.Name);
            removedComponents.Append("' from gameObject '");
            removedComponents.Append(component.gameObject.name);
            removedComponents.AppendLine("'.");
        }
        private static Component[] GetComponentsOnGameObject(GameObject gameObject)
        {
            if (componentsOnGameObject.TryGetValue(gameObject, out Component[] componentArray))
                return componentArray;
            componentsOnGo.Clear();
            gameObject.GetComponents(componentsOnGo);
            int index = 0;
            for (int i = 0; i < componentsOnGo.Count; i++)
            {
                if (componentsOnGo[i].GetType() == transformType)
                {
                    index = i;
                    break;
                }
            }
            componentsOnGo.RemoveAtSwapback(index);
            componentsOnGameObject.Add(gameObject, componentsOnGo.ToArray());
            return componentsOnGameObject[gameObject];
        }
        private static ComponentDependency[] GetComponentDependency(Type type)
        {
            if (!componentDependencies.TryGetValue(type, out ComponentDependency[] deps))
            {
                var attr = type.GetCustomAttributes(typeof(RequireComponent), true);
                bool hasDependency = false;
                if (attr == null || attr.Length == 0)
                {
                    componentDependencies.Add(type, ComponentDependency.EmptyArray);
                    return ComponentDependency.EmptyArray;
                }
                deps = new ComponentDependency[attr.Length];
                for (int i = 0; i < attr.Length; i++)
                {
                    if (!(attr[i] is RequireComponent requireComponent))
                        continue;
                    var dep = new ComponentDependency();
                    if (requireComponent.m_Type0 != transformType)
                        dep.types[0] = requireComponent.m_Type0;
                    if (requireComponent.m_Type1 != transformType)
                        dep.types[1] = requireComponent.m_Type1;
                    if (requireComponent.m_Type2 != transformType)
                        dep.types[2] = requireComponent.m_Type2;
                    if (dep.types[0] != null || dep.types[1] != null || dep.types[2] != null)
                    {
                        dep.hasDependency = true;
                        hasDependency = true;
                        deps[i] = dep;
                    }
                }
                if (hasDependency)
                {
                    componentDependencies.Add(type, deps);
                    return deps;
                }
                else
                {
                    componentDependencies.Add(type, ComponentDependency.EmptyArray);
                    return ComponentDependency.EmptyArray;
                }
            }
            return deps;
        }
        private static HashSet<Type> GetAllowedTypesHashSet(AssetType assetType) 
        {
            if (allowedTypesDict.TryGetValue(assetType, out HashSet<Type> set)) 
            {
                return set;
            }
            AddListToSet(Whitelists.ComponentGreyListCommonTypes, greyList);
            set = new HashSet<Type>();
            allowedTypesDict.Add(assetType, set);
            AddListToSet(Whitelists.ComponentWhiteListCommonTypes, set);
            AddListToSet(Whitelists.ComponentWhiteListCommonTextAssets, set);
            AddListToSet(Whitelists.ComponentWhiteListCommonTextInternals, set);
            switch (assetType)
            {
                case AssetType.Avatar:
                    {
                        AddListToSet(Whitelists.ComponentWhiteListAvatarTextInternals, set);
                        break;
                    }
                case AssetType.Scene:
                    {
                        AddListToSet(Whitelists.ComponentWhiteListSceneText, set);
                        AddListToSet(Whitelists.ComponentWhiteListSceneTextInternals, set);
                        AddListToSet(Whitelists.ComponentWhiteListSceneTypes, set);
                        break;
                    }
                case AssetType.Prop:
                    {
                        break;
                    }
                case AssetType.Other:
                case AssetType.Unknown:
                default:
                    break;
            }
            return set;
        }
        private static void AddListToSet(IReadOnlyList<string> whitelist, HashSet<Type> set) 
        {
            for (int i = 0; i < whitelist.Count; i++)
            {
                Type[] types = TypesFromName(whitelist[i]);
                if (types != null && types.Length > 0)
                    for (int j = 0; j < types.Length; j++)
                        set.Add(types[j]);
            }
        }
        private static void AddListToSet(IReadOnlyList<Type> whitelist, HashSet<Type> set)
        {
            for (int i = 0; i < whitelist.Count; i++)
            {
                Type[] types = TypesFromType(whitelist[i]);
                for (int j = 0; j < types.Length; j++)
                    set.Add(types[j]);
            }
        }
        private static Type[] TypesFromName(string name)
        {
            if (typeNameCache.TryGetValue(name, out Type[] types))
                return types;
            if (allAssemblies == null)
                allAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            Type type = null;
            for (int i = 0; i < allAssemblies.Length; i++)
            {
                type = allAssemblies[i].GetType(name);
                if (type != null)
                    break;
            }
            types = TypeArrayForType(type);
            typeNameCache.Add(name, types);
            return types;
        }
        private static Type[] TypesFromType(Type type)
        {
            string name = type.Name;
            if (typeNameCache.TryGetValue(name, out Type[] types))
                return types;
            types = TypeArrayForType(type);
            typeNameCache.Add(name, types);
            return types;
        }
        private static Type[] TypeArrayForType(Type type)
        {
            if (type == null)
                return emptyTypeArray;
            if (allAssemblies == null)
                allAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            matchingTypes.Clear();
            try
            {
                matchingTypes.Add(type);
                for (int i = 0; i < allAssemblies.Length; i++)
                {
                    Type[] types = allAssemblies[i].GetTypes();
                    for (int j = 0; j < types.Length; j++)
                        if (types[j] != type && type.IsAssignableFrom(types[j]))
                            matchingTypes.Add(types[j]);
                }
                return matchingTypes.ToArray();
            }
            finally
            {
                matchingTypes.Clear();
            }
        }
        private static void RemoveAtSwapback<T>(this List<T> list, int index)
        {
            int last = list.Count - 1;
            if (index != last)
                list[index] = list[last];
            list.RemoveAt(last);
        }

        private const int MaxMaterialsOnRenderer = 100;
        private static System.Reflection.Assembly[] allAssemblies = null;
        private static readonly System.Diagnostics.Stopwatch stopWatch = new System.Diagnostics.Stopwatch();
        private static readonly Shader fallbackShader = Shader.Find("Standard");
        private static readonly Type transformType = typeof(Transform);
        private static readonly Type[] emptyTypeArray = new Type[0];
        private static readonly StringBuilder removedComponents = new StringBuilder(10 * 1024);
        private static readonly List<Component> components = new List<Component>(1000);
        private static readonly List<Component> componentsOnGo = new List<Component>(10);
        private static readonly List<Material> materials = new List<Material>(1000);
        private static readonly HashSet<Type> matchingTypes = new HashSet<Type>();
        private static readonly HashSet<Type> greyList = new HashSet<Type>();
        private static readonly HashSet<string> ShaderBlacklistSet = new HashSet<string>();
        private static readonly Dictionary<string, Type[]> typeNameCache = new Dictionary<string, Type[]>();
        private static readonly Dictionary<GameObject, Component[]> componentsOnGameObject = new Dictionary<GameObject, Component[]>();
        private static readonly Dictionary<Type, ComponentDependency[]> componentDependencies = new Dictionary<Type, ComponentDependency[]>();
        private static readonly Dictionary<AssetType, HashSet<Type>> allowedTypesDict = new Dictionary<AssetType, HashSet<Type>>();
    }
}
