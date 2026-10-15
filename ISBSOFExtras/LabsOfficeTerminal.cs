using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using ISB.Compatibility;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ISBSSOFExtra
{
    internal sealed class OfficeTerminalFile
    {
        public string name { get; set; } = "";
        public string nameKey { get; set; } = "";
        public string textKey { get; set; } = "";
        public string text { get; set; } = "";
        public OfficeKteDocument kteBriefing { get; set; }
    }

    internal sealed partial class LabsOfficeScene
    {
        private Font _terminalFont, _documentFont;
        private GUIStyle _documentPaper, _documentTitle, _documentBody;
        private GUIStyle _terminalLabel, _terminalButton, _terminalWarning, _terminalTitle, _terminalHint, _terminalClose;
        private OfficeTerminalFile[] _terminalFiles;
        private int _selectedFile = -1;
        private OfficeTerminalFile _kteFile;
        private int[] _kteSelection;
        private string _kteHeader, _kteBody;
        private Vector2 _terminalScroll;
        private Vector2 _documentScroll;
        private static readonly string[] Cells =
        {
            "A",
            "B",
            "C",
            "D"
        };
        private static readonly string[] Pistons =
        {
            "A",
            "B",
            "C"
        };
        private readonly GUIContent _buttonContent = new GUIContent();
        private readonly GUIContent _warningContent = new GUIContent();
        private string _cachedStatus = "", _cachedGenerators = "";
        private float _statusAt;
        private string StateLabel(string name)
        {
            bool active = ControlState?.Invoke(name) == true;
            return Text(active ? "On" : "Off", active ? "ON" : "OFF");
        }

        private void ToggleControl(string name, string label)
        {
            bool active = ControlState?.Invoke(name) == true;
            if (TerminalButton(label + " [" + StateLabel(name) + "] > " + Text(active ? "TurnOff" : "TurnOn", active ? "TURN OFF" : "TURN ON")))
                SendTerminalCommand(name + (active ? "_OFF" : "_ON"));
        }

        private void TerminalGui()
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                PlayKeypadSound("keypress");
                CloseKeypad();
                e.Use();
                return;
            }

            if (e.type == EventType.KeyDown)
            {
                bool handled = true;
                if (e.character == '0')
                {
                    _terminalPage = _terminalPage == 4 ? 1 : 0;
                    _terminalScroll = Vector2.zero;
                }
                else if (_terminalPage == 0 && e.character >= '1' && e.character <= '6')
                    _terminalPage = e.character == '4' ? 5 : e.character == '5' ? 6 : e.character == '6' ? 7 : e.character - '0';
                else if (_terminalPage == 3 && (e.character == 'y' || e.character == 'Y'))
                    ConfirmTerminalShutdown();
                else if (_terminalPage == 3 && (e.character == 'n' || e.character == 'N'))
                    _terminalPage = 0;
                else
                    handled = false;
                if (handled)
                {
                    PlayKeypadSound("keypress");
                    _statusAt = 0f;
                    e.Use();
                }
            }

            if (_terminalFont == null)
            {
                _terminalFont = Font.CreateDynamicFontFromOSFont("Consolas", 20);
                _terminalLabel = new GUIStyle(GUI.skin.label)
                {
                    font = _terminalFont,
                    wordWrap = true
                };
                _terminalLabel.normal.textColor = new Color(.62f, .95f, .77f);
                _terminalButton = new GUIStyle(GUI.skin.label)
                {
                    font = _terminalFont,
                    alignment = TextAnchor.MiddleLeft,
                    wordWrap = true,
                    padding = new RectOffset(8, 8, 5, 5)
                };
                _terminalLabel.normal.textColor = new Color(.78f, .81f, .8f);
                _terminalButton.normal.textColor = new Color(.78f, .81f, .8f);
                _terminalButton.hover.textColor = new Color(.5f, 1f, .72f);
                _terminalButton.active.textColor = Color.white;
                _terminalClose = new GUIStyle(_terminalButton)
                {
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = false,
                    clipping = TextClipping.Clip,
                    padding = new RectOffset(0, 0, 0, 0)
                };
                _terminalWarning = new GUIStyle(_terminalLabel)
                {
                    padding = new RectOffset(12, 12, 10, 10)
                };
                _terminalWarning.normal.textColor = new Color(1f, .38f, .32f);
                _terminalTitle = new GUIStyle(_terminalLabel)
                {
                    fontStyle = FontStyle.Bold
                };
                _terminalTitle.normal.textColor = new Color(.65f, 1f, .81f);
                _terminalHint = new GUIStyle(_terminalLabel)
                {
                    padding = new RectOffset(26, 8, 0, 14)
                };
                _terminalHint.normal.textColor = new Color(.46f, .63f, .57f);
                try
                {
                    var path = Path.Combine(Path.GetDirectoryName(typeof(LabsOfficeScene).Assembly.Location), "Office", "terminal_files.json");
                    _terminalFiles = JsonConvert.DeserializeObject<OfficeTerminalFile[]>(File.ReadAllText(path)) ?? new OfficeTerminalFile[0];
                }
                catch (Exception ex)
                {
                    _log.LogError("ISB: terminal files could not be loaded: " + ex.Message);
                    _terminalFiles = new OfficeTerminalFile[0];
                }
            }

            if (Time.unscaledTime >= _statusAt)
            {
                _statusAt = Time.unscaledTime + .1f;
                _cachedStatus = TerminalStatus?.Invoke() ?? "";
                _cachedGenerators = GeneratorStatus?.Invoke() ?? "";
            }

            float width = Mathf.Min(1120, Screen.width - 48), height = Mathf.Min(780, Screen.height - 48);
            var area = new Rect((Screen.width - width) * .5f, (Screen.height - height) * .5f, width, height);
            var color = GUI.color;
            bool enabled = GUI.enabled;
            int depth = GUI.depth;
            try
            {
                GUI.depth = -9000;
                GUI.enabled = true;
                TerminalPanel(new Rect(area.x + 8, area.y + 10, width, height), new Color(0f, 0f, 0f, .65f));
                TerminalPanel(area, new Color(.22f, .43f, .35f));
                TerminalPanel(new Rect(area.x + 1, area.y + 1, width - 2, height - 2), new Color(.012f, .022f, .019f, .99f));
                TerminalPanel(new Rect(area.x + 1, area.y + 1, width - 2, 52), new Color(.055f, .105f, .085f));
                _terminalLabel.fontSize = _terminalButton.fontSize = _terminalClose.fontSize = Mathf.Clamp(Mathf.RoundToInt(height / 36), 14, 21);
                _terminalTitle.fontSize = _terminalLabel.fontSize + 1;
                _terminalHint.fontSize = Mathf.Max(12, _terminalLabel.fontSize - 3);
                GUILayout.BeginArea(new Rect(area.x + 20, area.y + 14, width - 40, height - 28));
                try
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(Text("TerminalTitle", "C.A.S.S.I.E. / EMERGENCY TERMINAL"), _terminalTitle);
                    if (GUILayout.Button("X", _terminalClose, GUILayout.Width(36), GUILayout.Height(32)))
                    {
                        PlayKeypadSound("keypress");
                        CloseKeypad();
                    }

                    GUILayout.EndHorizontal();
                    GUILayout.Space(20);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(Text("Authorized", "ACCESS GRANTED"), _terminalTitle, GUILayout.Width(width * .32f));
                    GUILayout.Label(_cachedStatus, _terminalLabel);
                    GUILayout.EndHorizontal();
                    TerminalRule();
                    GUILayout.Label("C:\\CASSIE\\" + TerminalPageTitle(), _terminalHint);
                    GUILayout.Space(6);
                    bool scrollOptions = _terminalPage != 1 && _terminalPage != 4;
                    if (scrollOptions)
                        _terminalScroll = GUILayout.BeginScrollView(_terminalScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.ExpandHeight(true));
                    else
                        GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
                    try
                    {
                        if (_terminalPage == 0)
                        {
                            _warningContent.text = Text("DeadManWarning", "DEAD MAN'S SWITCH AUTOMATICALLY ARMED — OPERATE WITH CAUTION");
                            var warning = _warningContent;
                            _terminalWarning.fontSize = _terminalLabel.fontSize;
                            var warningRect = GUILayoutUtility.GetRect(warning, _terminalWarning, GUILayout.ExpandWidth(true));
                            GUI.color = new Color(.22f, .015f, .012f, .95f);
                            GUI.DrawTexture(warningRect, Texture2D.whiteTexture);
                            GUI.color = Color.white;
                            GUI.Label(warningRect, warning, _terminalWarning);
                            GUI.color = color;
                            GUILayout.Space(10);
                            GUILayout.BeginHorizontal();
                            GUILayout.BeginVertical(GUILayout.Width((width - 76) * .5f));
                            TerminalMenu(1, 1, "Files", "DOWNLOAD FILES", "FilesHint", "SYSTEM ARCHIVE / SSD TRANSFER");
                            TerminalMenu(2, 2, "Logs", "SYSTEM DATA", "LogsHint", "FACILITY RECORDS / AUTHORIZATION");
                            TerminalMenu(3, 3, "Control", "DECONTAMINATION CONTROL", "ControlHint", "EMERGENCY CODE REQUIRED");
                            GUILayout.EndVertical();
                            GUILayout.Space(20);
                            GUILayout.BeginVertical();
                            TerminalMenu(4, 5, "PowerMenu", "FACILITY POWER / ENERGY CELLS", "PowerHint", "MASTER CONTROL / REMOTE ACTIVATION");
                            TerminalMenu(5, 6, "AlarmMenu", "LIGHTS / ALARM", "AlarmHint", "LIGHTING / PUBLIC ADDRESS");
                            TerminalMenu(6, 7, "PistonMenu", "GATE PISTONS", "PistonHint", "GATE ACTUATORS / OVERRIDE");
                            GUILayout.EndVertical();
                            GUILayout.EndHorizontal();
                        }
                        else if (_terminalPage == 1)
                        {
                            var disk = _state.operations;
                            if (!disk.diskInstalled)
                                GUILayout.Label(Text("NoDisk", "No hard drive - install an SSD to download."), _terminalWarning);
                            for (int i = 0; i < _terminalFiles.Length; i++)
                                if (TerminalButton(((disk.downloaded & (1 << i)) != 0 && disk.downloader == _world.MainPlayer.ProfileId ? "[OK] " : "[>] ") + ISBLocalizedText.Get(_terminalFiles[i].nameKey, _terminalFiles[i].name)))
                                {
                                    _selectedFile = i;
                                    _terminalPage = 4;
                                    _terminalScroll = _documentScroll = Vector2.zero;
                                }

                            if (disk.CanEject(_world.MainPlayer.ProfileId) && TerminalButton(Text("EjectSsd", "EJECT SSD")))
                                SendTerminalCommand("EJECT:" + disk.disk);
                        }
                        else if (_terminalPage == 2)
                            GUILayout.Label(Text("SystemData", "LOCAL EMERGENCY CONTROL\nAuthorization: verified\nPower controls: A / B / C / D / MASTER\nRemote shutdown requires operator confirmation."), _terminalLabel);
                        else if (_terminalPage == 3)
                        {
                            bool allowed = _state.stage == 2;
                            GUILayout.Label(Text(allowed ? "DeconAllowed" : "DeconDenied", allowed ? "DECONTAMINATION ACCESS: GRANTED" : "DECONTAMINATION ACCESS: DENIED"), _terminalLabel);
                            GUILayout.Label(_state.aborted ? Text("Stopped", "DECONTAMINATION DISABLED") : Text("DeconEnabled", "DECONTAMINATION ENABLED"), _terminalLabel);
                            if (TerminalButton(_state.aborted ? Text("Resume", "[Y] ENABLE DECONTAMINATION") : Text("ConfirmShutdown", "[Y] CONFIRM SHUTDOWN")))
                                ConfirmTerminalShutdown();
                            if (_emergencyNotice && !allowed)
                            {
                                GUILayout.Label(Text("EmergencyRequired", "ENTER THE EMERGENCY PASSWORD\nClose this terminal and enter the emergency code at the physical keypad."), _terminalLabel);
                                if (TerminalButton(Text("CloseForCode", "CLOSE TERMINAL / ENTER PASSWORD")))
                                    CloseKeypad();
                            }
                        }
                        else if (_terminalPage == 5)
                        {
                            GUILayout.Label(_cachedGenerators, _terminalLabel);
                            GUILayout.Label(Text("CellHelp", "Three remote operations at a time. Duration: 02:00. Cells can be programmed independently here. The master requires all four cells."), _terminalLabel);
                            foreach (string cell in Cells)
                                if (TerminalButton(Text("ProgramCell", "PROGRAM ENERGY CELL") + " " + cell + " [" + StateLabel("CELL_" + cell) + "] > 02:00"))
                                    SendTerminalCommand("CELL_" + cell);
                            ToggleControl("POWER", Text("Master", "FACILITY MASTER"));
                        }
                        else if (_terminalPage == 6)
                        {
                            ToggleControl("LIGHTS", Text("Lights", "EMERGENCY LIGHTS"));
                            ToggleControl("ALARM", Text("Alarm", "ALARM"));
                        }
                        else if (_terminalPage == 7)
                        {
                            GUILayout.Label(_cachedGenerators, _terminalLabel);
                            GUILayout.Label(Text("PistonHelp", "Three remote operations at a time. Duration: 02:00. Pistons can be programmed independently here."), _terminalLabel);
                            foreach (string piston in Pistons)
                                if (TerminalButton(Text("ProgramPiston", "PROGRAM GATE PISTON") + " " + piston + " [" + StateLabel("GATE_" + piston) + "] > 02:00"))
                                    SendTerminalCommand("PISTON_" + piston);
                            if (TerminalButton(Text("GateOverride", "OPEN GATES") + " [" + StateLabel("GATES") + "]"))
                                SendTerminalCommand("GATES_OPEN");
                        }
                        else if (_selectedFile >= 0 && _selectedFile < _terminalFiles.Length)
                        {
                            var disk = _state.operations;
                            string profile = _world.MainPlayer.ProfileId;
                            bool own = disk.downloader == profile;
                            bool completed = own && (disk.downloaded & (1 << _selectedFile)) != 0;
                            bool downloading = own && disk.downloading == _selectedFile;
                            var file = _terminalFiles[_selectedFile];
                            GUILayout.Label(Text("ReadOnlyDocument", "DOCUMENT / READ ONLY"), _terminalHint);
                            TerminalDocument(ISBLocalizedText.Get(file.nameKey, file.name), TerminalFileText(file), _terminalLabel.fontSize, Mathf.Clamp(height * .43f, 150f, 335f));
                            if (downloading || completed)
                            {
                                float progress = completed ? 1f : Mathf.Clamp01(1f - (float)(disk.downloadAt - (TerminalClock?.Invoke() ?? 0d)) / 3f);
                                GUILayout.Label(Text(completed ? "DownloadComplete" : "Downloading", completed ? "Download complete" : "Downloading...") + " " + (int)(progress * 100) + "%", _terminalLabel);
                            }

                            if (!disk.diskInstalled)
                                GUILayout.Label(Text("NoDisk", "No hard drive - install an SSD to download."), _terminalWarning);
                            else if (!own && disk.downloader != "")
                                GUILayout.Label(Text("DownloadBusy", "The SSD is being used by another operator."), _terminalHint);
                            bool canDownload = GUI.enabled;
                            GUI.enabled = canDownload && disk.CanDownload(profile, _selectedFile, disk.disk);
                            if (TerminalButton(Text("DownloadFile", "Download File")))
                                SendTerminalCommand("DOWNLOAD:" + _selectedFile + ":" + disk.disk);
                            GUI.enabled = canDownload;
                            if (disk.CanEject(profile) && TerminalButton(Text("EjectSsd", "EJECT SSD")))
                                SendTerminalCommand("EJECT:" + disk.disk);
                        }
                    }
                    finally
                    {
                        if (scrollOptions)
                            GUILayout.EndScrollView();
                        else
                            GUILayout.EndVertical();
                    }

                    GUILayout.FlexibleSpace();
                    TerminalRule();
                    if (_terminalPage != 0 && TerminalButton(Text("Back", "[0] BACK")))
                    {
                        _terminalPage = _terminalPage == 4 ? 1 : 0;
                        _terminalScroll = Vector2.zero;
                    }

                    if (TerminalButton(Text("Exit", "[ESC] CLOSE TERMINAL")))
                        CloseKeypad();
                    GUILayout.Label("C:\\CASSIE> " + ((int)(Time.unscaledTime * 2) % 2 == 0 ? "_" : ""), _terminalLabel);
                }
                finally
                {
                    GUILayout.EndArea();
                }
            }
            finally
            {
                GUI.color = color;
                GUI.enabled = enabled;
                GUI.depth = depth;
            }
        }

        private string TerminalFileText(OfficeTerminalFile file)
        {
            if (file.kteBriefing == null || !OfficeKteBriefing.Valid(_state.kteBriefing))
                return ISBLocalizedText.Get(file.textKey, file.text);
            string header = ISBLocalizedText.Get("ISB/Office/KteHeader", file.kteBriefing.header);
            if (_kteFile != file || _kteHeader != header || !OfficeKteBriefing.Same(_kteSelection, _state.kteBriefing))
            {
                _kteFile = file;
                _kteHeader = header;
                _kteSelection = (int[])_state.kteBriefing.Clone();
                _kteBody = file.kteBriefing.Render(_kteSelection, ISBLocalizedText.Get) ?? ISBLocalizedText.Get(file.textKey, file.text);
            }

            return _kteBody;
        }

        private void TerminalDocument(string title, string body, int fontSize, float height)
        {
            if (_documentFont == null)
            {
                _documentFont = Font.CreateDynamicFontFromOSFont("Segoe UI", 20);
                _documentPaper = new GUIStyle
                {
                    padding = new RectOffset(28, 28, 24, 28),
                    margin = new RectOffset(12, 12, 4, 16)
                };
                _documentPaper.normal.background = Texture2D.whiteTexture;
                _documentBody = new GUIStyle(GUI.skin.label)
                {
                    font = _documentFont,
                    wordWrap = true,
                    richText = false,
                    padding = new RectOffset(0, 0, 12, 12)
                };
                _documentBody.normal.textColor = new Color(.12f, .15f, .17f);
                _documentTitle = new GUIStyle(_documentBody)
                {
                    fontStyle = FontStyle.Bold
                };
            }

            _documentBody.fontSize = fontSize;
            _documentTitle.fontSize = fontSize + 3;
            GUILayout.BeginVertical(_documentPaper, GUILayout.ExpandWidth(true), GUILayout.Height(height));
            try
            {
                GUILayout.Label(title, _documentTitle);
                _documentScroll = GUILayout.BeginScrollView(_documentScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.ExpandHeight(true));
                try
                {
                    GUILayout.Label(body, _documentBody, GUILayout.ExpandWidth(true));
                }
                finally
                {
                    GUILayout.EndScrollView();
                }
            }
            finally
            {
                GUILayout.EndVertical();
            }
        }

        private static void TerminalPanel(Rect rect, Color tint)
        {
            var color = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = color;
        }

        private static void TerminalRule()
        {
            GUILayout.Space(12);
            TerminalPanel(GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true)), new Color(.17f, .29f, .24f));
            GUILayout.Space(10);
        }

        private string TerminalPageTitle()
        {
            switch (_terminalPage)
            {
                case 1:
                case 4:
                    return Text("Files", "DOWNLOAD FILES");
                case 2:
                    return Text("Logs", "SYSTEM DATA");
                case 3:
                    return Text("Control", "DECONTAMINATION CONTROL");
                case 5:
                    return Text("PowerMenu", "FACILITY POWER / ENERGY CELLS");
                case 6:
                    return Text("AlarmMenu", "LIGHTS / ALARM");
                case 7:
                    return Text("PistonMenu", "GATE PISTONS");
                default:
                    return Text("MainMenu", "MAIN MENU");
            }
        }

        private void TerminalMenu(int number, int page, string key, string fallback, string hintKey, string hint)
        {
            if (TerminalButton("[" + number + "] " + Text(key, fallback)))
                _terminalPage = page;
            GUILayout.Label(Text(hintKey, hint), _terminalHint);
        }

        private bool TerminalButton(string text)
        {
            _buttonContent.text = "> " + text;
            var rect = GUILayoutUtility.GetRect(_buttonContent, _terminalButton, GUILayout.MinHeight(40), GUILayout.ExpandWidth(true));
            bool hover = GUI.enabled && rect.Contains(Event.current.mousePosition);
            if (hover)
                TerminalPanel(rect, new Color(.06f, .15f, .105f));
            bool clicked = GUI.Button(rect, (hover ? "> " : "  ") + text, _terminalButton);
            if (clicked)
            {
                PlayKeypadSound("keypress");
                _statusAt = 0f;
            }

            return clicked;
        }

        private void DisposeTerminal()
        {
            Object.Destroy(_terminalFont);
            Object.Destroy(_documentFont);
            _terminalFont = _documentFont = null;
        }
    }

    public partial class ISBSpecialForcesAudio
    {
        private string GetTerminalDisplayControls()
        {
            string on = ISBLocalizedText.Get("ISB/Office/On", "ON"), off = ISBLocalizedText.Get("ISB/Office/Off", "OFF");
            return ISBLocalizedText.Get("ISB/Office/Lights", "EMERGENCY LIGHTS") + " [" + (TerminalState("LIGHTS") ? on : off) + "]\n" + ISBLocalizedText.Get("ISB/Office/Alarm", "ALARM") + " [" + (TerminalState("ALARM") ? on : off) + "]\n\n" + GetGeneratorStatus() + "\n\n" + ISBLocalizedText.Get("ISB/Office/PistonMenu", "GATE PISTONS") + "\n" + "A [" + (_takeoverGateA ? on : off) + "] B [" + (_takeoverGateB ? on : off) + "] C [" + (_takeoverGateC ? on : off) + "]";
        }

        private string GetTerminalStatus()
        {
            int cells = (_takeoverPowerA ? 1 : 0) + (_takeoverPowerB ? 1 : 0) + (_takeoverPowerC ? 1 : 0) + (_takeoverPowerD ? 1 : 0) + (_facilitySequence.masterStarted ? 1 : 0);
            string power = ISBLocalizedText.Get("ISB/Office/" + (_blackoutApplied ? "PowerOffline" : "PowerOnline"), _blackoutApplied ? "OFFLINE" : "ONLINE");
            string gas = _officeAccess?.aborted == true ? "GasDisabled" : _decontamination?.HasStarted(DecontaminationClock) == true ? "GasActive" : "GasArmed";
            string decon = ISBLocalizedText.Get("ISB/Office/" + gas, gas.Substring(3).ToUpperInvariant());
            return string.Format(ISBLocalizedText.Get("ISB/Office/Status", "POWER: {0} | GENERATORS: {1}/5\nDECONTAMINATION: {2}"), power, cells, decon);
        }
    }
}
