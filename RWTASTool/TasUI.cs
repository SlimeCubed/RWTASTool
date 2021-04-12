using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RWCustom;
using System.Threading;

namespace RWTASTool
{
    public class TasUI
    {
        public static TasUI main;
        public static ReaderWriterLockSlim queueLock = new ReaderWriterLockSlim();

        public FContainer fc;
        public RainWorld rw;

        public Vector2 origin;
        public int queueIndex = 0;
        public int repeatIndex = 0;
        public List<TasInputPackage> inputQueue = new List<TasInputPackage>();

        private List<UIButton> _buttons = new List<UIButton>();

        // TASing
        private LabelButton _bUp;
        private LabelButton _bDwn;
        private LabelButton _bLft;
        private LabelButton _bRgt;
        private LabelButton _bThw;
        private LabelButton _bGrb;
        private LabelButton _bJmp;
        private LabelButton _bMap;
        private LabelButton _submit;
        private LabelButton _pgUp;
        private LabelButton _pgDown;
        private LabelButton _play;

        // Playback
        private LabelButton _addInputs;
        private LabelButton _record;
        
        public bool Play => _play.IsActive;
        public bool AddInputs => _addInputs.IsActive;
        public bool Record => _record.IsActive;
        public bool loop = true;

        public int page;
        private const int _framesPerPage = 20;
        private FLabel[] _frameNums;
        private FLabel[] _frameDescs;
        private bool _frameLabelsDirty = true;
        private NavButton[] _frameButtons;
        private FileBrowser _fileMenu;

        public TasUI(RainWorld rw)
        {
            this.rw = rw;
            origin = new Vector2(50f, 768f - 50f);
            fc = new FContainer();
            Futile.stage.AddChild(fc);

            Vector2 size = new Vector2(150f, 380f);

            // Main Interface

            // Back
            fc.AddChild(new FSprite("pixel") { anchorX = 0f, anchorY = 1f, scaleX = size.x, scaleY = size.y, alpha = 0.5f, color = Color.black });

            // Buttons
            float spacing = (size.x - 5f) / 4f;
            // Directions
            _bUp = new LabelButton(this, new Vector2(5f, -30f), new Vector2(spacing - 5f, 25f), "Up", true);
            _bDwn = new LabelButton(this, new Vector2(5f + spacing, -30f), new Vector2(spacing - 5f, 25f), "Dwn", true);
            _bLft = new LabelButton(this, new Vector2(5f + spacing * 2f, -30f), new Vector2(spacing - 5f, 25f), "Lft", true);
            _bRgt = new LabelButton(this, new Vector2(5f + spacing * 3f, -30f), new Vector2(spacing - 5f, 25f), "Rgt", true);
            // Misc
            _bGrb = new LabelButton(this, new Vector2(5f, -60f), new Vector2(spacing - 5f, 25f), "Grb", true);
            _bThw = new LabelButton(this, new Vector2(5f + spacing, -60f), new Vector2(spacing - 5f, 25f), "Thw", true);
            _bJmp = new LabelButton(this, new Vector2(5f + spacing * 2f, -60f), new Vector2(spacing - 5f, 25f), "Jmp", true);
            _bMap = new LabelButton(this, new Vector2(5f + spacing * 3f, -60f), new Vector2(spacing - 5f, 25f), "Map", true);

            // Submit
            _submit = new LabelButton(this, new Vector2(5f, -90f), new Vector2(size.x - 10f - 55f, 25f), "Submit", false, btn =>
            {
                queueLock.EnterWriteLock();
                try
                {
                    if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                        inputQueue.Insert(Mathf.Clamp(queueIndex + 1, 0, inputQueue.Count), GetInputs());
                    else if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                    {
                        inputQueue.Insert(Mathf.Clamp(queueIndex, 0, inputQueue.Count), GetInputs());
                        queueIndex++;
                    }
                    else
                        inputQueue.Add(GetInputs());
                } finally
                {
                    queueLock.ExitWriteLock();
                }
                UpdateFrameLabels();
            })
            { desc = "Add a new input frame at the end of the list.\nHold [Shift] to insert after selection, [Ctrl] to insert before.\nPressing [Enter] on the keypad does the same." };

            // Ply
            _play = new LabelButton(this, new Vector2(size.x - 55f, -90f), new Vector2(50f, 25f), "Play", true, btn =>
            {
                loop = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            }) { desc = "Run input sequence.\nHold [Shift] to loop." };

            // Page up and down
            _pgDown = new LabelButton(this, new Vector2(5f, -120f), new Vector2(size.x / 2f - 7.5f, 25f), "Page Down", false, btn =>
            {
                page++;
                if (page > Math.Max(inputQueue.Count - 1, 0) / _framesPerPage) page = 0;
                UpdateFrameLabels();
            });
            _pgUp = new LabelButton(this, new Vector2(2.5f + size.x / 2f, -120f), new Vector2(size.x / 2f - 7.5f, 25f), "Page Up", false, btn =>
            {
                page--;
                if (page < 0) page = Math.Max(inputQueue.Count - 1, 0) / _framesPerPage;
                UpdateFrameLabels();
            });

            // Frame labels
            spacing = (size.y - 10f - 120f - 35f) / _framesPerPage;
            _frameNums = new FLabel[_framesPerPage];
            _frameDescs = new FLabel[_framesPerPage];
            _frameButtons = new NavButton[_framesPerPage];
            for (int i = 0; i < _framesPerPage; i++)
            {
                float y = -size.y + 10f + 35f + spacing * (_framesPerPage - i - 1);
                _frameNums[i] = new FLabel("font", "X") { alignment = FLabelAlignment.Left, x = 5f, y = y };
                _frameDescs[i] = new FLabel("font", "UDLR") { alignment = FLabelAlignment.Left, x = 50f, y = y };
                fc.AddChild(_frameNums[i]);
                fc.AddChild(_frameDescs[i]);

                _frameButtons[i] = new NavButton(this, new Vector2(5f, y - 5f), new Vector2(size.x - 10f, spacing), i)
                {
                    descContainer = fc
                };
            }
            
            UpdateFrameLabels();

            // Playback options
            spacing = (size.x - 5f) / 2f;
            _addInputs = new LabelButton(this, new Vector2(5f, 5f - size.y), new Vector2(spacing - 5f, 25f), "Add Inputs", true)
            {
                desc = "Add the current input sequence to live inputs rather than overriding them completely."
            };
            _record = new LabelButton(this, new Vector2(5f + spacing, 5f - size.y), new Vector2(spacing - 5f, 25f), "Record", true)
            {
                desc = "Add player inputs to the end of the input sequence each frame."
            };

            // File interface
            _fileMenu = new FileBrowser(this, new Vector2(size.x + 5f, 0f));
            
            main = this;
        }
        
