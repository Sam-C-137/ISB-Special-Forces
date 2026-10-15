using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ISBSSOFExtra
{
    internal sealed class DecontaminationScreenFrame : IDisposable
    {
        internal readonly Texture Background;
        internal readonly Rect Area;
        internal readonly float Aspect;
        internal readonly bool Office;
        internal readonly bool Terminal;
        internal RenderTexture ColorTexture { get; private set; }
        internal RenderTexture EmissionTexture { get; private set; }

        private readonly Rect _videoArea;
        internal bool Visible;
        private long _drawRevision = -1;
        internal DecontaminationScreenFrame(Texture background, Rect area, float aspect, bool office = false, bool terminal = false)
        {
            Office = office;
            Terminal = terminal;
            Background = background;
            Area = area;
            Aspect = aspect;
            _videoArea = DecontaminationScreenRules.FitVideo(area, aspect);
            try
            {
                int width = background != null ? background.width : Mathf.Min(1280, Mathf.CeilToInt(720f * aspect));
                int height = background != null ? background.height : Mathf.Min(720, Mathf.CeilToInt(1280f / aspect));
                int limit = Mathf.Min(2048, SystemInfo.maxTextureSize);
                float resize = Mathf.Min(1f, limit / (float)Mathf.Max(width, height));
                width = Mathf.Max(1, Mathf.RoundToInt(width * resize));
                height = Mathf.Max(1, Mathf.RoundToInt(height * resize));
                ColorTexture = CreateTexture(width, height, "ISB_MonitorColor");
                EmissionTexture = background != null ? CreateTexture(width, height, "ISB_MonitorEmission") : ColorTexture;
                if (background != null)
                {
                    RenderTexture previous = RenderTexture.active;
                    bool previousSrgb = GL.sRGBWrite;
                    try
                    {
                        GL.sRGBWrite = ColorTexture.sRGB;
                        Graphics.Blit(background, ColorTexture);
                    }
                    finally
                    {
                        GL.sRGBWrite = previousSrgb;
                        RenderTexture.active = previous;
                    }

                    DrawInto(ColorTexture, Texture2D.blackTexture, Area);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal void DrawVisible(Texture video, long revision)
        {
            if (!Visible || _drawRevision == revision)
                return;
            Draw(video);
            _drawRevision = revision;
        }

        internal void Draw(Texture video)
        {
            DrawInto(EmissionTexture, video, _videoArea);
            if (Office && OfficeScreenCanvas.Popup != null)
                DrawInto(EmissionTexture, OfficeScreenCanvas.Popup, _videoArea);
            if (OfficeScreenCanvas.PowerPopup != null)
                DrawInto(EmissionTexture, OfficeScreenCanvas.PowerPopup, _videoArea);
        }

        private static void DrawInto(RenderTexture target, Texture video, Rect area)
        {
            RenderTexture previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            try
            {
                RenderTexture.active = target;
                GL.sRGBWrite = target.sRGB;
                GL.PushMatrix();
                try
                {
                    GL.LoadPixelMatrix(0f, target.width, target.height, 0f);
                    Graphics.DrawTexture(new Rect(area.x * target.width, (1f - area.yMax) * target.height, area.width * target.width, area.height * target.height), video);
                }
                finally
                {
                    GL.PopMatrix();
                }
            }
            finally
            {
                GL.sRGBWrite = previousSrgb;
                RenderTexture.active = previous;
            }
        }

        internal static RenderTexture CreateTexture(int width, int height, string name)
        {
            var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            if (!texture.Create())
            {
                Object.Destroy(texture);
                throw new InvalidOperationException("Screen render texture could not be created.");
            }

            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = texture;
                GL.Clear(false, true, Color.black);
            }
            finally
            {
                RenderTexture.active = previous;
            }

            return texture;
        }

        internal static void Release(RenderTexture texture)
        {
            if (texture == null)
                return;
            texture.Release();
            Object.Destroy(texture);
        }

        public void Dispose()
        {
            if (EmissionTexture != ColorTexture)
                Release(EmissionTexture);
            Release(ColorTexture);
            ColorTexture = null;
            EmissionTexture = null;
        }
    }
}
