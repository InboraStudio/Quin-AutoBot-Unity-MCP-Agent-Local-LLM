using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace QuinAutobot
{
    public static class SceneCapture
    {
        public static string CaptureSceneViewBase64()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null || sv.camera == null)
            {
                Debug.LogWarning("[QuinAutobot] No active SceneView to capture.");
                return null;
            }

            int w = Mathf.Max(64, (int)sv.position.width);
            int h = Mathf.Max(64, (int)sv.position.height);

            var rt      = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var prevRT  = sv.camera.targetTexture;
            var prevAct = RenderTexture.active;

            sv.camera.targetTexture = rt;
            sv.camera.Render();
            sv.camera.targetTexture = prevRT;

            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prevAct;

            byte[] png    = tex.EncodeToPNG();
            string base64 = Convert.ToBase64String(png);

            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);

            return base64;
        }

        public static string LoadImageAsBase64(string absolutePath)
        {
            if (!File.Exists(absolutePath)) return null;
            byte[] bytes = File.ReadAllBytes(absolutePath);
            return Convert.ToBase64String(bytes);
        }

        public static string GetMimeType(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp"           => "image/webp",
                _                 => "image/png",
            };
        }
    }
}