        public void UpdateFrameLabels()
        {
            _frameLabelsDirty = true;
        }

        private void UpdateFrameLabelsInternal()
        {
            _frameLabelsDirty = false;
            queueLock.EnterReadLock();
            try
            {
                if (page > Math.Max(inputQueue.Count - 1, 0) / _framesPerPage) page = Math.Max(inputQueue.Count - 1, 0) / _framesPerPage;
                var frames = inputQueue.ToArray();
                if (frames == null) frames = new TasInputPackage[0];
                for (int i = 0; i < _frameNums.Length; i++)
                {
                    int o = i + page * _framesPerPage;
                    if (o >= frames.Length)
                    {
                        _frameNums[i].text = string.Empty;
                        _frameDescs[i].text = string.Empty;
                        _frameButtons[i].desc = string.Empty;
                    }
                    else
                    {
                        _frameNums[i].text = (o + 1).ToString();
                        _frameDescs[i].text = inputQueue[o].ToString();
                        _frameNums[i].color = (o == queueIndex) ? Color.cyan : Color.Lerp(Color.white, Color.grey, i / 5f);
                        _frameDescs[i].color = _frameNums[i].color;
                        _frameButtons[i].desc = (o == queueIndex) ? "Click to delete, press + or - to change repetitions." : string.Empty;
                    }
                }
            }
            finally
            {
                queueLock.ExitReadLock();
            }
        }
        
        private void ChildClicked(LabelButton button)
        {
            if(_bUp.IsActive && _bDwn.IsActive)
                ((button == _bUp) ? _bDwn : _bUp).IsActive = false;
            if(_bRgt.IsActive && _bLft.IsActive)
                ((button == _bRgt) ? _bLft : _bRgt).IsActive = false;
        }

        public void SetInputs(Player.InputPackage inputs)
        {
            _bUp.IsActive  = inputs.y ==  1;
            _bDwn.IsActive = inputs.y == -1;
            _bRgt.IsActive = inputs.x ==  1;
            _bLft.IsActive = inputs.x == -1;
            _bGrb.IsActive = inputs.pckp;
            _bThw.IsActive = inputs.thrw;
            _bJmp.IsActive = inputs.jmp;
            _bMap.IsActive = inputs.mp;
        }

        public Player.InputPackage ConsumeInputs()
        {
            TasInputPackage inputs;
            queueLock.EnterReadLock();
            try
            {
                if (inputQueue.Count == 0) return new Player.InputPackage();
                if (queueIndex >= inputQueue.Count) queueIndex = 0;
                inputs = inputQueue[queueIndex];
                if (repeatIndex++ >= inputs.repetitions)
                {
                    repeatIndex = 0;
                    queueIndex++;
                    if (queueIndex >= inputQueue.Count)
                    {
                        if (!loop) _play.IsActive = false;
                        queueIndex = 0;
                    }
                }
            }
            finally
            {
                queueLock.ExitReadLock();
            }
            page = queueIndex / _framesPerPage;
            UpdateFrameLabels();
            return inputs.plyInputs;
        }

