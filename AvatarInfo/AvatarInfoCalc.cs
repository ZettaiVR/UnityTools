using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using System.Reflection;
using System.Text;

namespace Zettai
{
    public static class Extentions
    {
#if !NET40
        // .NET Fw 3.5 doesn't have these
        public static void Clear(this StringBuilder sb) 
        {
            sb.Remove(0, sb.Length);
        }
        public static void Restart(this System.Diagnostics.Stopwatch sw) 
        {
            sw.Stop();
            sw.Reset();
            sw.Start();
        }
#endif
    }
    public class AvatarInfoCalc
    {
        public static AvatarInfoCalc Instance { get { return instance; } }
        private AvatarInfoCalc() { }
        public static HashSet<string> AllAdditionalShaderKeywords = new HashSet<string>();
        internal static readonly AvatarInfoCalc instance = new AvatarInfoCalc();
        public bool ShouldLog = true;
        private const float FourThird = (4f / 3f);
        static readonly FieldInfo fi = typeof(DynamicBone).GetField("m_Particles", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly MethodInfo dynBoneSetupParticlesMethod = typeof(DynamicBone).GetMethod("SetupParticles", BindingFlags.NonPublic | BindingFlags.Instance);
        readonly System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
        static readonly long nanosecPerTick = (1000L * 1000L * 1000L) / System.Diagnostics.Stopwatch.Frequency;
        readonly List<Renderer> renderers = new List<Renderer>();
        readonly List<SkinnedMeshRenderer> skinnedMeshRenderers = new List<SkinnedMeshRenderer>();
        readonly List<MeshRenderer> meshRenderers = new List<MeshRenderer>();
        readonly List<TrailRenderer> trailRenderers = new List<TrailRenderer>();
        readonly List<LineRenderer> lineRenderers = new List<LineRenderer>();
        readonly List<ParticleSystemRenderer> particleSystemRenderers = new List<ParticleSystemRenderer>();
        readonly List<CanvasRenderer> canvasRenderers = new List<CanvasRenderer>();
        readonly List<BillboardRenderer>  billboardRenderers = new List<BillboardRenderer>();
        readonly List<SpriteRenderer> spriteRenderers = new List<SpriteRenderer>();

        readonly List<Animator> animators = new List<Animator>();
        readonly List<AudioSource> audioSources = new List<AudioSource>();
        readonly List<AudioStatData> audioStatData = new List<AudioStatData>();
        readonly List<Cloth> cloths = new List<Cloth>();
        readonly List<Collider> colliders = new List<Collider>();
        readonly List<DynamicBone> dynamicBones = new List<DynamicBone>();
        readonly List<UnityEngine.Animations.IConstraint> constraints = new List<UnityEngine.Animations.IConstraint>();
        readonly List<Joint> joints = new List<Joint>();
        readonly List<Light> lights = new List<Light>();
        readonly List<Rigidbody> rigidbodies = new List<Rigidbody>();
        readonly List<RootMotion.FinalIK.VRIK> VRIKs = new List<RootMotion.FinalIK.VRIK>();
        readonly List<RootMotion.FinalIK.IK> IKs = new List<RootMotion.FinalIK.IK>();
        readonly List<RootMotion.FinalIK.TwistRelaxer> twistRelaxers = new List<RootMotion.FinalIK.TwistRelaxer>();
        readonly List<Transform> tempTransforms = new List<Transform>();
        readonly List<TextureStatData> textureStatData = new List<TextureStatData>();
        readonly List<Transform> transforms = new List<Transform>();

        readonly List<string> texturePropertyNames = new List<string>();
        readonly HashSet<string> shaderKeywords = new HashSet<string>();
        readonly List<int> leafDepth = new List<int>();
        readonly StringBuilder sb = new StringBuilder();
        readonly HashSet<AudioClip> audioClips = new HashSet<AudioClip>();
        readonly HashSet<Texture> textures = new HashSet<Texture>();
        readonly HashSet<DynamicBoneColliderBase> dbColliders = new HashSet<DynamicBoneColliderBase>();

        string output = "";
        string log_output;
        int faceCounter; 
        int clothVertCount;
        int dbCount;
        int dbCount2;
        int collCount;
        int index;
        int max = 0;
        int _string_length;
        long profiler_mem;
        long _calc_mem;
        float length;
        double _totalSize;
        double _size;

        AudioSource audioSource;
        Cubemap cubemap;
        IList dbParticlesList;
        MeshFilter meshFilter;
        RenderTexture rt;
#if UNITY_2019_1_OR_NEWER
        SubMeshDescriptor submesh;
#endif
        Texture _tempText;
        Texture2D texture2D;
        Type type;

        public struct AudioStatData
        {
            public string clipName;
            public double size;
            public bool loadInBackground;
            public AudioClipLoadType loadType;
            public float length;
            public int channels;
            public int frequency;
        }
        public static readonly List<string> defaultKeywords = new List<string>(new string[]
        { 
        // Unity standard shaders
"_ALPHABLEND_ON",
"_ALPHAMODULATE_ON",
"_ALPHAPREMULTIPLY_ON",
"_ALPHATEST_ON",
"_COLORADDSUBDIFF_ON",
"_COLORCOLOR_ON",
"_COLOROVERLAY_ON",
"_DETAIL_MULX2",
"_EMISSION",
"_FADING_ON",
"_GLOSSYREFLECTIONS_OFF",
"_GLOSSYREFLECTIONS_OFF",
"_MAPPING_6_FRAMES_LAYOUT",
"_METALLICGLOSSMAP",
"_NORMALMAP",
"_PARALLAXMAP",
"_REQUIRE_UV2",
"_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A",
"_SPECGLOSSMAP",
"_SPECULARHIGHLIGHTS_OFF",
"_SPECULARHIGHLIGHTS_OFF",
"_SUNDISK_HIGH_QUALITY",
"_SUNDISK_NONE",
"_SUNDISK_SIMPLE",
"_TERRAIN_NORMAL_MAP",
"BILLBOARD_FACE_CAMERA_POS",
"EFFECT_BUMP",
"EFFECT_HUE_VARIATION",
"ETC1_EXTERNAL_ALPHA",
"GEOM_TYPE_BRANCH",
"GEOM_TYPE_BRANCH_DETAIL",
"GEOM_TYPE_FROND",
"GEOM_TYPE_LEAF",
"GEOM_TYPE_MESH",
"LOD_FADE_CROSSFADE",
"PIXELSNAP_ON",
"SOFTPARTICLES_ON",
"STEREO_INSTANCING_ON",
"STEREO_MULTIVIEW_ON",
"UNITY_HDR_ON",
"UNITY_SINGLE_PASS_STEREO",
"UNITY_UI_ALPHACLIP",
"UNITY_UI_CLIP_RECT",
        // Post Processing
"ANTI_FLICKER",
"APPLY_FORWARD_FOG",
"AUTO_EXPOSURE",
"AUTO_KEY_VALUE",
"BLOOM",
"BLOOM_LENS_DIRT",
"BLOOM_LOW",
"CHROMATIC_ABERRATION",
"CHROMATIC_ABERRATION_LOW",
"COLOR_GRADING",
"COLOR_GRADING_HDR",
"COLOR_GRADING_HDR_3D",
"COLOR_GRADING_LOG_VIEW",
"DEPTH_OF_FIELD",
"DEPTH_OF_FIELD_COC_VIEW",
"DISTORT",
"DITHERING",
"FINALPASS",
"FOG_EXP",
"FOG_EXP2",
"FOG_LINEAR",
"FOG_OFF",
"FXAA",
"FXAA_KEEP_ALPHA",
"FXAA_LOW",
"GRAIN",
"SOURCE_GBUFFER",
"STEREO_DOUBLEWIDE_TARGET",
"STEREO_INSTANCING_ENABLED",
"TONEMAPPING_ACES",
"TONEMAPPING_CUSTOM",
"TONEMAPPING_FILMIC",
"TONEMAPPING_NEUTRAL",
"UNITY_COLORSPACE_GAMMA",
"USER_LUT",
"VIGNETTE",
"VIGNETTE_CLASSIC",
"VIGNETTE_MASKED"
        });
        public void CheckCloth(GameObject ava, ref AvatarInfo _AvatarInfo)
        {
            _AvatarInfo.clothNumber = 0;
            _AvatarInfo.clothDiff = 0;
            _AvatarInfo.clothVertCount = 0;
            ava.GetComponentsInChildren(true, cloths);
            foreach (Cloth cloth in cloths)
            {
                clothVertCount = cloth.vertices.Length;  //GC alloc: 6-7
                _AvatarInfo.clothVertCount += clothVertCount;
                _AvatarInfo.clothDiff += clothVertCount * cloth.clothSolverFrequency;
                _AvatarInfo.clothNumber++;
            }
            cloths.Clear();
        }
        public void CheckDB(GameObject ava, ref AvatarInfo _AvatarInfo)
        {
            dbCount = 0;
            collCount = 0;
            ava.GetComponentsInChildren(true, dynamicBones);
            foreach (DynamicBone db in dynamicBones)
            {
                if (db)
                {
                    try
                    {
                        dbParticlesList = fi.GetValue(db) as IList;
                        if (!db.isActiveAndEnabled || (dbParticlesList == null || dbParticlesList.Count == 0))
                        {
                            dynBoneSetupParticlesMethod.Invoke(db, new object[0]);
                            dbParticlesList = fi.GetValue(db) as IList;
                        }
                        if (dbParticlesList != null && dbParticlesList.Count > 0)
                        {
                            dbCount2 = dbParticlesList.Count;
                            // Endbones apparently don't count? | or they do, I'm not sure what's going on in vrc
                            //if (db.m_EndLength > 0 || db.m_EndOffset.magnitude > 0)
                            //{
                            //    dbCount2--;
                            //}
                            dbCount += dbCount2;
                            if (db.m_Colliders != null && db.m_Colliders.Count > 0)
                            {
                                dbColliders.UnionWith(db.m_Colliders);
                                dbColliders.Remove(null);

                                if (dbCount2 > 0)
                                {
                                    collCount += Mathf.Max(dbCount2 - 1, 0) * db.m_Colliders.Count;
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError(e);
                    }
                }
            }
            dynamicBones.Clear();
            dbColliders.Clear();
            dbParticlesList = null;
            _AvatarInfo.dbCount = dbCount;
            _AvatarInfo.dbCollisionCount = collCount;
        }
        public void CountObjects(GameObject gameObject, ref AvatarInfo avatarInfo)
        {
            gameObject.GetComponentsInChildren(true, transforms);
            gameObject.GetComponentsInChildren(true, joints);
            gameObject.GetComponentsInChildren(true, rigidbodies);
            gameObject.GetComponentsInChildren(true, constraints);
            gameObject.GetComponentsInChildren(true, animators);
            gameObject.GetComponentsInChildren(true, VRIKs);
            gameObject.GetComponentsInChildren(true, IKs);
            gameObject.GetComponentsInChildren(true, twistRelaxers);
            gameObject.GetComponentsInChildren(true, lights);
            gameObject.GetComponentsInChildren(true, colliders);
            avatarInfo.TransformCount = transforms.Count;
            avatarInfo.JointCount = joints.Count;
            avatarInfo.RigidBodyCount = rigidbodies.Count;
            avatarInfo.ConstraintCount = constraints.Count;
            avatarInfo.Animators = animators.Count;
            avatarInfo.VRIKComponents = VRIKs.Count;
            avatarInfo.OtherFinalIKComponents = IKs.Count - avatarInfo.VRIKComponents;
            avatarInfo.TwistRelaxers = twistRelaxers.Count;
            avatarInfo.Lights = lights.Count;
            avatarInfo.ColliderCount = colliders.Count;
            leafDepth.Clear();
            transforms.Clear();
            DFSGetChildren(gameObject.transform, 0);
            avatarInfo.MaxHiearchyDepth = GetMaxInList(leafDepth);
            avatarInfo.LongestPath = GetLeafPath(index);
            if (ShouldLog)
            {
                Debug.Log(LeavesToString());
                Debug.Log(avatarInfo.LongestPath);
                Debug.Log(leafDepth.Max() + " " + transforms[leafDepth.IndexOf(leafDepth.Max())].name);
            }
            sb.Clear();
            tempTransforms.Clear();
            leafDepth.Clear();
            transforms.Clear();
            joints.Clear();
            rigidbodies.Clear();
            constraints.Clear();
            animators.Clear();
            VRIKs.Clear();
            IKs.Clear();
            twistRelaxers.Clear();
            lights.Clear();
            colliders.Clear();
        }
        private int GetMaxInList(IList<int> list) 
        {
            max = 0;
            index = 0;
            for (int i = 0, length = list.Count; i < length; i++)
            {
                if (max < list[i]) 
                {
                    max = list[i];
                    index = i;
                }
            }
            return max + 1;
        }
        private string LeavesToString()
        {
            output = "";
            for (int i = 0, length = leafDepth.Count; i < length; i++)
            {
                output += $"{transforms[i].name} ({leafDepth[i]})\r\n";
            }
            return output;
        }
        private string GetLeafPath(int index)
        {
            if (transforms.Count == 0 || transforms.Count < index) { return ""; }
            transforms[index].GetComponentsInParent(true, tempTransforms);
            if (tempTransforms.Count == 0) { return ""; }
            sb.Clear();
            sb.Append(tempTransforms[tempTransforms.Count - 1].name);
            for (int i = tempTransforms.Count - 2; i >= 0 ; i--)
            {
                sb.Append("\\");
                sb.Append(tempTransforms[i].name);
            }
            return sb.ToString();
        }
        private void DFSGetChildren(Transform transform, int level)
        {
            var childCount = transform.childCount;
            if (childCount == 0 && level == 0) 
            {
                leafDepth.Add(0);
                transforms.Add(transform);
                return;
            }
            level++;
            for (var index = 0; index < childCount; index++)
            {
                var child = transform.GetChild(index);
                if (child.childCount > 0)
                {
                    DFSGetChildren(child, level);
                }
                else
                {
                    leafDepth.Add(level);
                    transforms.Add(child);
                }
            }
        }
        public void CheckRenderers(GameObject ava, ref AvatarInfo _AvatarInfo)
        {
            ava.GetComponentsInChildren(true, skinnedMeshRenderers);
            ava.GetComponentsInChildren(true, meshRenderers);
            ava.GetComponentsInChildren(true, trailRenderers);
            ava.GetComponentsInChildren(true, lineRenderers);
            ava.GetComponentsInChildren(true, particleSystemRenderers);
            ava.GetComponentsInChildren(true, canvasRenderers);
            ava.GetComponentsInChildren(true, billboardRenderers);
            ava.GetComponentsInChildren(true, spriteRenderers);
            _AvatarInfo.TrianglesOrQuads = 0;
            _AvatarInfo.lineTrailRenderers = 0;
            _AvatarInfo.skinnedMeshRenderers = 0;
            _AvatarInfo.meshRenderers = 0;
            _AvatarInfo.materialCount = 0;
            _AvatarInfo.skinnedBones = 0;
            _AvatarInfo.skinnedBonesVRC = 0;
            _AvatarInfo.particleSystems = 0;
            _AvatarInfo.otherRenderers = 0;
            _AvatarInfo.maxParticles = 0;
            _AvatarInfo.lineTrailRendererTriCount = 0;
            transforms.Clear();
            foreach (var item in skinnedMeshRenderers)
            {
                _AvatarInfo.materialCount += item.sharedMaterials.Length;
                _AvatarInfo.TrianglesOrQuads += CountMesh(item.sharedMesh);
                transforms.AddRange(item.bones);
                _AvatarInfo.skinnedMeshRenderers++;
            }
            foreach (var item in meshRenderers)
            {
                _AvatarInfo.materialCount += item.sharedMaterials.Length;
                _AvatarInfo.TrianglesOrQuads += CountMeshRendererTris(item);
                _AvatarInfo.meshRenderers++;
            }
            meshFilter = null;
            foreach (var item in trailRenderers)
            {
                _AvatarInfo.materialCount += item.sharedMaterials.Length;
                _AvatarInfo.lineTrailRendererTriCount += CountTrailRendererTris(item);
                _AvatarInfo.lineTrailRenderers++;
            }
            foreach (var item in lineRenderers)
            {
                _AvatarInfo.materialCount += item.sharedMaterials.Length;
                _AvatarInfo.lineTrailRendererTriCount += CountLineRendererTris(item);
                _AvatarInfo.lineTrailRenderers++;
            }
            foreach (var item in particleSystemRenderers)
            {
                _AvatarInfo.particleSystems++;
                ParticleSystem particleSystem = item.GetComponent<ParticleSystem>();
                if (particleSystem) 
                {
                    _AvatarInfo.maxParticles += particleSystem.main.maxParticles;
                }
                _AvatarInfo.materialCount += item.sharedMaterials.Length;
            }
            foreach (var item in canvasRenderers)
            {
                _AvatarInfo.otherRenderers++;
                _AvatarInfo.materialCount += item.materialCount;
            }
            foreach (var item in billboardRenderers)
            {
                _AvatarInfo.otherRenderers++;
                _AvatarInfo.materialCount += item.sharedMaterials.Length;
            }
            foreach (var item in spriteRenderers)
            {
                _AvatarInfo.otherRenderers++;
                _AvatarInfo.materialCount += item.sharedMaterials.Length;
            }
            _AvatarInfo.skinnedBones = transforms.Count();
            _AvatarInfo.skinnedBonesVRC = transforms.Distinct().Count();
            skinnedMeshRenderers.Clear();
            meshRenderers.Clear();
            trailRenderers.Clear();
            lineRenderers.Clear();
            particleSystemRenderers.Clear();
            canvasRenderers.Clear();
            billboardRenderers.Clear();
            spriteRenderers.Clear();
            transforms.Clear();
        }
        private int CountMesh(Mesh mesh)
        {
            faceCounter = 0;
            if (mesh)
            {
                for (int i = 0, length = mesh.subMeshCount; i < length; i++)
                {
                    MeshTopology topology;
#if UNITY_2019_1_OR_NEWER
                    submesh = mesh.GetSubMesh(i);
                    topology = submesh.topology;
#else
                    topology = mesh.GetTopology(i);
#endif
                    switch (topology)
                    {
                        case MeshTopology.Lines:
                        case MeshTopology.LineStrip:
                        case MeshTopology.Points:
                            //wtf
                            { continue; }
#if UNITY_2019_1_OR_NEWER
                        case MeshTopology.Quads:
                            {
                                faceCounter += (submesh.indexCount / 4);
                                continue;
                            }
                        case MeshTopology.Triangles:
                            {
                                faceCounter += (submesh.indexCount / 3);
                                continue;
                            }
#else
                        case MeshTopology.Quads:
                            {
                                faceCounter += (int)(mesh.GetIndexCount(i) / 4);
                                continue;
                            }
                        case MeshTopology.Triangles:
                            {
                                faceCounter += (int)(mesh.GetIndexCount(i) / 3);
                                continue;
                            }
#endif
                    }
                }
            }
            return faceCounter;

        }
        private int CountMeshRendererTris(MeshRenderer renderer)
        {
            meshFilter = renderer.gameObject.GetComponent<MeshFilter>();
            if (meshFilter)
            {
                return CountMesh(meshFilter.sharedMesh);
            }
            return 0;
        }
        private int CountTrailRendererTris(TrailRenderer renderer)
        {
            if (renderer)
            {
                return (int)(renderer.time * 100);
            }
            return 0;
        }
        private int CountLineRendererTris(LineRenderer renderer)
        {
            if (renderer)
                return renderer.positionCount * 2; // idk..
            return 0;
        }
        public void CheckAudio(GameObject ava, ref AvatarInfo _AvatarInfo)
        {
            audioClips.Clear();
            ava.GetComponentsInChildren(true, audioSources);
            _AvatarInfo.AudioSources = audioSources.Count;
            for (int i = 0, length = audioSources.Count; i < length; i++)
            {
                audioSource = audioSources[i];
                if (audioSource != null && audioSource.clip != null)
                    audioClips.Add(audioSource.clip);
            }
            audioSource = null;
            _totalSize = 0;
            if (ShouldLog) sb.Clear();
            length = 0;
            audioStatData.Clear();
            _AvatarInfo.AudioClipCount = audioClips.Count;
            foreach (AudioClip audioClip in audioClips)
            {
                _size = audioClip.samples * audioClip.channels * 2f; // 16 bit samples = 2 Bytes
                _totalSize += _size;
                length += audioClip.length;
                if (ShouldLog) audioStatData.Add(new AudioStatData {clipName = audioClip.name, size = Math.Round(_size / 1024 / 1024, 2), length = audioClip.length, channels = audioClip.channels, frequency = audioClip.frequency, loadInBackground = audioClip.loadInBackground, loadType = audioClip.loadType });
            }
            _AvatarInfo.AudioClipLength = (int)length;
            _AvatarInfo.AudioClipSizeMB = (Math.Round(_totalSize / 1024f / 1024f * 100f) / 100f);
            if (ShouldLog)
            {
                _AvatarInfo.AvatarInfoString += AddAudioInfoToLog(_AvatarInfo);
            }
            audioClips.Clear();
            audioSources.Clear();
        }
        private string AddAudioInfoToLog(AvatarInfo _AvatarInfo)
        {
            sb.Clear();
            sb.Append(_AvatarInfo.AudioClipCount);
            sb.Append(" audio clips");
            if (audioClips.Count > 0)
            {
                sb.Append(" with ");
                sb.Append(_AvatarInfo.AudioClipSizeMB);
                sb.Append(" MB estimated size");
            }
            sb.AppendLine(".");
            foreach (var audioClip in audioStatData)
            {
                sb.Append("Name: ");
                sb.Append(audioClip.clipName);
                sb.Append(": estimated size: ");
                sb.Append(audioClip.size);
                sb.Append(" MB, loadInBackground: ");
                sb.Append(audioClip.loadInBackground);
                sb.Append(", loadType: ");
                sb.Append(audioClip.loadType.ToString());
                sb.Append(", length: ");
                sb.Append(audioClip.length);
                sb.Append(", channels: ");
                sb.Append(audioClip.channels);
                sb.Append(", frequency: ");
                sb.Append(audioClip.frequency);
                sb.AppendLine();
            }
            return sb.ToString();
        }
        public void CheckTextures(GameObject ava, ref AvatarInfo _AvatarInfo)
        {
            if (ShouldLog) 
            {
                stopwatch.Restart();
            }
            _AvatarInfo.passCount = 0;
            textures.Clear();
            shaderKeywords.Clear();
            ava.GetComponentsInChildren(true, renderers);
            foreach (var rend in renderers)
            {
                foreach (var material in rend.sharedMaterials)
                {
                    if (material)
                    {
                        texturePropertyNames.Clear();
                        material.GetTexturePropertyNames(texturePropertyNames);
                        _AvatarInfo.passCount += material.passCount;
                        shaderKeywords.UnionWith(material.shaderKeywords);
                        foreach (var _name in texturePropertyNames)
                        {
                            _tempText = material.GetTexture(_name);
                            if (_tempText != null) 
                            {
                                textures.Add(_tempText); 
                            }
                        }
                    }
                }
            }
            renderers.Clear();
            _AvatarInfo.additionalShaderKeywords = shaderKeywords.Except(defaultKeywords).ToArray();
            AllAdditionalShaderKeywords.UnionWith(_AvatarInfo.additionalShaderKeywords);
            _AvatarInfo.additionalShaderKeywordCount = _AvatarInfo.additionalShaderKeywords.Length;
            _AvatarInfo.textureMem = 0;
            _AvatarInfo.calc_mem = 0;
            profiler_mem = 0;
            textureStatData.Clear();
            foreach (Texture texture in textures)
            {
                if (Profiler.supported)
                {
                    profiler_mem = Profiler.GetRuntimeMemorySizeLong(texture);
                    _AvatarInfo.textureMem += profiler_mem;
                }
                _calc_mem = 0;
                bool readWriteEnabled = texture.isReadable;
                var dim = texture.dimension;
                type = texture.GetType();
                switch (dim) 
                {
                    case TextureDimension.Tex2D:
                        {
                            if (type.Equals(typeof(Texture2D)))
                            {
                                texture2D = (Texture2D)texture;
                                _AvatarInfo.calc_mem += _calc_mem = CalculateMaxMemUse(texture2D.format, texture2D.width, texture2D.height, texture2D.mipmapCount > 1, readWriteEnabled);
                                if (ShouldLog)
                                {
                                    textureStatData.Add(new TextureStatData { type = "Texture2D", width = texture2D.width, height = texture2D.height, format = texture2D.format.ToString(), name = texture2D.name, profiler_mem = profiler_mem, _calc_mem = _calc_mem });
                                }
                                texture2D = null;
                            }
                            else if (type.Equals(typeof(RenderTexture)))
                            {
                                rt = (RenderTexture)texture;
                                _calc_mem = CalculateMaxRTMemUse(rt.format, rt.width, rt.height, rt.useMipMap, profiler_mem, rt.antiAliasing, rt.depth);
                                _AvatarInfo.calc_mem += _calc_mem;
                                if (ShouldLog)
                                {
                                    textureStatData.Add(new TextureStatData { type = "RenderTexture", width = rt.width, height = rt.height, format = rt.format.ToString(), name = rt.name, profiler_mem = profiler_mem, _calc_mem = _calc_mem });
                                }
                                rt = null;
                            }
                            else
                            {
                                if (ShouldLog)
                                {
                                    textureStatData.Add(new TextureStatData { type = texture.GetType().ToString(), width = texture.width, height = texture.height, format = dim.ToString(), name = texture.name, profiler_mem = profiler_mem, _calc_mem = 0 });
                                }
                            }
                            break;
                        }
                    case TextureDimension.Cube:
                        {
                            if (type.Equals(typeof(Cubemap)))
                            {
                                cubemap = (Cubemap)texture;
                                _calc_mem = (CalculateMaxMemUse(cubemap.format, cubemap.width, cubemap.height, cubemap.mipmapCount > 1, readWriteEnabled) * 6);
                                _AvatarInfo.calc_mem += _calc_mem;
                                if (ShouldLog)
                                {
                                    textureStatData.Add(new TextureStatData { type = "Cubemap", width = cubemap.width, height = cubemap.height, format = cubemap.format.ToString(), name = cubemap.name, profiler_mem = profiler_mem, _calc_mem = _calc_mem });
                                }
                                cubemap = null;
                            }
                            break;
                        }
                    case TextureDimension.CubeArray:
                        {
                            var cubemapArray = (CubemapArray)texture; bool mipmapped = false;
#if UNITY_2019_1_OR_NEWER
                            mipmapped = cubemapArray.mipmapCount > 0;
#else
                            {
                                var _ = new Texture2D(texture.width, texture.height);
                                Graphics.CopyTexture(texture, 0, _, 0);
                                mipmapped = _.mipmapCount > 1;
                            }
#endif
                            _calc_mem = (CalculateMaxMemUse(cubemapArray.format, cubemapArray.width, cubemapArray.height, mipmapped, readWriteEnabled) * 6) * cubemapArray.cubemapCount;
                            _AvatarInfo.calc_mem += _calc_mem;
                            if (ShouldLog)
                            {
                                textureStatData.Add(new TextureStatData { type = "CubeArray" + cubemapArray.cubemapCount, width = cubemap.width, height = cubemap.height, format = cubemap.format.ToString(), name = cubemap.name, profiler_mem = profiler_mem, _calc_mem = _calc_mem });
                            }
                            break;
                        }
                    case TextureDimension.Tex2DArray:
                        {
                            var texture2DArray = (Texture2DArray)texture;
                            bool mipmapped = false;
#if UNITY_2019_1_OR_NEWER
                            mipmapped = texture2DArray.mipmapCount > 0;
#else
                            {
                                var _ = new Texture2D(texture.width, texture.height);
                                Graphics.CopyTexture(texture, 0, _, 0);
                                mipmapped = _.mipmapCount > 1;
                            }
#endif
                            _AvatarInfo.calc_mem += _calc_mem = CalculateMaxMemUse(texture2DArray.format, texture2DArray.width, texture2DArray.height, mipmapped, readWriteEnabled) * texture2DArray.depth;
                            if (ShouldLog)
                            {
                                textureStatData.Add(new TextureStatData { type = "Tex2DArray" + texture2DArray.depth, width = texture2DArray.width, height = texture2DArray.height, format = texture2DArray.format.ToString(), name = texture2DArray.name, profiler_mem = profiler_mem, _calc_mem = _calc_mem });
                            }
                            texture2D = null;
                            break;
                        }
                    case TextureDimension.Tex3D:
                        {
                            var texture3D = (Texture3D)texture;
                            bool mipmapped = false;
#if UNITY_2019_1_OR_NEWER
                            mipmapped = texture3D.mipmapCount > 0;
#else
                            {
                                var _ = new Texture2D(texture.width, texture.height);
                                Graphics.CopyTexture(texture, 0, _, 0);
                                mipmapped = _.mipmapCount > 1;
                            }
#endif
                            _AvatarInfo.calc_mem += _calc_mem = CalculateMaxMemUse(texture3D.format, texture3D.width, texture3D.height, mipmapped, readWriteEnabled) * texture3D.depth;
                            if (ShouldLog)
                            {
                                textureStatData.Add(new TextureStatData { type = "texture3D", width = texture3D.width, height = texture3D.height, format = texture3D.format.ToString(), name = texture3D.name, profiler_mem = profiler_mem, _calc_mem = _calc_mem });
                            }
                            break;
                        }
                    default: 
                        {
                            if (ShouldLog)
                            {
                                textureStatData.Add(new TextureStatData { type = texture.GetType().ToString(), width = texture.width, height = texture.height, format = dim.ToString(), name = texture.name, profiler_mem = profiler_mem, _calc_mem = 0 });
                            }
                            break;
                        }
                }
            }
            if (!_AvatarInfo) { _AvatarInfo = ava.GetComponent<AvatarInfo>(); }
            if (!_AvatarInfo) { _AvatarInfo = ava.AddComponent<AvatarInfo>(); }
            _AvatarInfo.textureMemMB = Math.Round(_AvatarInfo.textureMem / 1024 / 1024f * 100) / 100f;
            _AvatarInfo.calc_memMB = Math.Round(_AvatarInfo.calc_mem / 1024 / 1024f * 100) / 100f;
            if (ShouldLog)
            {
                stopwatch.Stop();
                log_output = AddTextureInfoToLog(_AvatarInfo.textureMemMB, _AvatarInfo.calc_memMB, stopwatch.ElapsedTicks);
                _AvatarInfo.AvatarInfoString += log_output;
                Debug.Log(log_output);
            }
            textures.Clear();
            shaderKeywords.Clear();
        }
        private struct TextureStatData
        { 
            public int width;
            public int height;
            public string format;
            public string name;
            public string type;
            public long profiler_mem;
            public long _calc_mem;
            public bool isReadable;
        }
        private string AddTextureInfoToLog(double textureMemMB, double calc_memMB, long ElapsedTicks) 
        {
            sb.Clear();
            sb.Append("Textures use ");
            sb.Append(textureMemMB);
            sb.Append(" MBytes RAM + VRAM, calculated max: ");
            sb.Append(calc_memMB);
            sb.AppendLine(" MB VRAM.");
            sb.Append("Analysis took ");
            sb.Append(ElapsedTicks * nanosecPerTick / 1000000f);
            sb.Append(" ms (");
            sb.Append(ElapsedTicks);
            sb.AppendLine(" ticks)");
            foreach (var texture in textureStatData)
            {
                sb.Append(texture.type.PadRight(16, ' '));
                sb.Append(" width: ");
                sb.Append(texture.width.ToString().PadLeft(4, ' '));
                sb.Append(", height: ");
                sb.Append(texture.height.ToString().PadLeft(4, ' '));
                sb.Append(" Format: ");
                sb.Append(texture.format.PadRight(16, ' '));
                sb.Append(", name: ");
                sb.Append(texture.name);
                if (texture.profiler_mem > 0 || texture._calc_mem > 0)
                {
                    sb.Append(" using: ");
                }
                if (texture.profiler_mem > 0)
                {
                    sb.Append(Math.Round(texture.profiler_mem / 1024 / 1024f * 100) / 100f);
                    sb.Append(" MB (profiler)");
                    if (texture._calc_mem > 0) sb.Append(", ");
                }
                if (texture._calc_mem > 0)
                {
                    sb.Append(Math.Round(texture._calc_mem / 1024 / 1024f * 100) / 100f);
                    sb.Append(" MB (calculated) (");
                    sb.Append(texture.profiler_mem);
                    sb.Append(" Bytes)");
                }
                if (texture.isReadable) 
                {
                    sb.Append(" (texture marked as readable!)");
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }
        private long CalculateMaxRTMemUse(RenderTextureFormat format, int width, int height, bool mipmapped, long _default, int antiAlias, int depth)
        {
            _calc_mem = 0;
            switch (format)
            {
                //// 4 bit/pixel
                //    {
                //        _calc_mem = (long)(width * height / 2f * (mipmapped ? FourThird : 1f));
                //
                //        break;
                //    }
                case RenderTextureFormat.R8:  // 8 bit/pixel
                    {
                        _calc_mem = (width * height);
                        break;
                    }

                case RenderTextureFormat.ARGB4444: //2B/px
                case RenderTextureFormat.R16:
                case RenderTextureFormat.RG16:
                case RenderTextureFormat.RHalf:
                case RenderTextureFormat.RGB565:
                    {
                        _calc_mem = (long)(width * height * 2f);
                        break;
                    }

                // 3B/px
                //   {
                //       _calc_mem = (long)(width * height * 3f);
                //       break;
                //   }
                case RenderTextureFormat.ARGB32: // 4B/px
                case RenderTextureFormat.BGRA32:
                case RenderTextureFormat.RGHalf:
                case RenderTextureFormat.RFloat:
                    {
                        _calc_mem = (long)(width * height * 4f);
                        break;
                    }
                case RenderTextureFormat.ARGBHalf:
                case RenderTextureFormat.ARGB64:
                case RenderTextureFormat.RGBAUShort:
                case RenderTextureFormat.RGFloat: //8B/px
                    {
                        _calc_mem = (long)(width * height * 8f);
                        break;
                    }
                case RenderTextureFormat.ARGBInt:
                case RenderTextureFormat.ARGBFloat: //16B/px
                    {
                        _calc_mem = (long)(width * height * 16f);
                        break;
                    }
                case RenderTextureFormat.Depth:
                    {
                        _calc_mem = 0; // Depth will be added later
                        break;
                    }
                default:
                    {
                        _calc_mem = _default;
                        break;
                    }
            }
            int _AA = (antiAlias == 1) ? 1 : antiAlias + 1;
            /*
             *  MSAA level  |   texture size multiplier 
                MSAA1	    |   x
                MSAA2	    |   3x
                MSAA4	    |   5x
                MSAA8	    |   9x

                    depth buffer:
                MSAA level  |   texture size increment per pixel (Format independent)
                ------------|   16b     24/32b
                MSAA1	    |   2B	    4B 
                MSAA2	    |   4B	    8B 
                MSAA4	    |   8B	    16B 
                MSAA8	    |   16B 	32B 

                eg. 100×100px R8G8B8A8_Unorm: 4 * 10000 B
                MSAA8: 40 000 * 9 = 360 000 (B)
                24b depth = 10 000 * 4 = 40 000 (B) 
                MSAA8 + 24b depth = 360 000 + (10 000 * 4 * 8 = 320 000) = 680 000 (B)
                MipMaps don't affect stencils.

             */
            switch (depth)
            {
                case 0:
                    {
                        _calc_mem = _calc_mem * _AA * (long)(mipmapped ? FourThird : 1f);
                        break;
                    }

                case 16:
                    {
                        _calc_mem = _calc_mem * _AA * (long)(mipmapped ? FourThird : 1f) + (width * height * antiAlias * 2);
                        break;
                    }
                case 24:
                case 32:
                    {
                        _calc_mem = _calc_mem * _AA * (long)(mipmapped ? FourThird : 1f) + (width * height * antiAlias * 4);
                        break;
                    }
            }
            return _calc_mem;
        }
        private long CalculateMaxMemUse(TextureFormat format, int width, int height, bool mipmapped, bool readWriteEnabled)
        {
            _calc_mem = 0;
            switch (format)
            {
                case TextureFormat.BC4:
                case TextureFormat.DXT1:
                case TextureFormat.DXT1Crunched:
                case TextureFormat.EAC_R:
                case TextureFormat.EAC_R_SIGNED:
                case TextureFormat.ETC_RGB4:
                case TextureFormat.ETC_RGB4Crunched:
                case TextureFormat.ETC2_RGB:
                case TextureFormat.PVRTC_RGB4:
                case TextureFormat.PVRTC_RGBA4:       // 4 bit/pixel
                    {
                        _calc_mem = (long)(width * height / 2f);

                        break;
                    }
                case TextureFormat.Alpha8:
                case TextureFormat.R8:
                case TextureFormat.BC5:
                case TextureFormat.BC6H:
                case TextureFormat.BC7:
                case TextureFormat.DXT5:
                case TextureFormat.DXT5Crunched:
                case TextureFormat.EAC_RG:
                case TextureFormat.EAC_RG_SIGNED:
                case TextureFormat.ETC2_RGBA8:
                case TextureFormat.ETC2_RGBA8Crunched:  // 8 bit/pixel
                    {
                        _calc_mem = width * height;
                        break;
                    }
                // ASTC is using 128 bits to store n×n pixels
#if UNITY_2019_1_OR_NEWER
                case TextureFormat.ASTC_4x4:
                case TextureFormat.ASTC_HDR_4x4:
#endif
                case TextureFormat.ASTC_RGBA_4x4:
                    {
                        _calc_mem = (long)(width * height / (4 * 4) * 16f);
                        break;
                    }
                case TextureFormat.ASTC_RGB_5x5:
                case TextureFormat.ASTC_RGBA_5x5:
#if UNITY_2019_1_OR_NEWER
                case TextureFormat.ASTC_HDR_5x5:
#endif
                    {
                        _calc_mem = (long)(width * height / (5 * 5) * 16f);
                        break;
                    }
                case TextureFormat.ASTC_RGB_6x6:
                case TextureFormat.ASTC_RGBA_6x6:
#if UNITY_2019_1_OR_NEWER
                case TextureFormat.ASTC_HDR_6x6:
#endif
                    {
                        _calc_mem = (long)(width * height / (6 * 6) * 16f);
                        break;
                    }
                case TextureFormat.ASTC_RGB_8x8:
                case TextureFormat.ASTC_RGBA_8x8:
#if UNITY_2019_1_OR_NEWER
                case TextureFormat.ASTC_HDR_8x8:
#endif
                    {
                        _calc_mem = (long)(width * height / (8 * 8) * 16f);
                        break;
                    }
                case TextureFormat.ASTC_RGB_10x10:
                case TextureFormat.ASTC_RGBA_10x10:
#if UNITY_2019_1_OR_NEWER
                case TextureFormat.ASTC_HDR_10x10:
#endif
                    {
                        _calc_mem = (long)(width * height / (10 * 10) * 16f);
                        break;
                    }
                case TextureFormat.ASTC_RGB_12x12:
                case TextureFormat.ASTC_RGBA_12x12:
#if UNITY_2019_1_OR_NEWER
                case TextureFormat.ASTC_HDR_12x12:
#endif
                    {
                        _calc_mem = (long)(width * height / (12 * 12) * 16f);
                        break;
                    }
                case TextureFormat.ARGB4444: //2B/px
                case TextureFormat.R16:
                case TextureFormat.RG16:
                case TextureFormat.RHalf:
                case TextureFormat.RGB565:
                case TextureFormat.RGBA4444:
                    {
                        _calc_mem = (long)(width * height * 2f);
                        break;
                    }

                // 3B/px
                //   {
                //       _calc_mem = (long)(width * height * 3f );
                //       break;
                //   }
                case TextureFormat.RGB24:
                case TextureFormat.ARGB32: // 4B/px
                case TextureFormat.RGBA32:
                case TextureFormat.BGRA32:
                case TextureFormat.RGHalf:
                case TextureFormat.RFloat:
                    {
                        _calc_mem = (long)(width * height * 4f);
                        break;
                    }
                case TextureFormat.RGBAHalf:
                case TextureFormat.RGFloat: //8B/px
                    {
                        _calc_mem = (long)(width * height * 8f);
                        break;
                    }

                case TextureFormat.RGBAFloat: //16B/px               
                    {
                        _calc_mem = (long)(width * height * 16f);
                        break;
                    }
                default:
                    {
                        _calc_mem = 0;
                        break;
                    }
            }
            return (long)(_calc_mem * (readWriteEnabled ? 2 : 1) * (mipmapped ? FourThird : 1f));
        }
    }
}
