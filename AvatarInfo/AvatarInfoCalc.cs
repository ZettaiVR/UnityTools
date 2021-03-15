using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using System.Reflection;

namespace Zettai
{
    public static class AvatarInfoCalc
    {
        
        private const float FourThird = (4f / 3f);
        static List<AudioSource> audioSources;
        static List<AudioClip> audioClips;
        static readonly FieldInfo fi = typeof(DynamicBone).GetField("m_Particles", BindingFlags.NonPublic | BindingFlags.Instance);

        public static void CheckCloth(GameObject ava, ref AvatarInfo _AvatarInfo) 
        {
            var cloths = ava.GetComponentsInChildren<Cloth>();
            foreach (Cloth cloth in cloths)
            {
                uint count = (uint)cloth.vertices.Length;
                _AvatarInfo.clothVertCount += count;
                _AvatarInfo.clothDiff += (uint)(count * cloth.clothSolverFrequency);
                _AvatarInfo.clothNumber++;
            }
        }
        public static void CheckDB(GameObject ava, ref AvatarInfo _AvatarInfo) 
        {
            uint dbCount = 0;
            uint collCount = 0;
            var dbs = ava.GetComponentsInChildren<DynamicBone>();
            foreach (DynamicBone db in dbs)
            {
                try
                {
                    int count = (fi.GetValue(db) as IList).Count;
                    dbCount += (uint)count;
                    collCount += (uint)(Math.Max(count - 1, 0) * db.m_Colliders.Count);
                }
                catch 
                { 
                    //eh
                }
            }
            _AvatarInfo.dbCount = dbCount;
            _AvatarInfo.dbCollCount = collCount;

        }
        public static void CheckRenderers(GameObject ava, ref AvatarInfo _AvatarInfo) 
        {
            var renderers = ava.GetComponentsInChildren<Renderer>();

            foreach (var item in renderers)
            {
                _AvatarInfo.materialCount += (uint)item.sharedMaterials.Length;
                if (item.GetType() == typeof(MeshRenderer)) 
                {
                    _AvatarInfo.triangles += CountMeshRendererTris((MeshRenderer)item);
                    continue;
                }
                if (item.GetType() == typeof(SkinnedMeshRenderer))
                {
                    _AvatarInfo.triangles += CountSkinnedMeshRendererTris((SkinnedMeshRenderer)item);
                    continue;
                }
                if (item.GetType() == typeof(TrailRenderer))
                {
                    _AvatarInfo.triangles += CountTrailRendererTris((TrailRenderer)item);
                    continue;
                }
                if (item.GetType() == typeof(LineRenderer))
                {
                    _AvatarInfo.triangles += CountLineRendererTris((LineRenderer)item);
                    continue;
                }
                //if (item.GetType() == typeof(ParticleSystemRenderer))  // particle systems are too different
                //{
                //    continue;
                //}
            }

        }
        private static uint CountMesh(Mesh mesh) 
        {
            uint counter = 0;
            if (mesh)
            {
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    var submesh = mesh.GetSubMesh(i);
                    switch (submesh.topology)
                    {
                        case MeshTopology.Lines:
                        case MeshTopology.LineStrip:
                        case MeshTopology.Points:
                            //wtf
                            { continue; }
                        case MeshTopology.Quads:
                            {
                                counter += (uint)(submesh.indexCount / 4);
                                continue;
                            }
                        case MeshTopology.Triangles:
                            {
                                counter += (uint)(submesh.indexCount / 3);
                                continue;
                            }
                    }

                }
            }
            return counter;

        }
        private static uint CountSkinnedMeshRendererTris(SkinnedMeshRenderer renderer)
        {
            if (renderer.sharedMesh) 
            {
                return CountMesh(renderer.sharedMesh);
            }
            return 0;
        }
        private static uint CountMeshRendererTris(MeshRenderer renderer)
        {
            MeshFilter meshFilter = renderer.gameObject.GetComponent<MeshFilter>();
            if (meshFilter)
            {
                return CountMesh(meshFilter.sharedMesh);
            }
            return 0;
        }
        private static uint CountTrailRendererTris(TrailRenderer renderer)
        {
            if (renderer) 
            {
                return (uint)(renderer.time * 100);
            }
            return 0;
        }
        private static uint CountLineRendererTris(LineRenderer renderer)
        {
            if (renderer)
                return (uint)renderer.positionCount * 2; // idk..
            return 0;
        }
        public static void CheckAudio(GameObject ava, ref AvatarInfo _AvatarInfo)
        {
            audioClips = new List<AudioClip>();
            audioSources = new List<AudioSource>();
            ava.GetComponentsInChildren(true, audioSources);
            for (int i = 0; i < audioSources.Count; i++)
            {
                var item = audioSources[i];
                if (item != null && item.clip != null)
                    audioClips.Add(item.clip);
            }
            audioClips = audioClips.Distinct().ToList();
            double _totalSize = 0;
            string logMessage = "";
            float length = 0;
            _AvatarInfo.AudioClipCount = (uint)audioClips.Count;
            for (int i = 0; i < audioClips.Count; i++)
            {
                AudioClip a = audioClips[i];
                double _size = a.samples * a.channels * 2f; // 16 bit samples
                _totalSize += _size;
                length += a.length;
                logMessage += (a.name + ": estimated size: " + Math.Round(_size / 1024 / 1024, 2) + " MB, loadInBackground: " + a.loadInBackground + ", loadType: " + a.loadType + ", length: " + a.length + ", channels: " + a.channels + ", frequency: " + a.frequency + Environment.NewLine);
            }
            _AvatarInfo.AudioClipLength = (uint)length;
            double AudioClipSizeMB = (Math.Round(_totalSize / 1024f / 1024f * 100f) / 100f);
            _AvatarInfo.AudioClipSizeMB = (Math.Round(_totalSize / 1024f / 1024f * 100f) / 100f);
            logMessage = audioClips.Count + " audio clips" + ((audioClips.Count > 0) ? (" with " + AudioClipSizeMB + " MB estimated size.") : ".") + Environment.NewLine + logMessage;
            _AvatarInfo.AvatarInfoString += logMessage;
            Debug.Log(logMessage);
        }
        public static void CheckTextures(GameObject ava, ref AvatarInfo _AvatarInfo)
        {
            DateTime start = DateTime.Now;
            Texture _tempText;
            var textures = new List<Texture>();
            var renderers = ava.GetComponentsInChildren<Renderer>(true);
            foreach (var rend in renderers)
            {
                foreach (var material in rend.sharedMaterials)
                {
                    if (material)
                    {
                        List<string> outNames = new List<string>();
                        material.GetTexturePropertyNames(outNames);
                        foreach (var _name in outNames)
                        {
                            _tempText = material.GetTexture(_name);
                            if (_tempText != null) textures.Add(_tempText);
                        }
                    }
                }
            }
            textures = textures.Distinct().ToList();
            _AvatarInfo.textureMem = 0;
            long _mem;
            _AvatarInfo.calc_mem = 0;
            double _round;
            Texture2D texture2D;
            Cubemap cubemap;
            string log_output = "";
            foreach (var texture in textures)
            {
                _mem = Profiler.GetRuntimeMemorySizeLong(texture);
                _round = Math.Round(_mem / 1024 / 1024f * 100) / 100f;
                long _calc_mem;
                if (texture.GetType().Equals(typeof(Texture2D)))
                {
                    texture2D = (Texture2D)texture;

                    _AvatarInfo.calc_mem += _calc_mem = CalculateMaxMemUse(texture2D.format, texture2D.width, texture2D.height, texture2D.mipmapCount > 1, _mem);
                    _round = Math.Round(_mem / 1024 / 1024f * 100) / 100f;
                    _AvatarInfo.textureMem += _mem;
                    double _calc_round = Math.Round(_calc_mem / 1024 / 1024f * 100) / 100f;
                    string _log_output = "Texture2D object " + texture.name + " using: " + _mem + "Bytes (" + _round + " MB | " + _calc_round + " MB max), width: " + texture.width + ", height: " + texture.height + " Format: " + texture2D.format;
                    log_output += _log_output + Environment.NewLine;
                    //Debug.Log(_log_output);
                }
                else
                if (texture.GetType().Equals(typeof(Cubemap)))
                {
                    _AvatarInfo.textureMem += _mem;
                    cubemap = (Cubemap)texture;
                    _calc_mem = (CalculateMaxMemUse(cubemap.format, cubemap.width, cubemap.height, cubemap.mipmapCount > 1, _mem) * 6);
                    _AvatarInfo.calc_mem += _calc_mem;

                    double _calc_round = Math.Round(_calc_mem / 1024 / 1024f * 100) / 100f;
                    string _log_output = "Cubemap object " + texture.name + " using: " + _mem + "Bytes (" + _round + " MB| " + _calc_round + " MB max), width: " + texture.width + ", height: " + texture.height + " Format: " + cubemap.format;
                    log_output += _log_output + Environment.NewLine;
                    //Debug.Log(_log_output);
                }
                else
                if (texture.GetType().Equals(typeof(RenderTexture)))
                {
                    _AvatarInfo.textureMem += _mem;
                    RenderTexture rt = (RenderTexture)texture;
                    _calc_mem = CalculateMaxRTMemUse(rt.format, rt.width, rt.height, rt.useMipMap, _mem, rt.antiAliasing, rt.depth);
                    _AvatarInfo.calc_mem += _calc_mem;

                    double _calc_round = Math.Round(_calc_mem / 1024 / 1024f * 100) / 100f;
                    string _log_output = "RenderTexture object " + texture.name + " using: " + _mem + "Bytes (" + _round + " MB| " + _calc_round + " MB max), width: " + texture.width + ", height: " + texture.height + " Format: " + rt.format;
                    log_output += _log_output + Environment.NewLine;
                    //Debug.Log(_log_output);
                }
                else
                {
                    _AvatarInfo.textureMem += _mem;
                    string _log_output = texture.GetType() + " object " + texture.name + " using: " + _mem + "Bytes (" + _round + " MB), width: " + texture.width + ", height: " + texture.height;
                    log_output += _log_output + Environment.NewLine;
                    //Debug.Log(_log_output);
                }
            }
            var textureMemMB = Math.Round(_AvatarInfo.textureMem / 1024 / 1024f * 100) / 100f;
            var calc_memMB = Math.Round(_AvatarInfo.calc_mem / 1024 / 1024f * 100) / 100f;
            if (!_AvatarInfo) { _AvatarInfo = ava.GetComponent<AvatarInfo>(); }
            if (!_AvatarInfo) { _AvatarInfo = ava.AddComponent<AvatarInfo>(); }
            _AvatarInfo.textureMemMB = textureMemMB;
            _AvatarInfo.calc_memMB = calc_memMB;
            log_output = "Textures use " + textureMemMB + " MBytes VRAM, calculated max: " + calc_memMB + " MB." + Environment.NewLine + "Analysis took " + (DateTime.Now - start).TotalMilliseconds + " ms" + Environment.NewLine + log_output;
            _AvatarInfo.AvatarInfoString += log_output;
            Debug.Log(log_output);
        }
        private static long CalculateMaxRTMemUse(RenderTextureFormat format, int width, int height, bool mipmapped, long _default, int antiAlias, int depth)
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
        private static long CalculateMaxMemUse(TextureFormat format, int width, int height, bool mipmapped, long _default)
        {
            long _calc_mem;
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
                        _calc_mem = (long)(width * height / 2f * (mipmapped ? FourThird : 1f));

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
                        _calc_mem = (long)(width * height * (mipmapped ? FourThird : 1f));
                        break;
                    }
                // ASTC is using 128 bits to store n×n pixels
                case TextureFormat.ASTC_4x4:
                case TextureFormat.ASTC_HDR_4x4:
                case TextureFormat.ASTC_RGBA_4x4:
                    {
                        _calc_mem = (long)(width * height / (4 * 4) * 16f * (mipmapped ? FourThird : 1f));
                        break;
                    }
                case TextureFormat.ASTC_RGB_5x5:
                case TextureFormat.ASTC_RGBA_5x5:
                    // case TextureFormat.ASTC_HDR_5x5:
                    {
                        _calc_mem = (long)(width * height / (5 * 5) * 16f * (mipmapped ? FourThird : 1f));
                        break;
                    }
                case TextureFormat.ASTC_RGB_6x6:
                case TextureFormat.ASTC_HDR_6x6:
                case TextureFormat.ASTC_RGBA_6x6:
                    {
                        _calc_mem = (long)(width * height / (6 * 6) * 16f * (mipmapped ? FourThird : 1f));
                        break;
                    }
                case TextureFormat.ASTC_RGB_8x8:
                case TextureFormat.ASTC_HDR_8x8:
                case TextureFormat.ASTC_RGBA_8x8:
                    {
                        _calc_mem = (long)(width * height / (8 * 8) * 16f * (mipmapped ? FourThird : 1f));
                        break;
                    }
                case TextureFormat.ASTC_RGB_10x10:
                case TextureFormat.ASTC_RGBA_10x10:
                case TextureFormat.ASTC_HDR_10x10:
                    {
                        _calc_mem = (long)(width * height / (10 * 10) * 16f * (mipmapped ? FourThird : 1f));
                        break;
                    }
                case TextureFormat.ASTC_RGB_12x12:
                case TextureFormat.ASTC_HDR_12x12:
                case TextureFormat.ASTC_RGBA_12x12:
                    {
                        _calc_mem = (long)(width * height / (12 * 12) * 16f * (mipmapped ? FourThird : 1f));
                        break;
                    }
                case TextureFormat.ARGB4444: //2B/px
                case TextureFormat.R16:
                case TextureFormat.RG16:
                case TextureFormat.RHalf:
                case TextureFormat.RGB565:
                case TextureFormat.RGBA4444:
                    {
                        _calc_mem = (long)(width * height * 2f * (mipmapped ? FourThird : 1f));
                        break;
                    }

                // 3B/px
                //   {
                //       _calc_mem = (long)(width * height * 3f * (mipmapped ? FourThird : 1f));
                //       break;
                //   }
                case TextureFormat.RGB24:
                case TextureFormat.ARGB32: // 4B/px
                case TextureFormat.RGBA32:
                case TextureFormat.BGRA32:
                case TextureFormat.RGHalf:
                case TextureFormat.RFloat:
                    {
                        _calc_mem = (long)(width * height * 4f * (mipmapped ? FourThird : 1f));
                        break;
                    }
                case TextureFormat.RGBAHalf:
                case TextureFormat.RGFloat: //8B/px
                    {
                        _calc_mem = (long)(width * height * 8f * (mipmapped ? FourThird : 1f));
                        break;
                    }

                case TextureFormat.RGBAFloat: //16B/px               
                    {
                        _calc_mem = (long)(width * height * 16f * (mipmapped ? FourThird : 1f));
                        break;
                    }
                default:
                    {
                        _calc_mem = _default;
                        break;
                    }
            }
            return _calc_mem;
        }

    }

}