        public TasInputPackage GetInputs()
        {
            Player.InputPackage ip = new Player.InputPackage();
            ip.x = _bRgt.IsActive ? 1 : (_bLft.IsActive ? -1 : 0);
            ip.y = _bUp.IsActive ? 1 : (_bDwn.IsActive ? -1 : 0);
            ip.pckp = _bGrb.IsActive;
            ip.thrw = _bThw.IsActive;
            ip.jmp = _bJmp.IsActive;
            ip.mp = _bMap.IsActive;
            CorrectInputs(ref ip);
            return new TasInputPackage(ip);
        }
        
        public void Update()
        {
            fc.MoveToFront();
            fc.SetPosition(origin + Vector2.one * 0.01f);
            foreach (UIButton button in _buttons)
                button.Update();
            if (Input.GetKeyDown(KeyCode.KeypadEnter))
                _submit.SimulateClick();
            _fileMenu.Update();

            // Change repetitions when + or - are pressed
            if (RepeatableKeystroke(KeyCode.Plus, KeyCode.Equals, KeyCode.KeypadPlus))
            {
                bool changed;
                queueLock.EnterWriteLock();
                try
                {
                    changed = queueIndex >= 0 && queueIndex < inputQueue.Count;
                    if (changed)
                    {
                        if (inputQueue[queueIndex].repetitions < ushort.MaxValue)
                            inputQueue[queueIndex].repetitions++;
                    }
                } finally
                {
                    queueLock.ExitWriteLock();
                }
                if(changed)
                    UpdateFrameLabels();
            }
            else if (RepeatableKeystroke(KeyCode.Underscore, KeyCode.Minus, KeyCode.KeypadMinus))
            {
                bool changed;
                queueLock.EnterWriteLock();
                try
                {
                    changed = queueIndex >= 0 && queueIndex < inputQueue.Count;
                    if (changed)
                    {
                        if (inputQueue[queueIndex].repetitions > 0)
                            inputQueue[queueIndex].repetitions--;
                    }
                } finally
                {
                    queueLock.ExitWriteLock();
                }
                if(changed)
                    UpdateFrameLabels();
            }

            if (_frameLabelsDirty)
                UpdateFrameLabelsInternal();
        }

        private static float _keystrokeTimer;
        private static int _keystrokeCounter;
        private static bool RepeatableKeystroke(params KeyCode[] keys) => RepeatableKeystroke(ref _keystrokeTimer, ref _keystrokeCounter, keys);
        private static bool RepeatableKeystroke(ref float timer, ref int counter, params KeyCode[] keys)
        {
            bool hit = false;
            bool held = false;
            for(int i = 0; i < keys.Length; i++)
            {
                hit = hit || Input.GetKeyDown(keys[i]);
                held = held || Input.GetKey(keys[i]);
            }
            if(hit)
            {
                timer = 0f;
                counter = 0;
            }
            if (held)
            {
                timer += Time.deltaTime;
                if (timer > 0.5f)
                {
                    hit = true;
                    timer = (counter < 5) ? 0.25f : 0.4f;
                    counter++;
                }
            }
            return hit;
        }

        public class TasInputPackage
        {
            public Player.InputPackage plyInputs;
            public ushort repetitions;

            public TasInputPackage() {}
            public TasInputPackage(Player.InputPackage inputs, ushort repetitions = 0)
            {
                this.plyInputs = inputs;
                this.repetitions = repetitions;
            }

            private static string[] _conList = new string[16];
            public override string ToString()
            {
                for (int i = 0; i < _conList.Length; i++)
                    _conList[i] = null;
                int cli = 0;
                if (plyInputs.x != 0 || plyInputs.y != 0)
                {
                    if (plyInputs.y != 0) _conList[cli++] = plyInputs.y > 0 ? "U" : "D";
                    if (plyInputs.x != 0) _conList[cli++] = plyInputs.x > 0 ? "R" : "L";
                    _conList[cli++] = " ";
                }
                if (plyInputs.pckp) _conList[cli++] = "G";
                if (plyInputs.thrw) _conList[cli++] = "T";
                if (plyInputs.jmp) _conList[cli++] = "J";
                if (plyInputs.mp) _conList[cli++] = "M";
                if (plyInputs.analogueDir != Vector2.zero)
                {
                    _conList[cli++] = " A";
                }
                if(repetitions > 0)
                {
                    _conList[cli++] = " x";
                    _conList[cli++] = (repetitions + 1).ToString();
                }
                return string.Concat(_conList);
            }
        }

        private class FileBrowser
        {
            public bool open;
            public string path;

            public static string fileName = "New Replay";
            public static string fileExt = ".rwi";

            private string _lastPath;
            private string[] _files;
            private string[] _dirs;
            private int _page;
            private int _fileOrDirIndex;
            private bool _lastOpen;

