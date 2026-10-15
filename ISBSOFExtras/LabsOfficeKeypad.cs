using System.Collections.Generic;
using UnityEngine;

namespace ISBSSOFExtra
{
    internal sealed partial class LabsOfficeScene
    {
        private void KeypadGui()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            var e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Escape)
                {
                    CloseKeypad();
                    e.Use();
                    return;
                }

                if (_pending)
                {
                    e.Use();
                    return;
                }

                if (e.keyCode == KeyCode.Backspace && _keypadEntry.Length > 0)
                {
                    _keypadEntry = _keypadEntry.Substring(0, _keypadEntry.Length - 1);
                    PlayKeypadSound("keypress");
                    e.Use();
                }
                else if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
                {
                    PlayKeypadSound("keypress");
                    SubmitKeypad();
                    e.Use();
                }
                else if (e.character >= '0' && e.character <= '9' && _keypadEntry.Length < 4 && Time.unscaledTime >= _keypadResultUntil)
                {
                    _keypadEntry += e.character;
                    PlayKeypadSound("keypress");
                    e.Use();
                }
            }

            var s = Screen.height / 1080f;
            const float panelW = 224f;
            const float panelH = 229f;
            var px = (Screen.width - panelW * s) / 2f;
            var py = Screen.height - 264.5f * s;
            var panelCx = px + panelW * s / 2f;
            var showResult = Time.unscaledTime < _keypadResultUntil;
            EnsureKeypadStyles();
            GUI.Label(new Rect(px - 80f * s, py - 32f * s, (panelW + 160f) * s, 30f * s), _pending ? Text("Waiting", "Waiting for host...") : _state.stage == 0 ? Text("ConsoleCode", "Enter the console access code") : Text("EmergencyCode", "Enter the emergency code from the office monitor"), _keypadButtonStyle);
            DrawKeypadTex("Background_Shadow", new Rect(px - 15f * s, py - 15f * s, (panelW + 30f) * s, (panelH + 30f) * s), 1f);
            DrawKeypadTex("Background", new Rect(px, py, panelW * s, panelH * s), 1f);
            var subRect = new Rect(panelCx - 107f * s, py + 5f * s, 214f * s, 52f * s);
            DrawKeypadTex(showResult ? (_keypadGranted ? "Substrate_Green" : "Substrate_Red") : "Substrate_Gray", subRect, 1f);
            if (showResult)
            {
                var fade = Mathf.Clamp01((_keypadResultUntil - Time.unscaledTime) / 0.8f);
                var lightRect = new Rect(panelCx - 150f * s, subRect.y + subRect.height / 2f + (84f - 74f) * s, 300f * s, 148f * s);
                DrawKeypadTex(_keypadGranted ? "Light_Green" : "Light_Red", lightRect, fade);
                DrawKeypadTex(_keypadGranted ? "Flashing_Green" : "Flashing_Red", subRect, fade);
            }

            var holderLeft = panelCx - 110f * s;
            var holderTop = py + 2.5f * s;
            _keypadSlotStyle.fontSize = Mathf.RoundToInt(30f * s);
            for (var slot = 0; slot < 7; slot++)
            {
                var cx = holderLeft + (26f + 28f * slot) * s;
                var cy = holderTop + 28f * s;
                DrawKeypadTex("Number_Enter", new Rect(cx - 14f * s, cy - 14.5f * s, 28f * s, 39f * s), 1f);
                var typed = slot < _keypadEntry.Length;
                _keypadSlotStyle.normal.textColor = showResult ? (_keypadGranted ? new Color(0.65f, 1f, 0.65f) : new Color(1f, 0.5f, 0.5f)) : (typed ? new Color(0.9f, 0.94f, 1f) : new Color(0.3f, 0.33f, 0.37f));
                GUI.Label(new Rect(cx - 14f * s, cy - 25f * s, 28f * s, 50f * s), typed ? _keypadEntry[slot].ToString() : "0", _keypadSlotStyle);
            }

            var keys = new[]
            {
                "1",
                "2",
                "3",
                "4",
                "5",
                "6",
                "7",
                "8",
                "9",
                "X",
                "0",
                "E"
            };
            var gridLeft = panelCx - 70f * s;
            var gridTop = py + (114.5f + 26f - 65f) * s;
            _keypadButtonStyle.fontSize = Mathf.RoundToInt(17f * s);
            var mouse = Event.current.mousePosition;
            for (var i = 0; i < keys.Length; i++)
            {
                var col = i % 3;
                var row = i / 3;
                var bcx = gridLeft + (22f + 48f * col) * s;
                var bcy = gridTop + (17f + 32f * row) * s;
                var hitRect = new Rect(bcx - 20f * s, bcy - 14f * s, 40f * s, 28f * s);
                var visRect = new Rect(bcx - 21f * s, bcy - 15f * s, 42f * s, 30f * s);
                DrawKeypadTex(hitRect.Contains(mouse) ? "Button_Highlighted" : "Button_Normal", visRect, 1f);
                if (keys[i] == "X")
                {
                    DrawKeypadTex("Icon_Del_Static", new Rect(bcx - 7.5f * s, bcy - 8.5f * s, 15f * s, 17f * s), 1f);
                }
                else if (keys[i] == "E")
                {
                    DrawKeypadTex("Icon_Enter_Static", new Rect(bcx - 9.5f * s, bcy - 9.5f * s, 19f * s, 19f * s), 1f);
                }
                else
                {
                    GUI.Label(hitRect, keys[i], _keypadButtonStyle);
                }

                if (!GUI.Button(hitRect, GUIContent.none, GUIStyle.none))
                {
                    continue;
                }

                PlayKeypadSound("keypress");
                if (keys[i] == "X")
                {
                    if (_keypadEntry.Length > 0)
                    {
                        _keypadEntry = "";
                    }
                    else
                    {
                        CloseKeypad();
                    }
                }
                else if (keys[i] == "E")
                {
                    SubmitKeypad();
                }
                else if (_keypadEntry.Length < 4 && Time.unscaledTime >= _keypadResultUntil)
                {
                    _keypadEntry += keys[i];
                }
            }
        }

        private GUIStyle _keypadSlotStyle;
        private GUIStyle _keypadButtonStyle;
        private Dictionary<string, Texture2D> _keypadTex;
        private void DrawKeypadTex(string name, Rect rect, float alpha)
        {
            if (!_keypadTex.TryGetValue(name, out var tex) || tex == null)
            {
                return;
            }

            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, true);
            GUI.color = Color.white;
        }

        private void EnsureKeypadStyles()
        {
            if (_keypadSlotStyle != null)
            {
                return;
            }

            _keypadTex = new Dictionary<string, Texture2D>();
            foreach (var name in new[]
            {
                "Background",
                "Background_Shadow",
                "Substrate_Gray",
                "Substrate_Red",
                "Substrate_Green",
                "Flashing_Red",
                "Flashing_Green",
                "Light_Red",
                "Light_Green",
                "Button_Normal",
                "Button_Highlighted",
                "Button_Disabled",
                "Icon_Del_Static",
                "Icon_Enter_Static",
                "Number_Enter",
            }

            )
            {
                _keypadTex[name] = LoadEmbeddedTexture($"ISB.Office.keypad.{name}.png");
            }

            _keypadSlotStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 30,
                alignment = TextAnchor.MiddleCenter,
            };
            _keypadButtonStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 17,
                alignment = TextAnchor.MiddleCenter,
            };
            _keypadButtonStyle.normal.textColor = new Color(0.87f, 0.9f, 0.93f);
            foreach (var font in Resources.FindObjectsOfTypeAll<Font>())
            {
                if (font != null && font.name.ToLowerInvariant().Contains("bender"))
                {
                    _keypadSlotStyle.font = font;
                    _keypadButtonStyle.font = font;
                    break;
                }
            }
        }
    }
}
