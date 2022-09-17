using System.Globalization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
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
#pragma warning disable 612, 618
    public class AvatarInfoCalc
    {
        public static AvatarInfoCalc Instance { get { return instance; } }
        private AvatarInfoCalc() { }
        private readonly HashSet<string> AllAdditionalShaderKeywords = new HashSet<string>();
        private readonly HashSet<string> AllMaterialNames = new HashSet<string>();
        private readonly HashSet<string> AllShaderNames = new HashSet<string>();
        private readonly HashSet<AvatarInfo.MaterialInfo> AllMaterialInfo = new HashSet<AvatarInfo.MaterialInfo>();
        internal static readonly AvatarInfoCalc instance = new AvatarInfoCalc();
        public bool ShouldLog = true;
        public bool shouldEstimateMeshSize = false;
        private const float FourThirds = (4f / 3f);
        readonly System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
        static readonly long nanosecPerTick = (1000L * 1000L * 1000L) / System.Diagnostics.Stopwatch.Frequency;
        readonly List<Renderer> renderers = new List<Renderer>();
        readonly List<CanvasRenderer> canvasRenderers = new List<CanvasRenderer>();
        private static readonly Dictionary<string, List<string>> ShaderTexturePropertyNames = new Dictionary<string, List<string>>();
        private static readonly Dictionary<Shader, string> ShaderStats = new Dictionary<Shader, string>();
        private static readonly List<int> outNames = new List<int>();
        private static readonly Dictionary<int, string> TexturePropertyNameIDs = new Dictionary<int, string>();
        private static readonly HashSet<string> shaderPasses = new HashSet<string>();

        /*
        readonly List<SkinnedMeshRenderer> skinnedMeshRenderers = new List<SkinnedMeshRenderer>();
        readonly List<MeshRenderer> meshRenderers = new List<MeshRenderer>();
        readonly List<TrailRenderer> trailRenderers = new List<TrailRenderer>();
        readonly List<LineRenderer> lineRenderers = new List<LineRenderer>();
        readonly List<ParticleSystemRenderer> particleSystemRenderers = new List<ParticleSystemRenderer>();
        readonly List<BillboardRenderer>  billboardRenderers = new List<BillboardRenderer>();
        readonly List<SpriteRenderer> spriteRenderers = new List<SpriteRenderer>();
        */

        readonly List<Animator> animators = new List<Animator>();
        readonly List<AudioSource> audioSources = new List<AudioSource>();
        readonly List<AudioStatData> audioStatData = new List<AudioStatData>();
        readonly List<Cloth> cloths = new List<Cloth>();
        readonly List<Collider> colliders = new List<Collider>();
        readonly List<IConstraint> constraints = new List<IConstraint>();
        readonly List<Joint> joints = new List<Joint>();
        readonly List<Light> lights = new List<Light>();
        readonly List<Rigidbody> rigidbodies = new List<Rigidbody>();
        readonly List<Transform> tempTransforms = new List<Transform>();
        readonly List<TextureStatData> textureStatData = new List<TextureStatData>();
        readonly List<Transform> transforms = new List<Transform>();

        //Dynamic Bone
        readonly List<DynamicBone> dynamicBones = new List<DynamicBone>();
        static readonly FieldInfo fi = typeof(DynamicBone).GetField("m_Particles", BindingFlags.NonPublic | BindingFlags.Instance);
        //static readonly FieldInfo particleCount_fi = typeof(DynamicBone).GetField("particleCount", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        //static readonly FieldInfo colliderCount_fi = typeof(DynamicBone).GetField("colliderCount", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        static readonly FieldInfo transformCount_fi = typeof(DynamicBone).GetField("transformCount", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        static readonly MethodInfo dynBoneSetupParticlesMethod = typeof(DynamicBone).GetMethod("SetupParticles", BindingFlags.NonPublic | BindingFlags.Instance);

        // Final IK 
        readonly List<RootMotion.FinalIK.VRIK> VRIKs = new List<RootMotion.FinalIK.VRIK>();
        readonly List<RootMotion.FinalIK.IK> IKs = new List<RootMotion.FinalIK.IK>();

        readonly List<RootMotion.FinalIK.ShoulderRotator> ShoulderRotators = new List<RootMotion.FinalIK.ShoulderRotator>();
        readonly List<RootMotion.FinalIK.FBBIKArmBending> armBends = new List<RootMotion.FinalIK.FBBIKArmBending>();
        readonly List<RootMotion.FinalIK.Grounder> grounders = new List<RootMotion.FinalIK.Grounder>();
        readonly List<RootMotion.FinalIK.RotationLimit> RotationLimits = new List<RootMotion.FinalIK.RotationLimit>();
        readonly List<RootMotion.FinalIK.IKExecutionOrder> ExecOrders = new List<RootMotion.FinalIK.IKExecutionOrder>();
        readonly List<RootMotion.FinalIK.FBBIKHeadEffector> HeadEffectors = new List<RootMotion.FinalIK.FBBIKHeadEffector>();
        readonly List<RootMotion.FinalIK.TwistRelaxer> twistRelaxers = new List<RootMotion.FinalIK.TwistRelaxer>();

        List<Material> materialsCache = new List<Material>();
        readonly List<string> texturePropertyNames = new List<string>();
        readonly HashSet<string> shaderKeywords = new HashSet<string>();
        readonly List<int> leafDepth = new List<int>();
        readonly StringBuilder commonSb = new StringBuilder(1024*1024);
        static readonly StringBuilder staticSB = new StringBuilder();
        readonly HashSet<AudioClip> audioClips = new HashSet<AudioClip>();
        readonly HashSet<Texture> textures = new HashSet<Texture>();
        readonly Dictionary<Texture, Dictionary<string, List<string>>> texturesMaterials = new Dictionary<Texture, Dictionary<string, List<string>>>();
        readonly HashSet<DynamicBoneColliderBase> dbColliders = new HashSet<DynamicBoneColliderBase>();

        readonly HashSet<Mesh> meshes = new HashSet<Mesh>();
        readonly Dictionary<Mesh, MeshStatData> meshStatDataDict = new Dictionary<Mesh, MeshStatData>();
        string log_output;

        public struct MeshStatData
        {
            public uint vertexCount;
            public uint blendShapeCount;
            public uint triangleCount;
            public uint bindPoseCount;
            public uint vertexAttributeCount;
            public ulong VramMeasured;
            public ulong VramCalculated;
            public ulong VramBlendshapes;
            public string name;

            public void ToStringBuilder(StringBuilder sbOut) 
            {
                sbOut.Append("Mesh name: '");
                sbOut.Append(name);
                sbOut.Append("', VramMeasured: ");
                GetBytesReadable(VramMeasured, sbOut);
                sbOut.Append(", VramCalculated: ");
                GetBytesReadable(VramCalculated, sbOut);
                sbOut.Append(", VramBlendshapes: ");
                GetBytesReadable(VramBlendshapes, sbOut);
                sbOut.Append(", vertexCount: ");
                Uint5digitToStringBuilder(vertexCount, sbOut);
                sbOut.Append(", blendShapeCount: ");
                Uint5digitToStringBuilder(blendShapeCount, sbOut);
                sbOut.Append(", triangleCount: ");
                Uint5digitToStringBuilder(triangleCount, sbOut);
                sbOut.Append(", bindPoseCount: ");
                Uint5digitToStringBuilder(bindPoseCount, sbOut);
                sbOut.Append(", vertexAttributeCount: ");
                Uint5digitToStringBuilder(vertexAttributeCount, sbOut);
                sbOut.AppendLine();
            }
        }
        public struct AudioStatData
        {
            public string clipName;
            public uint size;
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

        static readonly StringBuilder getBytesReadableBuilder = new StringBuilder();
        public static string GetBytesReadable(long i)
        {
            if (i <= 0)
                return "0 B ";
            getBytesReadableBuilder.Clear();
            GetBytesReadable((ulong)i, getBytesReadableBuilder);
            return getBytesReadableBuilder.ToString();
        }
        private static readonly string[] intNames = new string[]
        {
            "0","1","2","3","4","5","6","7","8","9",
            "10","11","12","13","14","15","16","17","18","19",
            "20","21","22","23","24","25","26","27","28","29","30"
        };
        private static readonly char[] intNameChars = new char[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' };
        public static string SmallIntToString(int i)
        {
            if (i >= 0 && i <= 30)
            {
                return intNames[i];
            }
            return i.ToString();
        }
        public static void GetBytesReadable(ulong i, StringBuilder stringBuilder)
        {
            if (i <= 0)
            {
                stringBuilder.Append("0 B");
                return;
            }
            if (i < 1024)
            {
                Uint5digitToStringBuilder((uint)i, stringBuilder);
                stringBuilder.Append(" B");
                return;
            }
            string suffix;
            if (i >= 0x40000000)
            {
                suffix = " GB";
                i >>= 20;
            }
            else if (i >= 0x100000)
            {
                suffix = " MB";
                i >>= 10;
            }
            else
            {
                suffix = " kB";
            }
            // 123 456
            ulong t = (i & 1023) * 1000 / 1024;
            i >>= 10;
            Uint5digitToStringBuilder((uint)i, stringBuilder);
            stringBuilder.Append('.');
            if (i >= 1000)
            {
                t += 500;
                t /= 1000;
            }
            else if (i >= 100)
            {
                t += 50;
                t /= 100;
            }
            else if (i >= 10)
            {
                t += 5;
                t /= 10;
            }
            Uint5digitToStringBuilder((uint)t, stringBuilder);
            stringBuilder.Append(suffix);
        }


        private static void Uint5digitToStringBuilder(uint number, StringBuilder sb, bool padLeftFive = false, bool padRightFive = false)
        {
            if (number > 99999)
            {
                UintToStringBuilder(number, sb);
                //sb.Append(i);
                return;
            }
            if (padLeftFive)
                sb.Append(' ');
            if (number == 0)
            {
                if (padLeftFive)
                    sb.Append("   0");
                else if (padRightFive)
                    sb.Append("0    ");
                else
                    sb.Append('0');
                return;
            }
            var tenthousands = number / 10000;
            var decrement = tenthousands * 10000;
            var thousands = (number - decrement) / 1000;
            decrement += thousands * 1000;
            var hundresds = (number - decrement) / 100;
            decrement += hundresds * 100;
            var tens = (number - decrement) / 10;
            decrement += tens * 10;
            var ones = number - decrement;
            int significantDigits = 1;
            bool isSignificant = tenthousands > 0;
            significantDigits += isSignificant ? 1 : 0;
            AppendDigit(tenthousands, sb, isSignificant, padLeftFive);
            isSignificant |= thousands > 0;
            significantDigits += isSignificant ? 1 : 0;
            AppendDigit(thousands, sb, isSignificant, padLeftFive);
            isSignificant |= hundresds > 0;
            significantDigits += isSignificant ? 1 : 0;
            AppendDigit(hundresds, sb, isSignificant, padLeftFive);
            isSignificant |= tens > 0;
            significantDigits += isSignificant ? 1 : 0;
            AppendDigit(tens, sb, isSignificant, padLeftFive);
            AppendDigit(ones, sb, true, padLeftFive);
            if (!padRightFive)
                return;
            for (int i = 0; i < 5 - significantDigits; i++)
                sb.Append(' ');
        }
        private static void AppendDigit(uint digit, StringBuilder sb, bool significant, bool padLeftFive)
        {
            if (significant)
                sb.Append(intNameChars[digit]);
            else if (padLeftFive)
                sb.Append(' ');
        }
        private static readonly ulong[] PowersOf10 = new ulong[]
        {
            1, 10, 100, 1000, 10000, 100000, 1000000, 10000000, 100000000,
            1000000000, 10000000000, 100000000000, 1000000000000, 10000000000000,
            100000000000000, 1000000000000000, 10000000000000000, 100000000000000000, 
            1000000000000000000, 10000000000000000000
        };
        private static void UintToStringBuilder(ulong i, StringBuilder sb)
        {
            ulong decrease = 0;
            bool firstSignificant = false;
            for (int j = 19; j >= 0; j--)
            {
                ulong value = PowersOf10[j];
                var digit = (i - decrease) / value; 
                if (!firstSignificant && digit == 0)
                    continue;
                decrease += digit * value;
                firstSignificant = true;
                sb.Append(intNameChars[digit]);
            }
        }
        // 101  ,  0 | 1 | 2 | 3    || 
        // 11   ,  0 | 1 | 2 | 3    || 
        // 1    ,  0 | 1 | 2 | 3    || 

        private static ulong TrimRemainder(ulong t, int shift)
        {
            if (t > 1000)
                return t >> (shift - 1);
            if (t > 100)
                return t >> (shift);
            if (t > 10)
                return t >> (shift + 1);
            return t;
        }
        private static void AppendLine(StringBuilder sb, string text, object value)
        {
            sb.Append(text);
            sb.Append(value);
            sb.AppendLine();
        }/*
        public static string ShortStats(GameObject target)
        {
            AvatarInfo avatarInfo = new AvatarInfo();
            Instance.ShouldLog = false;
            Instance.CountObjects(target, ref avatarInfo);
            Instance.CheckAudio(target, ref avatarInfo);
            Instance.CheckDB(target, ref avatarInfo, false);
            Instance.CheckRenderers(target, ref avatarInfo);
            Instance.CheckTextures(target, ref avatarInfo);
            Instance.CheckCloth(target, ref avatarInfo);
            StringBuilder sb = new StringBuilder();
            AppendLine(sb, "VRAM usage: ", GetBytesReadable(avatarInfo.VRAM));
            AppendLine(sb, "AudioClips: ", GetBytesReadable(avatarInfo.AudioClipSize));
            AppendLine(sb, "AudioClip Count: ", avatarInfo.AudioClipCount);
            AppendLine(sb, "AudioClip Length [sec]: ", avatarInfo.AudioClipLength);
            AppendLine(sb, "AudioSources: ", avatarInfo.AudioSources);
            AppendLine(sb, "Faces (tri or quad): ", avatarInfo.FaceCount);
            AppendLine(sb, "Mesh renderers: ", avatarInfo.meshRenderers);
            AppendLine(sb, "Skinned mesh renderers: ", avatarInfo.skinnedMeshRenderers);
            AppendLine(sb, "Line or trail renderers: ", avatarInfo.lineTrailRenderers);
            AppendLine(sb, "Cloths: ", avatarInfo.clothNumber);
            AppendLine(sb, "Cloth vert count: ", avatarInfo.clothVertCount);
            AppendLine(sb, "Dynamic Bone transform count: ", avatarInfo.dbParticleCount);
            AppendLine(sb, "Dynamic Bone collision count: ", avatarInfo.dbCollisionCount);
            AppendLine(sb, "Material count: ", avatarInfo.materialCount);
            AppendLine(sb, "RigidBody count: ", avatarInfo.RigidBodyCount);
            AppendLine(sb, "Collider count: ", avatarInfo.ColliderCount);
            AppendLine(sb, "Joint count: ", avatarInfo.JointCount);
            AppendLine(sb, "Constraint count: ", avatarInfo.ConstraintCount);
            AppendLine(sb, "Lights: ", avatarInfo.Lights);
            AppendLine(sb, "Skinned bones (VRC): ", avatarInfo.skinnedBonesVRC);
            AppendLine(sb, "Additional shader keywords: ", avatarInfo.additionalShaderKeywords.Length);
            return sb.ToString();
        }*/
        public void CheckCloth(GameObject ava, ref AvatarInfo _AvatarInfo)
        {
            _AvatarInfo.clothNumber = 0;
            _AvatarInfo.clothDiff = 0;
            _AvatarInfo.clothVertCount = 0;
            ava.GetComponentsInChildren(true, cloths);
            foreach (Cloth cloth in cloths)
            {
                int clothVertCount = cloth.vertices.Length;
                _AvatarInfo.clothVertCount += clothVertCount;
                _AvatarInfo.clothDiff += clothVertCount * cloth.clothSolverFrequency;
                _AvatarInfo.clothNumber++;
            }
            cloths.Clear();
        }
        public void CheckDB(GameObject ava, ref AvatarInfo _AvatarInfo, bool initDb)
        {
            int dbCount = 0;
            int collCount = 0;
            int totalTransformCount = 0;
            ava.GetComponentsInChildren(true, dynamicBones);
            _AvatarInfo.dbScriptCount = dynamicBones.Count;
            bool fieldInfoExists = transformCount_fi != null;
            foreach (DynamicBone db in dynamicBones)
            {
                if (!db)
                    continue;
                try
                {
                    dbColliders.Clear();
                    if (fieldInfoExists)
                        CountDbWithFieldInfo(initDb, ref dbCount, ref collCount, ref totalTransformCount, db);
                    else
                        CountDbWithoutFieldInfo(initDb, ref dbCount, ref collCount, ref totalTransformCount, db);
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
            }
            dynamicBones.Clear();
            dbColliders.Clear();
            _AvatarInfo.dbTransformCount = totalTransformCount;
            _AvatarInfo.dbParticleCount = dbCount;
            _AvatarInfo.dbCollisionCount = collCount;
        }

        private void CountDbWithoutFieldInfo(bool initDb, ref int dbCount, ref int collCount, ref int totalTransformCount, DynamicBone db)
        {
            var dbParticlesList = fi.GetValue(db) as IList;
            if (dbParticlesList == null || dbParticlesList.Count == 0)
            {
                if (!initDb)
                    return;
                dbParticlesList = InitDb(db);
            }
            if (dbParticlesList == null || dbParticlesList.Count <= 0)
                return;
            int endbones = 0;
            int transformCount = 0;
            foreach (object item in dbParticlesList)
            {
                var type = item.GetType();
                var transform = (Transform)type.GetField("m_Transform", BindingFlags.Public | BindingFlags.Instance).GetValue(item);
                // endbones don't have a corresponding transform
                if (transform == null)
                    endbones++;
                else
                    transformCount++;
            }
            totalTransformCount += transformCount;

            dbCount += dbParticlesList.Count;
            if (db.m_Colliders == null || db.m_Colliders.Count <= 0)
                return;

            dbColliders.UnionWith(db.m_Colliders);
            dbColliders.RemoveWhere(a => a == null);
            // Endbones apparently don't count in collision checks?
            if (transformCount <= 0)
                return;
            //                                  Root doesn't count as collider either
            collCount += (Mathf.Max(transformCount - 1, 0) * dbColliders.Count);
        }

        private void CountDbWithFieldInfo(bool initDb, ref int dbCount, ref int collCount, ref int totalTransformCount, DynamicBone db)
        {
            int transformCount = (int)transformCount_fi.GetValue(db);
            totalTransformCount += transformCount;
            var dbParticlesList = fi.GetValue(db) as IList;
            if (dbParticlesList == null || dbParticlesList.Count == 0)
            {
                if (!initDb)
                    return;
                dbParticlesList = InitDb(db);
            }
            dbCount += (int)dbParticlesList?.Count;
            if (db.m_Colliders == null || db.m_Colliders.Count <= 0)
                return;
            dbColliders.UnionWith(db.m_Colliders);
            dbColliders.RemoveWhere(a => a == null);
            if (transformCount > 0) // Endbones apparently don't count as colliders?
                collCount += (Mathf.Max(transformCount - 1, 0) * dbColliders.Count); // Root doesn't count as collider either
        }

        private static IList InitDb(DynamicBone db)
        {
            dynBoneSetupParticlesMethod.Invoke(db, new object[0]);
            return fi.GetValue(db) as IList;
        }

        public void CountObjects(GameObject gameObject, ref AvatarInfo avatarInfo)
        {
            gameObject.GetComponentsInChildren(true, transforms);
            gameObject.GetComponentsInChildren(true, joints);
            gameObject.GetComponentsInChildren(true, rigidbodies);
            gameObject.GetComponentsInChildren(true, constraints);
            gameObject.GetComponentsInChildren(true, animators);
            gameObject.GetComponentsInChildren(true, lights);
            gameObject.GetComponentsInChildren(true, colliders);

            // Final IK
            gameObject.GetComponentsInChildren(true, VRIKs);
            gameObject.GetComponentsInChildren(true, IKs);
            gameObject.GetComponentsInChildren(true, twistRelaxers);
            gameObject.GetComponentsInChildren(true, ShoulderRotators);
            gameObject.GetComponentsInChildren(true, armBends);
            gameObject.GetComponentsInChildren(true, grounders);
            gameObject.GetComponentsInChildren(true, RotationLimits);
            gameObject.GetComponentsInChildren(true, ExecOrders);
            gameObject.GetComponentsInChildren(true, HeadEffectors);

            avatarInfo.VRIKComponents = VRIKs.Count;
            avatarInfo.OtherFinalIKComponents = IKs.Count - avatarInfo.VRIKComponents + twistRelaxers.Count + ShoulderRotators.Count +
                armBends.Count + grounders.Count + RotationLimits.Count + ExecOrders.Count + HeadEffectors.Count;
            avatarInfo.TwistRelaxers = twistRelaxers.Count;

            avatarInfo.TransformCount = transforms.Count;
            avatarInfo.JointCount = joints.Count;
            avatarInfo.RigidBodyCount = rigidbodies.Count;
            avatarInfo.ConstraintCount = constraints.Count;
            avatarInfo.Animators = animators.Count;
            avatarInfo.Lights = lights.Count;
            avatarInfo.ColliderCount = colliders.Count;
            leafDepth.Clear();
            transforms.Clear();
            DFSGetChildren(gameObject.transform, 0);
            avatarInfo.MaxHiearchyDepth = GetMaxInList(leafDepth, out int index);
            avatarInfo.LongestPath = GetLeafPath(index);
            if (ShouldLog)
            {
                Debug.Log(LeavesToString());
                Debug.Log(avatarInfo.LongestPath);
                Debug.Log(leafDepth.Max() + " " + transforms[leafDepth.IndexOf(leafDepth.Max())].name);
            }
            commonSb.Clear();
            tempTransforms.Clear();
            leafDepth.Clear();
            transforms.Clear();
            joints.Clear();
            rigidbodies.Clear();
            constraints.Clear();
            animators.Clear();
            lights.Clear();
            colliders.Clear();

            // Final IK
            VRIKs.Clear();
            IKs.Clear();
            twistRelaxers.Clear();
        }
        private int GetMaxInList(IList<int> list, out int index)
        {
            int max = 0;
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
            //output = "";
            commonSb.Clear();
            for (int i = 0, length = leafDepth.Count; i < length; i++)
            {
                commonSb.Append(transforms[i].name);
                commonSb.Append(" (");
                commonSb.Append(SmallIntToString(leafDepth[i]));
                commonSb.AppendLine(")");
                //  output += $"{transforms[i].name} ({leafDepth[i]})\r\n";
            }
            return commonSb.ToString();
        }
        private string GetLeafPath(int index)
        {
            if (transforms.Count == 0 || transforms.Count < index)
            {
                return "";
            }
            transforms[index].GetComponentsInParent(true, tempTransforms);
            if (tempTransforms.Count == 0)
            {
                return "";
            }
            commonSb.Clear();
            commonSb.Append(tempTransforms[tempTransforms.Count - 1].name);
            for (int i = tempTransforms.Count - 2; i >= 0; i--)
            {
                commonSb.Append("\\");
                commonSb.Append(tempTransforms[i].name);
            }
            return commonSb.ToString();
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
            _AvatarInfo.FaceCount = 0;
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
            renderers.Clear();
            ava.GetComponentsInChildren(true, renderers);
            meshes.Clear();
            for (int i = 0; i < renderers.Count; i++)
            {
                var renderer = renderers[i];
                _AvatarInfo.materialCount += renderer.sharedMaterials.Length;
                if (renderer is SkinnedMeshRenderer)
                {
                    var skinnedMeshRenderer = renderer as SkinnedMeshRenderer;
                    var mesh = skinnedMeshRenderer.sharedMesh;
                    meshes.Add(mesh);
                    _AvatarInfo.FaceCount += CountMesh(mesh, out int _);
                    transforms.AddRange(skinnedMeshRenderer.bones);
                    _AvatarInfo.skinnedMeshRenderers++;
                }
                else if (renderer is MeshRenderer)
                {
                    _AvatarInfo.FaceCount += CountMeshRendererTris(renderer as MeshRenderer);
                    _AvatarInfo.meshRenderers++;
                }
                else if (renderer is TrailRenderer)
                {
                    _AvatarInfo.lineTrailRendererTriCount += CountTrailRendererTris(renderer as TrailRenderer);
                    _AvatarInfo.lineTrailRenderers++;
                }
                else if (renderer is LineRenderer)
                {
                    _AvatarInfo.lineTrailRendererTriCount += CountLineRendererTris(renderer as LineRenderer);
                    _AvatarInfo.lineTrailRenderers++;
                }
                else if (renderer is ParticleSystemRenderer)
                {
                    _AvatarInfo.particleSystems++;
                    var psRenderer = renderer as ParticleSystemRenderer;
                    if (psRenderer)
                    {
                        var mesh = psRenderer.mesh;
                        meshes.Add(mesh);
                        _AvatarInfo.FaceCount += CountMesh(mesh, out int _);
                    }
                    var particleSystem = renderer.GetComponent<ParticleSystem>();
                    if (particleSystem)
                        _AvatarInfo.maxParticles += particleSystem.main.maxParticles;

                }
                else if (renderer is BillboardRenderer || renderer is SpriteRenderer || renderer is UnityEngine.Tilemaps.TilemapRenderer)
                {
                    _AvatarInfo.otherRenderers++;
                }
            }
            meshStatDataDict.Clear();
            ulong vram = 0;
            ulong vramProfiler = 0;
            ulong vramBlendshapes = 0;
            foreach (var mesh in meshes)
            {
                vram += CountMeshVram(mesh, out ulong _vramProfiler, out ulong _vramBlendshapes);
                vramProfiler += _vramProfiler;
                vramBlendshapes += _vramBlendshapes;
            }
            _AvatarInfo.VramBlendshapes = vramBlendshapes;
            _AvatarInfo.VRAM_MeshesProfiler = vramProfiler;
            _AvatarInfo.VRAM_Meshes = vram;

            LogMeshVram(_AvatarInfo);

            canvasRenderers.Clear();
            ava.GetComponentsInChildren(true, canvasRenderers);
            foreach (var item in canvasRenderers)
            {
                _AvatarInfo.otherRenderers++;
                _AvatarInfo.materialCount += item.materialCount;
            }
            canvasRenderers.Clear();

            _AvatarInfo.skinnedBones = transforms.Count;
            _AvatarInfo.skinnedBonesVRC = CountDistinct(transforms);
            transforms.Clear();
        }

        private void LogMeshVram(AvatarInfo _AvatarInfo)
        {
            commonSb.Clear();
            commonSb.Append(meshStatDataDict.Count);
            commonSb.Append(" Meshes take ");
            GetBytesReadable(_AvatarInfo.VRAM_MeshesProfiler, commonSb);
            commonSb.Append(" (profiled) | ");
            GetBytesReadable(_AvatarInfo.VRAM_Meshes + _AvatarInfo.VramBlendshapes, commonSb);
            commonSb.AppendLine(" (calculated) VRAM.");
            foreach (var item in meshStatDataDict)
            {
                item.Value.ToStringBuilder(commonSb);
            }
            _AvatarInfo.AvatarInfoString += commonSb.ToString();
            commonSb.Clear();
        }

        private static readonly HashSet<Transform> tempDbHashSet = new HashSet<Transform>();
        private static readonly HashSet<int> tempIntMap = new HashSet<int>();
        private unsafe int CountDistinct(List<Transform> collection)
        {
            if (collection == null || collection.Count < 2)
            {
                return collection == null ? 0 : collection.Count;
            }
            tempIntMap.Clear();
            var listCount = collection.Count;
            for (int i = 0; i < listCount; i++)
                tempIntMap.Add(collection[i]?.GetInstanceID() ?? 0);
            return tempIntMap.Count();


          /*  tempDbHashSet.Clear();
            tempDbHashSet.UnionWith(collection);
            int count = tempDbHashSet.Count;
            tempDbHashSet.Clear();
            return count;*/
        }
        public static int OptimizedDistinctAndCount<TSource>(IEnumerable<TSource> source, int numberOfElements)
        {
            if (source == null) return 0;
            var set = new HashSet<TSource>(source);
            return set.Count;
        }
        private readonly List<Matrix4x4> bindPoses = new List<Matrix4x4>();
        private readonly Dictionary<VertexAttributeFormat, byte> VertexAttributeFormatSize = new Dictionary<VertexAttributeFormat, byte>
        {
            { VertexAttributeFormat.SInt32, 4 },
            { VertexAttributeFormat.UInt32, 4 },
            { VertexAttributeFormat.Float32, 4 },
            { VertexAttributeFormat.Float16, 2 },
            { VertexAttributeFormat.SNorm16, 2 },
            { VertexAttributeFormat.UNorm16, 2 },
            { VertexAttributeFormat.SInt16, 2 },
            { VertexAttributeFormat.UInt16, 2 },
            { VertexAttributeFormat.SNorm8, 1 },
            { VertexAttributeFormat.UNorm8, 1 },
            { VertexAttributeFormat.UInt8, 1 },
            { VertexAttributeFormat.SInt8, 1 },
        };
        private ulong CountMeshVram(Mesh mesh, out ulong vramProfiler, out ulong vramBlendshapes)
        {
            if (!mesh)
            {
                vramBlendshapes = 0;
                vramProfiler = 0;
                return 0;
            }

            ulong vram = 0;
            int vertexCount = mesh.vertexCount;
            int bytesPerVert = 0;
            int vertexAttributeCount = mesh.vertexAttributeCount;
            for (int i = 0; i < vertexAttributeCount; i++)
            {
                var attribs = mesh.GetVertexAttribute(i);
                var dim = attribs.dimension;
                var size = VertexAttributeFormatSize[attribs.format];
                bytesPerVert += size * dim;
            }
            vram += (ulong)(vertexCount * bytesPerVert);
            var blendShapeCount = mesh.blendShapeCount;

            mesh.GetBindposes(bindPoses);
            int bindPoseCount = bindPoses.Count;
            vram += (ulong)(bindPoseCount * 64);
            bindPoses.Clear();
            var allBones = mesh.GetAllBoneWeights();
            vram += (ulong)(allBones.Length * 8);
            var triCount = CountMesh(mesh, out int indiciesCount);
            vram += (ulong)(indiciesCount * 4);
            vramProfiler = (ulong)Profiler.GetRuntimeMemorySizeLong(mesh);
            if (mesh.isReadable)
                vramProfiler /= 2;
            /*     if (Profiler.supported)
                 {
                     meshStatDataDict[mesh] = new MeshStatData
                     {
                         bindPoseCount = (uint)bindPoseCount,
                         blendShapeCount = (uint)blendShapeCount,
                         name = mesh.name,
                         triangleCount = (uint)triCount,
                         vertexAttributeCount = (uint)vertexAttributeCount,
                         vertexCount = (uint)vertexCount,
                         VramCalculated = vram,
                         VramMeasured = vramProfiler,
                         VramBlendshapes = vertexCount * blendShapeCount * 12
                     };
                     return vram;
                 }*/
            vramBlendshapes = 0;
            if (shouldEstimateMeshSize)
            {
                indexCache.Clear();
                FillIndexCache(mesh);

                int blendShapeFrameCount = 0;
                var pos = new Vector3[vertexCount];
                var norm = new Vector3[vertexCount];
                var tang = new Vector3[vertexCount];
                ulong blendShapeDataSize = 0;
                for (int i = 0; i < blendShapeCount; i++)
                {
                    var frameCount = mesh.GetBlendShapeFrameCount(i);
                    blendShapeFrameCount += frameCount;
                    for (int j = 0; j < frameCount; j++)
                    {
                        blendShapeDataSize += GetBlendShapeArraySize(mesh, pos, norm, tang, i, j);
                    }
                }
                vramBlendshapes = blendShapeDataSize;
                indexCache.Clear();
            }
            meshStatDataDict[mesh] = new MeshStatData
            {
                bindPoseCount = (uint)bindPoseCount,
                blendShapeCount = (uint)blendShapeCount,
                name = mesh.name,
                triangleCount = (uint)triCount,
                vertexAttributeCount = (uint)vertexAttributeCount,
                vertexCount = (uint)vertexCount,
                VramCalculated = vram,
                VramMeasured = vramProfiler,
                VramBlendshapes = vramBlendshapes
            };
            return vram;
        }

        private void FillIndexCache(Mesh mesh)
        {
            var subMeshCount = mesh.subMeshCount;
            for (int i = 0; i < subMeshCount; i++)
            {
                mesh.GetIndices(indicies, i);
                intCache.Clear();
                intCache.UnionWith(indicies);
                indexCache.Add(i, intCache.ToArray());
            }
            intCache.Clear();
        }

        private readonly List<int> indicies = new List<int>();
        private readonly HashSet<int> intCache = new HashSet<int>();
        private readonly Dictionary<int, int[]> indexCache = new Dictionary<int, int[]>();
        private ulong GetBlendShapeArraySize(Mesh mesh, Vector3[] pos, Vector3[] norm, Vector3[] tang, int shape, int frame)
        {
            mesh.GetBlendShapeFrameVertices(shape, frame, pos, norm, tang);

            ulong size = 0;
            var subMeshCount = mesh.subMeshCount;

            for (int i = 0; i < subMeshCount; i++)
            {
                var _indicies = indexCache[i];
                ulong length = (ulong)_indicies.Length;
                bool hasPos = false;
                bool hasNorm = false;
                bool hasTang = false;
                for (int j = 0; j < _indicies.Length; j++)
                {
                    int item = _indicies[j];
                    if (!hasPos)
                    {
                        var a = pos[item];
                        if (a.x != 0 && a.y != 0 && a.z != 0)
                            hasPos = true;
                    }
                    if (!hasNorm)
                    {
                        var a = norm[item];
                        if (a.x != 0 && a.y != 0 && a.z != 0)
                            hasNorm = true;
                    }
                    if (!hasTang)
                    {
                        var a = tang[item];
                        if (a.x != 0 && a.y != 0 && a.z != 0)
                            hasTang = true;
                    }
                    if (hasPos && hasNorm && hasTang)
                        break;
                }
                if (hasPos)
                    size += length * 12;
                if (hasNorm)
                    size += length * 12;
                if (hasTang)
                    size += length * 12;
            }
            return size;
        }

        private int CountMesh(Mesh mesh, out int indiciesCount)
        {
            indiciesCount = 0;
            if (!mesh)
            {
                return 0;
            }
            int faceCounter = 0;
            for (int i = 0, length = mesh.subMeshCount; i < length; i++)
            {
                MeshTopology topology;
#if UNITY_2019_1_OR_NEWER
                SubMeshDescriptor submesh = mesh.GetSubMesh(i);
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
                            indiciesCount += submesh.indexCount;
                            faceCounter += (submesh.indexCount / 4);
                            continue;
                        }
                    case MeshTopology.Triangles:
                        {
                            indiciesCount += submesh.indexCount;
                            faceCounter += (submesh.indexCount / 3);
                            continue;
                        }
#else
                    case MeshTopology.Quads:
                        {
                            indiciesCount += (int)mesh.GetIndexCount(i);
                            faceCounter += (int)(mesh.GetIndexCount(i) / 4);
                            continue;
                        }
                    case MeshTopology.Triangles:
                        {
                            indiciesCount += (int)mesh.GetIndexCount(i);
                            faceCounter += (int)(mesh.GetIndexCount(i) / 3);
                            continue;
                        }
#endif
                }
            }
            return faceCounter;
        }

        private int GetBlendShapeArrayCount(Vector3[] pos, Vector3[] norm, Vector3[] tang)
        {
            int count = 0;
            if (pos != null && pos.Any(a => !a.Equals(Vector3.zero)))
                count++;
            if (norm != null && norm.Any(a => !a.Equals(Vector3.zero)))
                count++;
            if (tang != null && tang.Any(a => !a.Equals(Vector3.zero)))
                count++;
            return count;
        }

        private int CountMeshRendererTris(MeshRenderer renderer)
        {
            MeshFilter meshFilter = renderer.gameObject.GetComponent<MeshFilter>();
            if (meshFilter)
            {
                var mesh = meshFilter.sharedMesh;
                meshes.Add(mesh);
                return CountMesh(mesh, out int _);
            }
            return 0;
        }
        private ulong CountTrailRendererTris(TrailRenderer renderer)
        {
            if (renderer)
            {
                return (ulong)(renderer.time * 100);
            }
            return 0;
        }
        private ulong CountLineRendererTris(LineRenderer renderer)
        {
            if (renderer)
                return (ulong)(renderer.positionCount * 2); // idk..
            return 0;
        }
        public void CheckAudio(GameObject ava, ref AvatarInfo _AvatarInfo)
        {
            audioClips.Clear();
            ava.GetComponentsInChildren(true, audioSources);
            _AvatarInfo.AudioSources = audioSources.Count;
            for (int i = 0; i < audioSources.Count; i++)
            {
                AudioSource audioSource = audioSources[i];
                if (audioSource != null && audioSource.clip != null)
                    audioClips.Add(audioSource.clip);
            }
            long _totalSize = 0;
            if (ShouldLog) commonSb.Clear();
            float length = 0;
            audioStatData.Clear();
            _AvatarInfo.AudioClipCount = audioClips.Count;
            foreach (AudioClip audioClip in audioClips)
            {
                uint _size = (uint)(audioClip.samples * audioClip.channels) * 2; // 16 bit samples = 2 Bytes
                _totalSize += _size;
                length += audioClip.length;
                if (ShouldLog) audioStatData.Add(new AudioStatData {
                    clipName = audioClip.name,
                    size = _size,
                    length = audioClip.length,
                    channels = audioClip.channels,
                    frequency = audioClip.frequency,
                    loadInBackground = audioClip.loadInBackground,
                    loadType = audioClip.loadType
                });
            }
            _AvatarInfo.AudioClipLength = (int)length;
            _AvatarInfo.AudioClipSize = _totalSize;
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
            commonSb.Clear();
            commonSb.Append(_AvatarInfo.AudioClipCount);
            commonSb.Append(" audio clips");
            if (audioClips.Count > 0)
            {
                commonSb.Append(" with ");
                GetBytesReadable((ulong)_AvatarInfo.AudioClipSize, commonSb);
                commonSb.Append(" estimated size");
            }
            commonSb.AppendLine(".");
            var _audioStatData = audioStatData.OrderByDescending(a => a.size);
            foreach (var audioClip in _audioStatData)
            {
                commonSb.Append("Size: ");
                GetBytesReadable(audioClip.size, commonSb);
                //sb.Append(GetBytesReadable(audioClip.size).PadRight(8)); 
                commonSb.Append(", loadInBackground: ");
                commonSb.Append(audioClip.loadInBackground);
                commonSb.Append(", loadType: ");
                commonSb.Append(audioClipLoadTypes[(int)audioClip.loadType]);
                commonSb.Append(", length: ");
                Uint5digitToStringBuilder((uint)audioClip.length, commonSb);
                commonSb.Append(" sec");
                //sb.Append(audioClip.length.ToString("0.## sec", CultureInfo.InvariantCulture).PadLeft(10));
                commonSb.Append(", channels: ");
                Uint5digitToStringBuilder((uint)audioClip.channels, commonSb);
                // sb.Append(audioClip.channels);
                commonSb.Append(", frequency: ");
                Uint5digitToStringBuilder((uint)audioClip.frequency, commonSb);
                //sb.Append(audioClip.frequency);
                commonSb.Append(" name: ");
                commonSb.Append(audioClip.clipName);
                commonSb.AppendLine();
            }
            return commonSb.ToString();
        }
        static readonly string[] audioClipLoadTypes = new string[] { " Decompress on load ", "Compressed in memory", "     Streaming      " };
        public void CheckTextures(GameObject ava, ref AvatarInfo _AvatarInfo)
        {
            if (ShouldLog) 
            {
                stopwatch.Restart();
            }
            _AvatarInfo.passCount = 0;
            textures.Clear();
            texturesMaterials.Clear();
            shaderKeywords.Clear();
            AllMaterialInfo.Clear();
            AllShaderNames.Clear();
            AllMaterialNames.Clear();
            ava.GetComponentsInChildren(true, renderers);
            foreach (var rend in renderers)
            {
                string rendererName = rend.name;
                rend.GetSharedMaterials(materialsCache);
                foreach (var material in materialsCache)
                {
                    try
                    {
                        CheckMaterial(material, _AvatarInfo, rendererName);
                    }
                    catch (Exception e) {
                        Debug.LogError(e);
                    }
                }
            }
            renderers.Clear();
            _AvatarInfo.materialInfo = AllMaterialInfo.ToArray();
            _AvatarInfo.shaderNames = AllShaderNames.ToArray();
            _AvatarInfo.materialNames = AllMaterialNames.ToArray();
            _AvatarInfo.additionalShaderKeywords = shaderKeywords.Except(defaultKeywords).ToArray();
            AllAdditionalShaderKeywords.UnionWith(_AvatarInfo.additionalShaderKeywords);
            _AvatarInfo.additionalShaderKeywordCount = _AvatarInfo.additionalShaderKeywords.Length;
            _AvatarInfo.textureMem = 0;
            _AvatarInfo.calc_mem = 0;
            long profiler_mem = 0;
            ulong vram = 0;
            textureStatData.Clear();
            foreach (Texture texture in textures)
            {
                CheckTexture(texture, _AvatarInfo, ref profiler_mem, ref vram);
            }
            _AvatarInfo.textureMemMB = Math.Round(_AvatarInfo.textureMem / 10485.76f ) / 100f;
            _AvatarInfo.calc_memMB = Math.Round(_AvatarInfo.calc_mem / 10485.76f) / 100f;
            _AvatarInfo.VRAM_Textures = vram;
            if (ShouldLog)
            {
                stopwatch.Stop();
                log_output = AddTextureInfoToLog(_AvatarInfo.textureMem, _AvatarInfo.calc_mem, vram, stopwatch.ElapsedTicks);
                _AvatarInfo.AvatarInfoString += log_output;
                Debug.Log(log_output);
            }
            textures.Clear();
            shaderKeywords.Clear();
        }

        private List<string> GetTexturePropertyNames(string shader, Material material) 
        {
            if (!ShaderTexturePropertyNames.TryGetValue(shader, out List<string> _texturePropertyNames)) 
            {
                _texturePropertyNames = new List<string>();
                material.GetTexturePropertyNames(_texturePropertyNames);
                ShaderTexturePropertyNames.Add(shader, _texturePropertyNames);
            }
            return _texturePropertyNames;
        }
        public static void ClearShaderStats() 
        {
            shaderPasses.Clear();
            ShaderStats.Clear();
        }
        public static string GetShaderStats()
        {
            staticSB.Clear();
            var passes = shaderPasses.AsEnumerable();
            foreach (var pass in passes)
            {
                staticSB.AppendLine(pass);
            }
            staticSB.AppendLine("----------------------------");
            var keys = ShaderStats.Keys.AsEnumerable();
            foreach (var item in keys)
            {
                staticSB.Append(ShaderStats[item]);
            }
            return staticSB.ToString();
        }
        static bool MaterialRecursion = false;
        private void CheckMaterial(Material material, AvatarInfo _AvatarInfo, string rendererName) 
        {
            if (!material)
                return;

          //  texturePropertyNames.Clear();
          //  material.GetTexturePropertyNames(texturePropertyNames);
            _AvatarInfo.passCount += material.passCount;
            string _materialName = material.name;
            AllMaterialNames.Add(_materialName);
            var shader = material.shader;
            if (!ShaderStats.ContainsKey(shader)) 
            {
                commonSb.Clear();
                int _count = material.passCount;
                commonSb.Append(shader.name);
                commonSb.Append(": ");
                commonSb.AppendLine(_count.ToString());
                for (int i = 0; i < _count; i++)
                {
                    commonSb.Append(i);
                    commonSb.Append(": ");
                    var pass = material.GetPassName(i);
                    shaderPasses.Add(pass);
                    commonSb.AppendLine(pass);                    
                }
                ShaderStats[shader] = commonSb.ToString();
            }
            var materialShaderName = material.shader.name;
            AllShaderNames.Add(materialShaderName);
            AllMaterialInfo.Add(new AvatarInfo.MaterialInfo
            {
                name = _materialName,
                renderQueue = (uint)material.renderQueue,
                shaderName = materialShaderName,
#if UNITY_2019_1_OR_NEWER
                shaderPassCount = (uint)shader.passCount,
#endif
                material = material
            });
            shaderKeywords.UnionWith(material.shaderKeywords);
            var _outNames = MaterialRecursion ? new List<int>() : outNames;
            _outNames.Clear();
            material.GetTexturePropertyNameIDs(_outNames);
            if (ShouldLog)
            {
                bool hasMissing = false;
                for (int i = 0; i < _outNames.Count; i++)
                    if (!TexturePropertyNameIDs.ContainsKey(_outNames[i]))
                        hasMissing = true;
                texturePropertyNames.Clear();
                if (hasMissing)
                    material.GetTexturePropertyNames(texturePropertyNames);
                if (_outNames.Count == texturePropertyNames.Count)
                    for (int i = 0; i < _outNames.Count; i++)
                        if (!TexturePropertyNameIDs.ContainsKey(_outNames[i]))
                            TexturePropertyNameIDs[_outNames[i]] = texturePropertyNames[i];
            }
            var prevE = Application.GetStackTraceLogType(LogType.Error);
            Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.None);
            bool prevLog = Debug.unityLogger.logEnabled;
            Debug.unityLogger.logEnabled = false;
            foreach (var _textureID in _outNames)
            {
                var texture = material.GetTexture(_textureID);
                if (texture != null)
                {
                    if (texture.GetType().Equals(typeof(CustomRenderTexture))) 
                    {
                        var crt = texture as CustomRenderTexture;
                        if (crt) 
                        {
                            var prevMaterialRecursion = MaterialRecursion;
                            MaterialRecursion = true;
                            
                            CheckMaterial(crt.initializationMaterial, _AvatarInfo, rendererName + " CRT: " + crt.name);
                            CheckMaterial(crt.material, _AvatarInfo, rendererName + " CRT: " + crt.name);
                            MaterialRecursion = prevMaterialRecursion;
                        }
                    }
                    textures.Add(texture);
                    if (!ShouldLog)
                        continue;
                    string materialName = rendererName + "\\" + _materialName;
                    string _textureName = null;
                    if (TexturePropertyNameIDs.ContainsKey(_textureID))
                    {
                        _textureName = TexturePropertyNameIDs[_textureID];
                    }
                    else
                    {
                        _textureName = _textureID.ToString();
                    }
                    if (texturesMaterials.ContainsKey(texture))
                    {
                        if (!texturesMaterials[texture].ContainsKey(materialName))
                            texturesMaterials[texture].Add(materialName, new List<string>());
                        if (!texturesMaterials[texture][materialName].Contains(_textureName))
                            texturesMaterials[texture][materialName].Add(_textureName);
                    }
                    else
                    {
                        texturesMaterials.Add(texture, new Dictionary<string, List<string>>());
                        texturesMaterials[texture].Add(materialName, new List<string>());
                        texturesMaterials[texture][materialName].Add(_textureName);
                    }
                }
            }
            Debug.unityLogger.logEnabled = prevLog;
            Application.SetStackTraceLogType(LogType.Error, prevE);
        }
        private void CheckTexture(Texture texture, AvatarInfo _AvatarInfo, ref long profiler_mem, ref ulong vram)
        {
            if (Profiler.supported)
            {
                profiler_mem = Profiler.GetRuntimeMemorySizeLong(texture);
                _AvatarInfo.textureMem += profiler_mem;
            }
            long _calc_mem = 0;
            bool readWriteEnabled = texture.isReadable;
            var dim = texture.dimension;
            Type type = texture.GetType();           
            string materialName = "";            
            if (ShouldLog)
            {
                commonSb.Clear();
                if (texturesMaterials.TryGetValue(texture, out Dictionary<string, List<string>> materials))
                {
                    if (materials.Count > 0)
                    {
                        var _materials = materials.Keys.AsEnumerable();
                        int length = _materials.Count();
                        int i = 0;
                        foreach (var material in _materials)
                        {
                            commonSb.Append(material);
                            if (materials[material].Count > 0)
                            {
                                commonSb.Append(" (");
                                for (int j = 0; j < materials[material].Count; j++)
                                {
                                    commonSb.Append(materials[material][j]);
                                    if (j != materials[material].Count - 1)
                                    {
                                        commonSb.Append(", ");
                                    }
                                }
                                commonSb.Append(")");
                            }
                            if (i != material.Length - 1)
                            {
                                commonSb.Append(", ");
                            }
                            i++;
                        }
                        materialName = commonSb.ToString();
                    }
                }
            }
            switch (dim)
            {
                case TextureDimension.Tex2D:
                    {
                        if (type.Equals(typeof(Texture2D)))
                        {
                            Texture2D texture2D = (Texture2D)texture;
                            bool mipmapped = texture2D.mipmapCount > 1;
                            _AvatarInfo.calc_mem += _calc_mem = CalculateMaxMemUse(texture2D.format, texture2D.width, texture2D.height, mipmapped, readWriteEnabled);
                            if (ShouldLog)
                            {
                                textureStatData.Add(new TextureStatData
                                {
                                    type = "Texture2D",
                                    width = texture2D.width,
                                    height = texture2D.height,
                                    format = GetTextureFormat(texture2D.format),
                                    name = texture2D.name,
                                    profiler_mem = profiler_mem,
                                    _calc_mem = _calc_mem,
                                    isReadable = readWriteEnabled,
                                    materialName = materialName,
                                    isMipmapped = mipmapped,
                                    isStreaming = texture2D.streamingMipmaps
                                });
                            }
                            texture2D = null;
                        }
                        else if (type.Equals(typeof(RenderTexture)))
                        {
                            RenderTexture rt = (RenderTexture)texture;
                            _calc_mem = CalculateMaxRTMemUse(rt.format, rt.width, rt.height, rt.useMipMap, profiler_mem, rt.antiAliasing, rt.depth);
                            _AvatarInfo.calc_mem += _calc_mem;
                            if (ShouldLog)
                            {
                                textureStatData.Add(new TextureStatData
                                {
                                    type = "RenderTexture",
                                    width = rt.width,
                                    height = rt.height,
                                    format = rt.format.ToString(),
                                    name = rt.name,
                                    profiler_mem = profiler_mem,
                                    _calc_mem = _calc_mem,
                                    isReadable = readWriteEnabled,
                                    materialName = materialName
                                });
                            }
                            rt = null;
                        }
                        else if (type.Equals(typeof(CustomRenderTexture)))
                        {
                            CustomRenderTexture crt = (CustomRenderTexture)texture;
                            _calc_mem = CalculateMaxRTMemUse(crt.format, crt.width, crt.height, crt.useMipMap, profiler_mem, crt.antiAliasing, crt.depth);
                            _AvatarInfo.calc_mem += _calc_mem;
                            if (ShouldLog)
                            {
                                textureStatData.Add(new TextureStatData
                                {
                                    type = $"CustomRenderTexture (updateMode: {crt.updateMode}, pass: {crt.shaderPass}, pass: {crt.shaderPass})",
                                    width = crt.width,
                                    height = crt.height,
                                    format = crt.format.ToString(),
                                    name = crt.name,
                                    profiler_mem = profiler_mem,
                                    _calc_mem = _calc_mem,
                                    isReadable = readWriteEnabled,
                                    materialName = materialName
                                });
                            }
                            crt = null;
                        }
                        else
                        {
                            if (ShouldLog)
                            {
                                textureStatData.Add(new TextureStatData
                                {
                                    type = texture.GetType().ToString(),
                                    width = texture.width,
                                    height = texture.height,
                                    format = dim.ToString(),
                                    name = texture.name,
                                    profiler_mem = profiler_mem,
                                    _calc_mem = 0,
                                    isReadable = readWriteEnabled,
                                    materialName = materialName
                                });
                            }
                        }
                        break;
                    }
                case TextureDimension.Cube:
                    {
                        if (type.Equals(typeof(Cubemap)))
                        {
                            Cubemap cubemap = (Cubemap)texture;
                            bool mipmapped = cubemap.mipmapCount > 1;
                            _calc_mem = (CalculateMaxMemUse(cubemap.format, cubemap.width, cubemap.height, mipmapped, readWriteEnabled) * 6);
                            _AvatarInfo.calc_mem += _calc_mem;
                            if (ShouldLog)
                            {
                                textureStatData.Add(new TextureStatData
                                {
                                    type = "Cubemap",
                                    width = cubemap.width,
                                    height = cubemap.height,
                                    format = GetTextureFormat(cubemap.format),
                                    name = cubemap.name,
                                    profiler_mem = profiler_mem,
                                    _calc_mem = _calc_mem,
                                    isReadable = readWriteEnabled,
                                    materialName = materialName,
                                    isMipmapped = mipmapped,
                                    isStreaming = cubemap.streamingMipmaps
                                });
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
                            textureStatData.Add(new TextureStatData
                            {
                                type = "CubeArray" + cubemapArray.cubemapCount,
                                width = cubemapArray.width,
                                height = cubemapArray.height,
                                format = GetTextureFormat(cubemapArray.format),
                                name = cubemapArray.name,
                                profiler_mem = profiler_mem,
                                _calc_mem = _calc_mem,
                                materialName = materialName,
                                isMipmapped = mipmapped
                            });
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
                        _AvatarInfo.calc_mem += _calc_mem = CalculateMaxMemUse(texture2DArray.format, texture2DArray.width, texture2DArray.height, mipmapped, false) * texture2DArray.depth;
                        if (ShouldLog)
                        {
                            textureStatData.Add(new TextureStatData
                            {
                                type = "Tex2DArray" + texture2DArray.depth,
                                width = texture2DArray.width,
                                height = texture2DArray.height,
                                format = GetTextureFormat(texture2DArray.format),
                                name = texture2DArray.name,
                                profiler_mem = profiler_mem,
                                _calc_mem = _calc_mem,
                                materialName = materialName,
                                isMipmapped = mipmapped
                            });
                        }
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
                            textureStatData.Add(new TextureStatData
                            {
                                type = "texture3D",
                                width = texture3D.width,
                                height = texture3D.height,
                                format = GetTextureFormat(texture3D.format),
                                name = texture3D.name,
                                profiler_mem = profiler_mem,
                                _calc_mem = _calc_mem,
                                materialName = materialName,
                                isMipmapped = mipmapped
                            });
                        }
                        break;
                    }
                default:
                    {
                        if (ShouldLog)
                        {
                            textureStatData.Add(new TextureStatData
                            {
                                type = texture.GetType().ToString(),
                                width = texture.width,
                                height = texture.height,
                                format = dim.ToString(),
                                name = texture.name,
                                profiler_mem = profiler_mem,
                                _calc_mem = 0,
                                materialName = materialName
                            });
                        }
                        break;
                    }
            }
            if (readWriteEnabled)
            {
                vram += (ulong)(_calc_mem >> 1);
            }
            else
            {
                vram += (ulong)_calc_mem;
            }
        }
        private static string GetTextureFormat(TextureFormat format)
        {
            return format.ToString();
        }
        public static string GetTexturePropertyNameIDCache() 
        {
            var values = TexturePropertyNameIDs.Values.ToArray();
            staticSB.Clear();
            staticSB.Append(values.Length);
            staticSB.AppendLine(" elements in TexturePropertyNameID cache.");
            for (int i = 0; i < values.Length; i++)
            {
                staticSB.AppendLine(values[i]);                
            }
            return staticSB.ToString();
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
            public bool isMipmapped;
            public string materialName;
            internal bool isStreaming;
        }
        private string AddTextureInfoToLog(long textureMem, long calc_mem, ulong vram, long ElapsedTicks) 
        {
            commonSb.Clear();
            commonSb.Append("Textures use ");
            GetBytesReadable(vram, commonSb);
            commonSb.Append(" VRAM. (");
            GetBytesReadable((ulong)textureMem, commonSb);
            commonSb.Append(" RAM + VRAM, calculated max: ");
            GetBytesReadable((ulong)calc_mem, commonSb);
            commonSb.AppendLine(")");
            commonSb.Append("Analysis took ");
            commonSb.Append((ElapsedTicks * nanosecPerTick / 1000000f).ToString(CultureInfo.InvariantCulture));
            commonSb.Append(" ms (");
            commonSb.Append(ElapsedTicks);
            commonSb.AppendLine(" ticks)");
            var _textureStatData = textureStatData.OrderByDescending(a => a._calc_mem).ToList();
            for (int i = 0; i < _textureStatData.Count; i++)
            {
                var texture = _textureStatData[i];
                commonSb.Append(texture.type.PadRight(16, ' '));
                commonSb.Append(" size: ");
                Uint5digitToStringBuilder((uint)texture.width, commonSb, true);
                commonSb.Append("×");
                Uint5digitToStringBuilder((uint)texture.height, commonSb, false, true);
                commonSb.Append("(");
                if (texture.isReadable)
                {
                    commonSb.Append("R");
                }
                else
                {
                    commonSb.Append("-");
                }
                if (texture.isMipmapped)
                {
                    commonSb.Append("M");
                }
                else
                {
                    commonSb.Append("-");
                }
                if (texture.isStreaming)
                {
                    commonSb.Append("S");
                }
                else
                {
                    commonSb.Append("-");
                }
                commonSb.Append(")");
                commonSb.Append(" Format: ");
                commonSb.Append(texture.format.PadRight(16, ' '));               
                if (texture.profiler_mem > 0 || texture._calc_mem > 0)
                {
                    commonSb.Append(" using: ");
                    if (texture.profiler_mem > 0)
                    {
                        commonSb.Append(GetBytesReadable(texture.profiler_mem).PadLeft(8));
                        commonSb.Append(" (profiled)");
                        if (texture._calc_mem > 0)
                        {
                            commonSb.Append(" | ");
                        }
                    }
                    else 
                    {
                        commonSb.Append("(can't be profiled) | ");
                    }
                    if (texture._calc_mem > 0)
                    {
                        commonSb.Append(GetBytesReadable(texture._calc_mem).PadLeft(8));
                        commonSb.Append(" (calc)");
                    }
                }
                commonSb.Append(", name: ");
                commonSb.Append(texture.name);
                commonSb.Append(", material name(s): ");
                commonSb.Append(texture.materialName);
                commonSb.AppendLine();
            }
            return commonSb.ToString();
        }
        private long CalculateMaxRTMemUse(RenderTextureFormat format, int width, int height, bool mipmapped, long _default, int antiAlias, int depth)
        {
            long _calc_mem;
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
                case RenderTextureFormat.ARGB1555:          // 16 bit
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
                case RenderTextureFormat.RInt:              // 32 bit
                case RenderTextureFormat.RGB111110Float:    // 32 bit
                case RenderTextureFormat.RG32:              // 32 bit
                case RenderTextureFormat.Default:           // ARGB32
                case RenderTextureFormat.ARGB2101010:       // 32 bit
                    {
                        _calc_mem = (long)(width * height * 4f);
                        break;
                    }
                case RenderTextureFormat.ARGBHalf:
                case RenderTextureFormat.DefaultHDR:        // ARGBHalf
                case RenderTextureFormat.ARGB64:
                case RenderTextureFormat.RGBAUShort:
                case RenderTextureFormat.RGFloat:           // 8B/px
                case RenderTextureFormat.RGInt:             // 64 bit
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
                case RenderTextureFormat.Shadowmap:         // ?
                case RenderTextureFormat.BGRA10101010_XR:   // 40 bit?
                case RenderTextureFormat.BGR101010_XR:      // 30 bit?
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
            _calc_mem = (long)(_calc_mem * _AA * (mipmapped ? FourThirds : 1f));
            switch (depth)
            {
                case 16:
                    {
                        _calc_mem += width * height * antiAlias * 2;
                        break;
                    }
                case 24:
                case 32:
                    {
                        _calc_mem += width * height * antiAlias * 4;
                        break;
                    }
            }
            return _calc_mem;
        }
        private long CalculateMaxMemUse(TextureFormat format, int width, int height, bool mipmapped, bool readWriteEnabled)
        {
            long _calc_mem = 0;
            switch (format)
            {
                case TextureFormat.PVRTC_RGBA2:
                case TextureFormat.PVRTC_RGB2:      // 2 bit/pixel
                    {
                        _calc_mem = (long)(width * height / 4f);

                        break;
                    }
                case TextureFormat.BC4:
                case TextureFormat.DXT1:
                case TextureFormat.DXT1Crunched:
                case TextureFormat.EAC_R:
                case TextureFormat.EAC_R_SIGNED:
                case TextureFormat.ETC_RGB4:
                case TextureFormat.ETC_RGB4Crunched:
                case TextureFormat.ETC_RGB4_3DS:      // obsolete but doesn't throw error in 2019.4

                case TextureFormat.ETC2_RGB:
                case TextureFormat.ETC2_RGBA1:        // tested in editor to be the same as ETC2_RGB
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
                case TextureFormat.ETC_RGBA8_3DS:       // obsolete but doesn't throw error in 2019.4
                case TextureFormat.ETC2_RGBA8Crunched:  // 8 bit/pixel
                    {
                        _calc_mem = width * height;
                        break;
                    }                
                case TextureFormat.ARGB4444: 
                case TextureFormat.R16:
                case TextureFormat.RG16:
                case TextureFormat.RHalf:
                case TextureFormat.RGB565:
                case TextureFormat.RGBA4444:        // 2 Bytes/pixel
                    {
                        _calc_mem = (long)(width * height * 2f);
                        break;
                    }
                case TextureFormat.RGB24:
                case TextureFormat.ARGB32: 
                case TextureFormat.RGBA32:
                case TextureFormat.BGRA32:
                case TextureFormat.RG32:
                case TextureFormat.RGB9e5Float:
                case TextureFormat.RGHalf:
                case TextureFormat.RFloat:          // 4 Bytes/pixel
                    {
                        _calc_mem = (long)(width * height * 4f);
                        break;
                    }
                case TextureFormat.RGBAHalf:
                case TextureFormat.RGB48:
                case TextureFormat.RGBA64:
                case TextureFormat.RGFloat:         // 8 Bytes/pixel
                    {
                        _calc_mem = (long)(width * height * 8f);
                        break;
                    }

                case TextureFormat.RGBAFloat:       // 16 Bytes/pixel
                    {
                        _calc_mem = (long)(width * height * 16f);
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
                case TextureFormat.YUY2: 
                    // "Currently, this texture format is only useful for native code plugins
                    // as there is no support for texture importing or pixel access for this format.
                    // YUY2 is implemented for Direct3D 9, Direct3D 11, and Xbox One."
                default:
                    {
                        _calc_mem = 0;
                        break;
                    }
            }
            return (long)(_calc_mem * (readWriteEnabled ? 2L : 1L) * (mipmapped ? FourThirds : 1f));
        }
    }
}