            private TasUI _ui;
            private Vector2 _openSize;
            private Vector2 _closedSize;
            private FSprite _back;
            private LabelButton _close;
            private FLabel[] _fileLabels;
            private FLabel _filePathView;
            private UIButton _filePathButton;
            private UIButton[] _fileButtons;
            private List<FNode> _hideWhenClosed = new List<FNode>();
            private List<UIButton> _disableWhenClosed = new List<UIButton>();

            public FileBrowser(TasUI ui, Vector2 offset)
            {
                path = Custom.RootFolderDirectory() + "Input Replays";
                Directory.CreateDirectory(path);
                _ui = ui;

                _openSize = new Vector2(150f, 250f);
                _closedSize = new Vector2(35f, 35f);
                Vector2 size = _openSize;

                // Back
                _back = new FSprite("pixel") { anchorX = 0f, anchorY = 1f, x = offset.x, y = offset.y, alpha = 0.5f, color = Color.black };
                _ui.fc.AddChild(_back);

                // Collapse button
                _close = new LabelButton(_ui, offset + new Vector2(5f, -30f), new Vector2(25f, 25f), string.Empty, false, btn => open = !open);

                // Load file
                float spacing = (size.x - 35f) / 3f;
                _disableWhenClosed.Add(new LabelButton(_ui, offset + new Vector2(35f, -30f), new Vector2(spacing - 5f, 25f), "Load", false, btn =>
                {
                    int ind = _fileOrDirIndex - _dirs.Length;
                    if (ind >= 0 && ind < _files.Length)
                    {
                        queueLock.EnterWriteLock();
                        try
                        {
                            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                                ui.Load(_files[ind], Mathf.Clamp(ui.queueIndex + 1, 0, ui.inputQueue.Count));
                            else if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                                ui.Load(_files[ind], Mathf.Clamp(ui.queueIndex, 0, ui.inputQueue.Count));
                            else
                                ui.Load(_files[ind]);
                        } finally
                        {
                            queueLock.ExitWriteLock();
                        }
                    }
                })
                { desc = "Load an input sequence from a file, overwriting the current inputs.\nHold [Shift] to insert after selection, [Ctrl] to insert before." });

                // Load file
                string saveDesc = "Save the current input sequence as \"" + fileName + fileExt + "\". A number will be added onto the end if that name is taken.\nHold [Shift] to overwrite the currently selected file.";
                _disableWhenClosed.Add(new LabelButton(_ui, offset + new Vector2(35f + spacing, -30f), new Vector2(spacing - 5f, 25f), "Save", false, btn =>
                {
                    string resultPath;
                    if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                    {
                        int ind = _fileOrDirIndex - _dirs.Length;
                        if (ind >= 0 && ind < _files.Length)
                            resultPath = ui.SaveOverwrite(_files[ind]);
                        else
                            return;
                    }
                    else
                        resultPath = ui.Save(path, fileName, fileExt);
                    btn.desc = saveDesc + "\nLast saved to " + resultPath;
                    btn.ClearDesc();
                    UpdateFiles(true);
                })
                { desc = saveDesc });

                // New file
                _disableWhenClosed.Add(new LabelButton(_ui, offset + new Vector2(35f + spacing * 2f, -30f), new Vector2(spacing - 5f, 25f), "New", false, btn =>
                {
                    queueLock.EnterWriteLock();
                    try
                    {
                        ui.inputQueue.Clear();
                        ui.queueIndex = 0;
                        ui.repeatIndex = 0;
                        ui.UpdateFrameLabels();
                    } finally
                    {
                        queueLock.ExitWriteLock();
                    }
                })
                { desc = "Clear the current input sequence." });

                // Browser controls
                // Back, Up, Down
                spacing = (size.x - 5f) / 3f;
                _disableWhenClosed.Add(new LabelButton(_ui, offset + new Vector2(5f + spacing * 0f, -60f), new Vector2(spacing - 5f, 25f), "Back", false, btn =>
                {
                    int i = path.LastIndexOf(Path.DirectorySeparatorChar);
                    if (i != -1)
                        path = path.Substring(0, i);
                    UpdateFiles();
                }));
                _disableWhenClosed.Add(new LabelButton(_ui, offset + new Vector2(5f + spacing * 1f, -60f), new Vector2(spacing - 5f, 25f), "Up"  , false, btn =>
                {
                    _page--;
                    if (_page < 0) _page = 0;
                    UpdateFiles();
                }));
                _disableWhenClosed.Add(new LabelButton(_ui, offset + new Vector2(5f + spacing * 2f, -60f), new Vector2(spacing - 5f, 25f), "Down", false, btn =>
                {
                    _page++;
                    int maxPage = Math.Max((_files.Length + _dirs.Length - 1) / _fileLabels.Length, 0);
                    if (_page > maxPage) _page = maxPage;
                    UpdateFiles();
                }));

                // Path indicator
                _hideWhenClosed.Add(_filePathView = new FLabel("font", string.Empty) { anchorX = 0f, anchorY = 1f, x = offset.x + 6.5f, y = offset.y - 65f + 0.5f });
                _ui.fc.AddChild(_filePathView);
                _disableWhenClosed.Add(_filePathButton = new UIButton(_ui, offset + new Vector2(6f, -65f - 12f), new Vector2(size.x - 10f, 12f)));
                _filePathButton.descContainer = _ui.fc;

                // File labels
                // Estimated height of 12 pixels
                float startY = -65f - 15f;
                float height = size.y + startY - 12f;
                int count = Mathf.FloorToInt(height / 12f); 
                spacing = height / count;

                _fileLabels = new FLabel[count];
                _fileButtons = new UIButton[count];
                for(int i = 0; i < count; i++)
                {
                    int j = i;
                    Vector2 pos = new Vector2(offset.x + 6f, Mathf.Floor(offset.y + startY - spacing * i));
                    _fileButtons[i] = new UIButton(_ui, pos - Vector2.up * 12f, new Vector2(size.x - 10f, 12f));
                    _fileButtons[i].OnClick += btn =>
                    {
                        int newIndex = j + _page * _fileLabels.Length;
                        if(newIndex == _fileOrDirIndex)
                        {
                            if (_fileOrDirIndex < _dirs.Length)
                                path = _dirs[_fileOrDirIndex];
                        } else
                        {
                            _fileOrDirIndex = newIndex;
                        }
                        UpdateFiles();
                    };
                    _disableWhenClosed.Add(_fileButtons[i]);
                    _fileLabels[i] = new FLabel("font", string.Empty) { anchorX = 0f, anchorY = 1f, x = pos.x + 0.5f, y = pos.y + 0.5f };
                    _hideWhenClosed.Add(_fileLabels[i]);
                    _ui.fc.AddChild(_fileLabels[i]);
                }

                Open();
            }

