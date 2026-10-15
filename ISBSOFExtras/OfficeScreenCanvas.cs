using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ISBSSOFExtra
{
    internal sealed class OfficeScreenCanvas : IDisposable
    {
        internal static long Revision;
        private static readonly Dictionary<Texture, long> Frames = new Dictionary<Texture, long>();
        internal static long FrameRevision(Texture texture) => texture != null && Frames.TryGetValue(texture, out long value) ? value : 0;
        internal static RenderTexture Popup;
        internal static RenderTexture PowerPopup;
        internal RenderTexture PowerFrame { get; private set; }
        internal RenderTexture TerminalFrame { get; private set; }

        private readonly RenderTexture _powerPopup;
        private string _lastPower = "", _lastTerminal = "";
        internal RenderTexture Frame { get; private set; }

        private readonly Font _font;
        private readonly Texture2D _desktop;
        private readonly RenderTexture _popup;
        private string _last = "";
        internal OfficeScreenCanvas(bool loadDesktop = true)
        {
            try
            {
                Frame = DecontaminationScreenFrame.CreateTexture(1280, 648, "ISB_FacilityDisplay");
                PowerFrame = DecontaminationScreenFrame.CreateTexture(1280, 648, "ISB_PowerBackground");
                _popup = DecontaminationScreenFrame.CreateTexture(1280, 648, "ISB_OfficeCodeWindow");
                _powerPopup = DecontaminationScreenFrame.CreateTexture(1280, 648, "ISB_PowerWindow");
                TerminalFrame = DecontaminationScreenFrame.CreateTexture(1280, 648, "ISB_OfficeTerminalScreen");
                _font = Font.CreateDynamicFontFromOSFont("Consolas", 48);
                if (loadDesktop)
                    _desktop = LoadImage("cassie.png");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private static Texture2D LoadImage(string name)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(OfficeScreenCanvas).Assembly.Location), "Office", name);
            try
            {
                if (!ImageConversion.LoadImage(texture, System.IO.File.ReadAllBytes(path)))
                    throw new InvalidOperationException(name);
                return texture;
            }
            catch
            {
                Object.Destroy(texture);
                throw;
            }
        }

        internal void ShowStartup(Texture video, bool videoFinished, float progress)
        {
            _lastTerminal = "";
            Paint(TerminalFrame, Color.black, () =>
            {
                if (!videoFinished)
                {
                    if (video != null)
                        Graphics.DrawTexture(new Rect(0, 0, 1280, 648), video);
                    return;
                }

                Window(240, 210, 800, 190, Locale("StartupTitle", "C.A.S.S.I.E. / SYSTEM STARTUP"));
                Text(Locale("Loading", "> LOADING ") + (int)(progress * 100) + "%", 280, 298, 34, Color.white);
                Text("[" + new string ('#', (int)(progress * 24)) + new string ('-', 24 - (int)(progress * 24)) + "]", 280, 365, 34, new Color(.5f, 1f, .72f));
            });
        }

        internal void ShowStandby()
        {
            if (_lastTerminal == "standby")
                return;
            _lastTerminal = "standby";
            Paint(TerminalFrame, Color.black, () =>
            {
                Text("TERRAGROUP LABS", 54, 120, 36, new Color(.3f, .5f, .4f));
                Text(Locale("Standby", "STANDBY / AUTHENTICATION REQUIRED"), 54, 178, 24, Color.gray);
                Text("C:\\CASSIE> _", 54, 578, 26, new Color(.3f, .5f, .4f));
            });
        }

        internal void ShowDesktop()
        {
            if (_lastTerminal == "desktop")
                return;
            _lastTerminal = "desktop";
            Paint(TerminalFrame, Color.black, () => Graphics.DrawTexture(new Rect(0, 0, 1280, 648), _desktop));
        }

        internal static bool IsCodeScreen(Renderer renderer)
        {
            if (renderer == null || !renderer.gameObject.scene.name.StartsWith("Laboratory_Office_Abov", StringComparison.OrdinalIgnoreCase))
                return false;
            var t = renderer.transform;
            while (t != null)
            {
                if (t.name == "Lab_Recreation_LED" && t.parent != null && t.parent.name.TrimStart('_') == "GRAFT_LIGHT_emissive")
                    return true;
                t = t.parent;
            }

            return false;
        }

        internal void ShowCode(string code)
        {
            if (Popup != _popup)
                Revision++;
            Popup = _popup;
            if (_last == code)
                return;
            _last = code;
            Paint(_popup, Color.clear, () =>
            {
                Window(764, 28, 484, 153, Locale("AccessTitle", "C.A.S.S.I.E. / ACCESS"));
                Text(Locale("EmergencyPassword", "> DECONTAMINATION EMERGENCY CODE"), 784, 101, 15, new Color(.65f, .83f, .87f));
                CenteredText(code, 158, 43, new Color(.55f, 1f, .76f), 764, 484);
                Window(764, 204, 484, 153, Locale("AccessTitle", "C.A.S.S.I.E. / ACCESS"));
                Text(Locale("AccessCode", "> ACCESS CODE"), 784, 277, 20, new Color(1f, .2f, .15f));
                CenteredText(Locale("UnknownCode", "[UNKNOWN]"), 334, 34, new Color(1f, .2f, .15f), 764, 484);
            });
            Paint(Frame, Color.black, () => Graphics.DrawTexture(new Rect(0, 0, 1280, 648), _popup));
        }

        internal void ShowDecontamination(double elapsed, bool paused)
        {
            int remaining = Math.Max(0, (int)Math.Ceiling(DecontaminationVideoTimeline.Duration - elapsed));
            string key = "decon:" + remaining + ":" + paused;
            if (_last == key)
                return;
            _last = key;
            Paint(Frame, new Color(.018f, .025f, .021f), () =>
            {
                CenteredText(Locale("DeconScreenTitle", "C.A.S.S.I.E. / DECONTAMINATION"), 116, 45, new Color(.55f, 1f, .76f));
                CenteredText(Locale(paused ? "DeconScreenPaused" : remaining == 0 ? "DeconScreenActive" : "DeconScreenCountdown", paused ? "DECONTAMINATION PAUSED" : remaining == 0 ? "DECONTAMINATION IN PROGRESS" : "TIME UNTIL DECONTAMINATION"), 212, 38, Color.white);
                CenteredText((remaining / 60).ToString("00") + ":" + (remaining % 60).ToString("00"), 425, 138, new Color(1f, .21f, .1f));
                CenteredText(Locale("EvacuateNow", "EVACUATE IMMEDIATELY"), 542, 42, new Color(1f, .21f, .1f));
            });
        }

        internal void ShowDestruction(Texture image, double elapsed, double countdownAt, double seconds)
        {
            if (Popup != null)
                Revision++;
            Popup = null;
            bool blink = elapsed >= countdownAt - 1d && elapsed < countdownAt && ((int)((elapsed - countdownAt + 1d) * 4d) & 1) == 1;
            int remaining = Math.Max(0, (int)Math.Ceiling(countdownAt + seconds - elapsed));
            string key = elapsed < countdownAt ? (blink ? "blink" : "image") : "timer" + remaining + ":" + ((int)(elapsed * 2d) & 1);
            if (_last == key)
                return;
            _last = key;
            Paint(Frame, Color.black, () =>
            {
                if (elapsed < countdownAt)
                {
                    if (!blink && image != null)
                        Graphics.DrawTexture(new Rect(64, 0, 1152, 648), image);
                    return;
                }

                CenteredText(Locale("DestructionTitle", "C.A.S.S.I.E. / DEAD MAN'S SWITCH"), 124, 48, new Color(1f, .12f, .07f));
                CenteredText(Locale("SelfDestruction", "FACILITY SELF-DESTRUCTION"), 206, 43, Color.white);
                CenteredText((remaining / 60).ToString("00") + ":" + (remaining % 60).ToString("00"), 434, 150, new Color(1f, .13f, .04f));
                if (((int)(elapsed * 2d) & 1) == 0)
                    CenteredText(Locale("EvacuateNow", "EVACUATE IMMEDIATELY"), 553, 44, new Color(1f, .05f, .025f));
            });
        }

        internal static bool IsTerminalOverlay(Renderer renderer)
        {
            return renderer != null && renderer.name == "lab_monitor (5)" && renderer.gameObject.scene.name.StartsWith("Laboratory_Office_Abov", StringComparison.OrdinalIgnoreCase) && renderer.transform.parent != null && renderer.transform.parent.name.TrimStart('_') == "GRAFT_LIGHT_emissive" && (renderer.transform.position - new Vector3(-266.1131f, 5.011f, -375.7813f)).sqrMagnitude < .25f;
        }

        internal static bool IsTerminalScreen(Renderer renderer)
        {
            if (renderer == null || !renderer.gameObject.scene.name.StartsWith("Laboratory_Office_Abov", StringComparison.OrdinalIgnoreCase) || !renderer.name.Equals("pc_laptop_d_LOD0", StringComparison.OrdinalIgnoreCase))
                return false;
            for (var part = renderer.transform; part != null; part = part.parent)
                if (part.name == "Office_table_set_05 (2)" && Math.Abs(part.position.x + 266.5467f) < 1f && Math.Abs(part.position.y - 4.936f) < 1f)
                    return true;
            return false;
        }

        internal void ShowTerminal(string status, string controls, bool allowed)
        {
            string key = status + controls + allowed;
            if (_lastTerminal == key)
                return;
            _lastTerminal = key;
            Paint(TerminalFrame, new Color(.01f, .018f, .014f), () =>
            {
                var green = new Color(.6f, 1f, .78f);
                Box(0, 0, 1280, 42, new Color(.13f, .17f, .19f));
                Text(Locale("TerminalTitle", "C.A.S.S.I.E. / EMERGENCY TERMINAL"), 28, 30, 22, Color.white);
                Text("TERRAGROUP LABS", 36, 88, 28, green);
                int y = 132;
                foreach (var line in status.Split('\n'))
                {
                    Text(line, 36, y, 24, Color.white);
                    y += 34;
                }

                Text(Locale(allowed ? "DeconAllowed" : "DeconDenied", allowed ? "DECONTAMINATION ACCESS: GRANTED" : "DECONTAMINATION ACCESS: DENIED"), 36, 218, 23, allowed ? green : new Color(1f, .35f, .3f));
                string[] keys =
                {
                    "Files",
                    "Logs",
                    "Control",
                    "PowerMenu",
                    "AlarmMenu",
                    "PistonMenu"
                };
                string[] fallback =
                {
                    "DOWNLOAD FILES",
                    "SYSTEM DATA",
                    "DECONTAMINATION CONTROL",
                    "FACILITY POWER / ENERGY CELLS",
                    "LIGHTS / ALARM",
                    "GATE PISTONS"
                };
                for (int i = 0; i < keys.Length; i++)
                    Text("[" + (i + 1) + "] " + Locale(keys[i], fallback[i]), 36, 266 + i * 36, 23, green);
                y = 272;
                foreach (var line in controls.Split('\n'))
                {
                    Text(line, 670, y, 18, Color.white);
                    y += 26;
                }

                Box(28, 496, 1224, 64, new Color(.15f, .015f, .01f));
                Text(Locale("DeadManWarning", "DEAD MAN'S SWITCH AUTOMATICALLY ARMED - OPERATE WITH CAUTION"), 44, 535, 21, new Color(1f, .3f, .25f));
                Text("C:\\CASSIE> _", 36, 613, 26, green);
            });
        }

        internal void ShowPower(double now, double begin, double end, List<FacilityRemoteOperation> operations)
        {
            var active = new List<FacilityRemoteOperation>(FacilitySequenceState.RemoteConcurrentLimit);
            if (operations != null)
                foreach (var operation in operations)
                    if (operation != null && now >= operation.finishAt - FacilitySequenceState.RemoteDurationSeconds && now < operation.finishAt && active.Count < FacilitySequenceState.RemoteConcurrentLimit)
                        active.Add(operation);
            bool restart = begin >= 0d && end >= 0d && now >= begin && now < end;
            if (active.Count == 0 && !restart)
            {
                if (PowerPopup != null) Revision++;
                PowerPopup = null;
                _lastPower = "";
                return;
            }
            if (PowerPopup != _powerPopup) Revision++;
            PowerPopup = _powerPopup;
            int restartRemaining = restart ? Math.Max(0, (int)Math.Ceiling(end - now)) : -1;
            string key = restartRemaining.ToString();
            foreach (var operation in active)
                key += "|" + operation.Action + ":" + Math.Max(0, (int)Math.Ceiling(operation.finishAt - now));
            if (_lastPower == key) return;
            _lastPower = key;
            Paint(_powerPopup, Color.clear, () =>
            {
                int row = 0;
                if (restart) DrawPowerTimer("", restartRemaining, 24 + row++ * 150);
                foreach (var operation in active)
                    DrawPowerTimer(operation.cell, Math.Max(0, (int)Math.Ceiling(operation.finishAt - now)), 24 + row++ * 150);
            });
        }

        private void DrawPowerTimer(string cell, int remaining, int y)
        {
            Window(30, y, 472, 140, Locale("PowerTitle", "C.A.S.S.I.E. / POWER CONTROL"));
            Text(cell == "" ? Locale("RestartLabel", "> FACILITY RESTART") : (cell.StartsWith("P:", StringComparison.Ordinal) ? Locale("RemotePistonLabel", "> REMOTE PISTON ") + cell.Substring(2) : Locale("RemoteCellLabel", "> REMOTE CELL ") + cell), 50, y + 65, 23, Color.white);
            Text((remaining / 60).ToString("00") + ":" + (remaining % 60).ToString("00"), 183, y + 120, 46, new Color(.55f, 1f, .76f));
        }

        private static string Locale(string key, string fallback) => ISB.Compatibility.ISBLocalizedText.Get("ISB/Office/" + key, fallback);
        private void Window(int x, int y, int width, int height, string title)
        {
            Box(x, y, width, height, new Color(.25f, .3f, .27f));
            Box(x + 2, y + 2, width - 4, height - 4, new Color(.008f, .014f, .01f, .99f));
            Box(x + 2, y + 2, width - 4, 32, new Color(.05f, .07f, .06f));
            Text(title, x + 10, y + 24, 17, Color.white);
            Text("_ [] [X]", x + width - 86, y + 24, 17, Color.white);
        }

        private static void Paint(RenderTexture target, Color clear, Action draw)
        {
            Frames[target] = ++Revision;
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                RenderTexture.active = target;
                GL.sRGBWrite = target.sRGB;
                GL.Clear(false, true, clear);
                GL.PushMatrix();
                try
                {
                    GL.LoadPixelMatrix(0, 1280, 648, 0);
                    draw();
                }
                finally
                {
                    GL.PopMatrix();
                }
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = srgb;
            }
        }

        private static void Box(float x, float y, float w, float h, Color color) => Graphics.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture, new Rect(0, 0, 1, 1), 0, 0, 0, 0, color);
        private void CenteredText(string text, float baseline, int size, Color color, float left = 0f, float availableWidth = 1280f)
        {
            _font.RequestCharactersInTexture(text, size, FontStyle.Bold);
            float width = 0;
            foreach (char c in text)
                if (_font.GetCharacterInfo(c, out var glyph, size, FontStyle.Bold))
                    width += glyph.advance;
            Text(text, left + (availableWidth - width) * .5f, baseline, size, color);
        }

        private void Text(string text, float x, float baseline, int size, Color color) => DrawText(_font, FontStyle.Bold, text, x, baseline, size, color);
        private static void DrawText(Font font, FontStyle style, string text, float x, float baseline, int size, Color color)
        {
            font.RequestCharactersInTexture(text, size, style);
            font.material.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(color);
            foreach (char c in text)
            {
                if (!font.GetCharacterInfo(c, out var info, size, style))
                    continue;
                GL.TexCoord(info.uvTopLeft);
                GL.Vertex3(x + info.minX, baseline - info.maxY, 0);
                GL.TexCoord(info.uvTopRight);
                GL.Vertex3(x + info.maxX, baseline - info.maxY, 0);
                GL.TexCoord(info.uvBottomRight);
                GL.Vertex3(x + info.maxX, baseline - info.minY, 0);
                GL.TexCoord(info.uvBottomLeft);
                GL.Vertex3(x + info.minX, baseline - info.minY, 0);
                x += info.advance;
            }

            GL.End();
        }

        private static void RemoveFrame(Texture texture)
        {
            if (texture != null)
                Frames.Remove(texture);
        }

        private bool _disposed;
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Revision++;
            RemoveFrame(PowerFrame);
            DecontaminationScreenFrame.Release(PowerFrame);
            PowerFrame = null;
            RemoveFrame(Frame);
            RemoveFrame(_popup);
            RemoveFrame(_powerPopup);
            RemoveFrame(TerminalFrame);
            if (Popup == _popup)
                Popup = null;
            if (PowerPopup == _powerPopup)
                PowerPopup = null;
            DecontaminationScreenFrame.Release(_powerPopup);
            DecontaminationScreenFrame.Release(TerminalFrame);
            DecontaminationScreenFrame.Release(Frame);
            DecontaminationScreenFrame.Release(_popup);
            Object.Destroy(_font);
            Object.Destroy(_desktop);
            Frame = null;
            TerminalFrame = null;
        }
    }
}