            public void OnFileChanged(object sender, FileSystemEventArgs e) => UpdateFiles(true);

            private FileSystemWatcher _fsw;
            public void UpdateFiles(bool refreshPath = false)
            {
                if(path != _lastPath || refreshPath)
                {
                    if(path != _lastPath)
                    {
                        if (_fsw == null)
                        {
                            _fsw = new FileSystemWatcher(path, "*" + fileExt);
                            _fsw.Created += OnFileChanged;
                            _fsw.Deleted += OnFileChanged;
                            _fsw.Renamed += OnFileChanged;
                            _fsw.EnableRaisingEvents = true;
                        }
                        else _fsw.Path = path;
                        _lastPath = path;
                    }
                    try
                    {
                        _dirs = Directory.GetDirectories(path + Path.DirectorySeparatorChar);
                        _files = Directory.GetFiles(path + Path.DirectorySeparatorChar, "*" + fileExt);
                    } catch(Exception)
                    {
                        _dirs = new string[0];
                        _files = new string[0];
                    }
                    _page = 0;
                    string displayPath = path;
                    int charCount = Math.Min(10, displayPath.Length);
                    float maxWidth = _openSize.x - 10f;
                    float clipWidth = _openSize.x - 10f - 15f;
                    int clipCharCount = 0;
                    while(charCount <= displayPath.Length && (charCount < 50))
                    {
                        _filePathView.text = displayPath.Substring(displayPath.Length - charCount);
                        float w = _filePathView.textRect.width;
                        if (w < clipWidth)
                            clipCharCount = charCount;
                        if(w > maxWidth)
                        {
                            _filePathView.text = "..." + displayPath.Substring(displayPath.Length - clipCharCount);
                            charCount--;
                            break;
                        }
                        charCount++;
                    }
                    _filePathButton.desc = path;
                }
                int labelCount = _fileLabels.Length;
                int entryCount = _files.Length + _dirs.Length;
                int maxPage = (entryCount - 1) / labelCount;
                if (_page > maxPage) _page = maxPage;
                if (_page < 0) _page = 0;
                for(int i = 0; i < labelCount; i++)
                {
                    int o = i + _page * labelCount;
                    bool selected = o == _fileOrDirIndex;
                    if(o < 0 || (o >= entryCount))
                    {
                        _fileLabels[i].text = string.Empty;
                    } else
                    {
                        if (o < _dirs.Length)
                        {
                            _fileLabels[i].text = _dirs[o].Substring(_dirs[o].LastIndexOf(Path.DirectorySeparatorChar) + 1);
                            _fileLabels[i].color = new Color(selected ? 1f : 0.7f, 0f, 0f);
                        }
                        else
                        {
                            _fileLabels[i].text = _files[o - _dirs.Length].Substring(_files[o - _dirs.Length].LastIndexOf(Path.DirectorySeparatorChar) + 1);
                            _fileLabels[i].color = selected ? Color.white : new Color(0.7f, 0.7f, 0.7f);
                        }
                    }
                }
            }

            public void Update()
            {
                if (_lastOpen != open) if (open) Open(); else Close();
                _lastOpen = open;
            }

            private void Close()
            {
                open = false;
                foreach (FNode node in _hideWhenClosed) node.isVisible = false;
                foreach (UIButton btn in _disableWhenClosed) btn.active = false;
                _back.scaleX = _closedSize.x;
                _back.scaleY = _closedSize.y;
                _close.label.text = "File";
            }

            private void Open()
            {
                open = true;
                foreach (FNode node in _hideWhenClosed) node.isVisible = true;
                foreach (UIButton btn in _disableWhenClosed) btn.active = true;
                _back.scaleX = _openSize.x;
                _back.scaleY = _openSize.y;
                _close.label.text = "X";
                UpdateFiles();
            }
        }

        private string Save(string dir, string fileName, string fileExt)
        {
            // Find an open file path
            int i = 0;
            string testPath;
            do
            {
                testPath = dir + Path.DirectorySeparatorChar + fileName + (i == 0 ? string.Empty : (" " + i)) + fileExt;
                i++;
            } while (File.Exists(testPath));
            // Save to the file
            return SaveOverwrite(testPath);
        }

        private string SaveOverwrite(string path)
        {
            InputFrame f = new InputFrame();
            using (FileStream fs = File.Open(path, FileMode.Create))
            {
                queueLock.EnterReadLock();
                try
                {
                    for (int readIndex = 0; readIndex < inputQueue.Count; readIndex++)
                    {
                        f.Load(inputQueue[readIndex]);
                        f.ToStream(fs);
                    }
                } finally
                {
                    queueLock.ExitReadLock();
                }
            }
            return path;
        }

        private void Load(string path, int insertIndex = -1)
        {
            bool insert = insertIndex >= 0;
            if (!insert)
            {
                inputQueue.Clear();
                queueIndex = 0;
                repeatIndex = 0;
            }
            InputFrame f = new InputFrame();
            List<TasInputPackage> inputBuffer = new List<TasInputPackage>();
            using (FileStream fs = File.OpenRead(path))
            {
                while (f.FromStream(fs))
                    inputBuffer.Add(f.ToInputs());
            }
            if (insert)
                inputQueue.InsertRange(0, inputBuffer);
            else
                inputQueue.AddRange(inputBuffer);
            UpdateFrameLabels();
        }

        internal static void CorrectInputs(ref Player.InputPackage inputs)
        {
            if (!inputs.gamePad && Vector2.SqrMagnitude(inputs.analogueDir) == 0f)
            {
                inputs.gamePad = false;
                if (inputs.y < 0)
                    inputs.downDiagonal = inputs.x;
            }
            else
            {
                inputs.gamePad = true;
                inputs.analogueDir = Vector2.ClampMagnitude(inputs.analogueDir, 1f);
                // Ensure that the given input package is valid based on the analogue dir
                if (inputs.analogueDir.x < -0.5f)
                    inputs.x = -1;
                if (inputs.analogueDir.x > 0.5f)
                    inputs.x = 1;
                if (inputs.analogueDir.y < -0.5f)
                    inputs.y = -1;
                if (inputs.analogueDir.y > 0.5f)
                    inputs.y = 1;
                if (inputs.analogueDir.y < -0.05f)
                    if (inputs.analogueDir.x < -0.05f)
                        inputs.downDiagonal = -1;
                    else if (inputs.analogueDir.x > 0.05f)
                        inputs.downDiagonal = 1;
            }
        }

        internal static bool InputsEqual(Player.InputPackage a, Player.InputPackage b)
        {
            CorrectInputs(ref a);
            CorrectInputs(ref b);
            if (a.analogueDir != b.analogueDir ||
                a.x    != b.x    ||
                a.y    != b.y    ||
                a.jmp  != b.jmp  ||
                a.mp   != b.mp   ||
                a.pckp != b.pckp ||
                a.thrw != b.thrw) return false;
            return true;
        }

        public void Seek(int queueIndex, int repeatIndex)
        {
            this.queueIndex = queueIndex;
            this.repeatIndex = repeatIndex;
            page = Math.Max(queueIndex - 1, 0) / _framesPerPage;
            UpdateFrameLabels();
        }

        // A button used for navigating the input queue
        private class NavButton : UIButton
        {
            private int _index;
            public NavButton(TasUI parent, Vector2 pos, Vector2 size, int index) : base(parent, pos, size)
            {
                _index = index;
            }

            protected override void Clicked()
            {
                queueLock.EnterWriteLock();
                try
                {
                    int newInd = _index + ui.page * _framesPerPage;
                    if (ui.queueIndex == newInd)
                    {
                        if (ui.queueIndex < ui.inputQueue.Count && ui.queueIndex >= 0)
                            ui.inputQueue.RemoveAt(ui.queueIndex);
                        ui.queueIndex--;
                        if (ui.queueIndex < 0) ui.queueIndex = 0;
                    }
                    else
                    {
                        ui.queueIndex = newInd;
                        if (ui.queueIndex >= ui.inputQueue.Count)
                            ui.queueIndex = ui.inputQueue.Count - 1;
                    }
                    ui.repeatIndex = 0;
                } finally
                {
                    queueLock.ExitWriteLock();
                }
                ui.UpdateFrameLabels();
                base.Clicked();
            }
        }

        // A button with a label and background sprite
        private class LabelButton : UIButton
        {
            public FSprite back;
            public FLabel label;

            public Color onColor = Color.cyan;
            public Color offColor = Color.Lerp(Color.black, Color.cyan, 0.25f);

            public bool toggle;

            public bool IsActive
            {
                get => _isActive;
                set
                {
                    _isActive = value;
                    back.color = _isActive ? onColor : offColor;
                    label.color = _isActive ? offColor : onColor;
                }
            }
            private bool _isActive;

            public LabelButton(TasUI ui, Vector2 pos, Vector2 size, string text, bool toggle, ClickHandler ch = null) : base(ui, pos, size)
            {
                if (ch != null) OnClick += ch;
                this.toggle = toggle;
                back = new FSprite("pixel") { scaleX = size.x, scaleY = size.y, anchorX = 0f, anchorY = 0f, color = offColor };
                label = new FLabel("font", text) { alignment = FLabelAlignment.Center, color = onColor };
                AddToContainer(ui.fc);
            }
            
            public override void Update()
            {
                base.Update();
                label.x = Mathf.Floor(pos.x + size.x / 2f);
                label.y = Mathf.Floor(pos.y + size.y / 2f);
                back.SetPosition(pos);
                label.isVisible = active;
                back.isVisible = active;
            }

            public LabelButton AddToContainer(FContainer c)
            {
                c.AddChild(back);
                c.AddChild(label);
                descContainer = c;
                return this;
            }

            protected override void Clicked()
            {
                base.Clicked();
                if (!toggle)
                    IsActive = true;
                else
                    IsActive = !IsActive;
                ui.ChildClicked(this);
            }

            protected override void Unclicked()
            {
                base.Unclicked();
                if(!toggle)
                    IsActive = false;
            }
        }

        // An invisible button
        private class UIButton
        {
            protected TasUI ui;
            public Vector2 pos;
            public Vector2 size;
            public bool clicked;
            public bool active = true;
            public string desc = string.Empty;
            public FContainer hoverDesc;
            public FContainer descContainer;

            public bool MouseOver
            {
                get
                {
                    if (!active) return false;
                    Vector2 mp = ui.fc.ScreenToLocal(Input.mousePosition) - pos;
                    return mp.x >= 0f && mp.y >= 0f && mp.x <= size.x && mp.y <= size.y;
                }
            }

            public delegate void ClickHandler(UIButton button);
            public event ClickHandler OnClick;

            public UIButton(TasUI parent, Vector2 pos, Vector2 size)
            {
                ui = parent;
                this.pos = pos;
                this.size = size;
                parent._buttons.Add(this);
            }
            
            public void SimulateClick()
            {
                clicked = true;
                Clicked();
                Unclicked();
                clicked = false;
            }

            public void ClearDesc()
            {
                hoverDesc.RemoveFromContainer();
                hoverDesc = null;
            }

            public virtual void Update()
            {
                if (Input.GetMouseButtonDown(0))
                {
                    if (MouseOver)
                    {
                        clicked = true;
                        Clicked();
                    }
                }
                else if (Input.GetMouseButtonUp(0))
                {
                    if (clicked)
                    {
                        clicked = false;
                        Unclicked();
                    }
                }

                if (MouseOver && !string.IsNullOrEmpty(desc))
                {
                    if (hoverDesc == null && descContainer != null)
                    {
                        hoverDesc = new FContainer();
                        FLabel text = new FLabel("font", desc)
                        {
                            alignment = FLabelAlignment.Custom,
                            anchorX = 0f,
                            anchorY = 0f,
                            x = 0.5f,
                            y = 0.5f
                        };
                        hoverDesc.AddChild(new FSprite("pixel")
                        {
                            anchorX = 0f,
                            anchorY = 0f,
                            color = Color.black,
                            alpha = 0.5f,
                            scaleX = text.textRect.width + 10f,
                            scaleY = text.textRect.height + 10f,
                            x = -5f,
                            y = -5f
                        });
                        hoverDesc.AddChild(text);
                        descContainer.AddChild(hoverDesc);
                    }

                    Vector2 localMp = descContainer.ScreenToLocal(Input.mousePosition);
                    hoverDesc.x = Mathf.Floor(localMp.x) + 10f;
                    hoverDesc.y = Mathf.Floor(localMp.y) + 10f;
                }
                else if (hoverDesc != null)
                    ClearDesc();
            }

            protected virtual void Clicked()
            {
                OnClick?.Invoke(this);
            }

            protected virtual void Unclicked()
            {
            }
        }

        // Represents one frame of input in a dense format
        public class InputFrame
        {
            private DataFlags _data;
            public Vector2 analogDir;
            public ushort repCount;

            private enum DataFlags : ushort
            {
                Up = 0x0001,
                Dwn = 0x0002,
                Rgt = 0x0004,
                Lft = 0x0008,
                Grb = 0x0010,
                Thw = 0x0020,
                Jmp = 0x0040,
                Map = 0x0080,
                Alg = 0x0100,
                Rep = 0x0200,
            }

            public bool Up => (_data & DataFlags.Up) > 0;
            public bool Dwn => (_data & DataFlags.Dwn) > 0;
            public bool Lft => (_data & DataFlags.Lft) > 0;
            public bool Rgt => (_data & DataFlags.Rgt) > 0;
            public bool Grb => (_data & DataFlags.Grb) > 0;
            public bool Thw => (_data & DataFlags.Thw) > 0;
            public bool Jmp => (_data & DataFlags.Jmp) > 0;
            public bool Map => (_data & DataFlags.Map) > 0;
            public bool HasAnalog => (_data & DataFlags.Alg) > 0;
            public bool Repeat => (_data & DataFlags.Rep) > 0;

            public InputFrame() { }

            public InputFrame(TasInputPackage inputs)
            {
                Load(inputs);
            }

            public void Load(TasInputPackage inputs)
            {
                Player.InputPackage ip = inputs.plyInputs;
                _data = (ip.y == 1 ? DataFlags.Up : 0) |
                        (ip.y == -1 ? DataFlags.Dwn : 0) |
                        (ip.x == 1 ? DataFlags.Rgt : 0) |
                        (ip.x == -1 ? DataFlags.Lft : 0) |
                        (ip.pckp ? DataFlags.Grb : 0) |
                        (ip.thrw ? DataFlags.Thw : 0) |
                        (ip.jmp ? DataFlags.Jmp : 0) |
                        (ip.mp ? DataFlags.Map : 0) |
                        ((ip.analogueDir.x != 0f || ip.analogueDir.y != 0f) ? DataFlags.Alg : 0) |
                        ((inputs.repetitions > 0) ? DataFlags.Rep : 0);
                analogDir = ip.analogueDir;
                repCount = inputs.repetitions;
            }

            public TasInputPackage ToInputs()
            {
                Player.InputPackage ip = new Player.InputPackage(HasAnalog, Rgt ? 1 : (Lft ? -1 : 0), Up ? 1 : (Dwn ? -1 : 0), Jmp, Thw, Grb, Map, false);
                ip.analogueDir = analogDir;
                CorrectInputs(ref ip);
                return new TasInputPackage(ip, repCount);
            }

            private byte[] _buffer = new byte[4];
            private bool _hasLoggedError = false;
            private ushort _frameDataMask = 0;
            public bool FromStream(Stream stream)
            {
                if (stream.Read(_buffer, 0, 2) != 2) return false;
                _data = (DataFlags)BitConverter.ToUInt16(_buffer, 0);

                if(_frameDataMask == 0)
                {
                    foreach (ushort val in Enum.GetValues(typeof(DataFlags)))
                        _frameDataMask |= val;
                }

                if (((ushort)_data & _frameDataMask) != (ushort)_data)
                {
                    _hasLoggedError = true;
                    Debug.LogError(new FileFormatException("Invalid input packet, the input replay file may be corrupted or the mod may be outdated!"));
                }
                if (HasAnalog)
                {
                    if (stream.Read(_buffer, 0, 4) != 4) return false;
                    analogDir.x = BitConverter.ToSingle(_buffer, 0);
                    if (stream.Read(_buffer, 0, 4) != 4) return false;
                    analogDir.y = BitConverter.ToSingle(_buffer, 0);
                    if (analogDir.sqrMagnitude > 1.01f && !_hasLoggedError)
                    {
                        _hasLoggedError = true;
                        Debug.LogError(new FileFormatException("Invalid analog direction, the input replay file may be corrupted or the mod may be outdated!"));
                    }
                } else
                {
                    analogDir = new Vector2();
                }
                if(Repeat)
                {
                    if (stream.Read(_buffer, 0, 2) != 2) return false;
                    repCount = BitConverter.ToUInt16(_buffer, 0);
                    if(repCount == 0 && !_hasLoggedError)
                    {
                        _hasLoggedError = true;
                        Debug.LogError(new FileFormatException("Repetition count of 0 encountered, the input replay file may be corrupted or the mod may be outdated!"));
                    }
                }
                else
                {
                    repCount = 0;
                }
                return true;
            }

            public void ToStream(Stream stream)
            {
                stream.Write(BitConverter.GetBytes((ushort)_data), 0, 2);
                if (HasAnalog)
                {
                    stream.Write(BitConverter.GetBytes(analogDir.x), 0, 4);
                    stream.Write(BitConverter.GetBytes(analogDir.y), 0, 4);
                }
                if(Repeat)
                {
                    stream.Write(BitConverter.GetBytes(repCount), 0, 2);
                }
            }

            public class FileFormatException : Exception
            {
                public FileFormatException(string message) : base(message) {}

                public FileFormatException(string message, Exception innerException) : base(message, innerException) {}
            }
        }
    }
}
