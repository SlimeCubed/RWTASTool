using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using RWCustom;

namespace RWTASTool
{
    // TODO:
    // Find out why an extra frame is recorded at the start, but it desyncs when it is played back

    public class TasMod : Partiality.Modloader.PartialityMod
    {
        public const string version = "1.1";

        public static bool paused;

        public TasUI ui;
        public DeterministicRNG dRNG;

        private bool _frameAdvance;
        private float _frameAdvanceTimer = 0f;

        public TasMod()
        {
            ModID = "Rain World TAS Tool";
            Version = version;
            author = "Slime_Cubed";
        }
        
        private Dictionary<EntityID, List<WeakReference>> _abstractItems = new Dictionary<EntityID, List<WeakReference>>();
        private Dictionary<EntityID, List<WeakReference>> _realItems = new Dictionary<EntityID, List<WeakReference>>();
        public override void OnEnable()
        {
            // Tas UI
            On.RainWorld.Start += RainWorld_Start;
            On.RainWorld.Update += RainWorld_Update;
            On.RainWorldGame.RawUpdate += RainWorldGame_RawUpdate;
            On.Player.checkInput += Player_checkInput;
            On.RWInput.PlayerInput += RWInput_PlayerInput;
            On.MainLoopProcess.RawUpdate += MainLoopProcess_RawUpdate;
            
            // Duplication debug
            On.PhysicalObject.ctor += PhysicalObject_ctor;
            On.AbstractPhysicalObject.ctor += AbstractPhysicalObject_ctor;
            On.RoomCamera.DrawUpdate += RoomCamera_DrawUpdate;

            // RNG determinization
            //On.RainWorldGame.ctor += RainWorldGame_ctor;
            //On.RainWorldGame.ShutDownProcess += RainWorldGame_ShutDownProcess;
        }

        private FieldInfo _MainLoopProcess_myTimeStacker = typeof(MainLoopProcess).GetField("myTimeStacker", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private void MainLoopProcess_RawUpdate(On.MainLoopProcess.orig_RawUpdate orig, MainLoopProcess self, float dt)
        {
            if (paused)
            {
                if (_frameAdvance)
                {
                    _frameAdvance = false;
                    self.Update();
                    _MainLoopProcess_myTimeStacker.SetValue(self, 0f);
                }
                self.GrafUpdate(1f);
                return;
            }
            orig(self, dt);
        }

        private void RainWorldGame_RawUpdate(On.RainWorldGame.orig_RawUpdate orig, RainWorldGame self, float dt)
        {
            orig(self, dt);
        }

        private void RainWorldGame_ShutDownProcess(On.RainWorldGame.orig_ShutDownProcess orig, RainWorldGame self)
        {
            if(dRNG != null)
            {
                dRNG.Dispose();
                dRNG = null;
            }
            orig(self);
        }

        private void RainWorldGame_ctor(On.RainWorldGame.orig_ctor orig, RainWorldGame self, ProcessManager manager)
        {
            if (dRNG != null) dRNG.Dispose();
            dRNG = new DeterministicRNG(self, 0);
            orig(self, manager);
        }

        private FLabel _dupeDebug;
        private List<EntityID> _toRemove = new List<EntityID>();
        private void RoomCamera_DrawUpdate(On.RoomCamera.orig_DrawUpdate orig, RoomCamera self, float timeStacker, float timeSpeed)
        {
            orig(self, timeStacker, timeSpeed);
            
            // Cull dead refs
            foreach (var pair in _abstractItems)
            {
                List<WeakReference> list = pair.Value;
                list.RemoveAll(wr => !wr.IsAlive);
                if (list.Count == 0)
                    _toRemove.Add(pair.Key);
            }
            for (int i = _toRemove.Count - 1; i >= 0; i--) _abstractItems.Remove(_toRemove[i]);
            _toRemove.Clear();

            foreach (var pair in _realItems)
            {
                List<WeakReference> list = pair.Value;
                list.RemoveAll(wr => !wr.IsAlive);
                if (list.Count == 0)
                    _toRemove.Add(pair.Key);
            }
            for (int i = _toRemove.Count - 1; i >= 0; i--) _realItems.Remove(_toRemove[i]);
            _toRemove.Clear();

            // Update duplication debug

            if (_dupeDebug == null)
            {
                _dupeDebug = new FLabel("font", "");
                _dupeDebug.color = Color.magenta;
                _dupeDebug.anchorX = 0f;
                _dupeDebug.anchorY = 0f;
                Futile.stage.AddChild(_dupeDebug);
            }
            _dupeDebug.x = Mathf.Floor(Input.mousePosition.x + 5f) + 0.5f;
            _dupeDebug.y = Mathf.Floor(Input.mousePosition.y + 5f) + 0.5f;

            EntityID? hovered = null;
            BodyChunk hoveredChunk = null;

            if (self.room != null)
            {
                Vector2 mp = (Vector2)Input.mousePosition + Vector2.Lerp(self.lastPos, self.pos, timeStacker);
                for (int i = self.room.physicalObjects.Length - 1; i >= 0; i--)
                {
                    List<PhysicalObject> objs = self.room.physicalObjects[i];
                    for (int j = objs.Count - 1; j >= 0; j--)
                    {
                        PhysicalObject obj = objs[j];
                        for (int k = obj.bodyChunks.Length - 1; k >= 0; k--)
                        {
                            if (Custom.DistLess(Vector2.Lerp(obj.bodyChunks[k].lastPos, obj.bodyChunks[k].pos, timeStacker), mp, obj.bodyChunks[k].rad))
                            {
                                hovered = obj.abstractPhysicalObject.ID;
                                hoveredChunk = obj.bodyChunks[k];
                                goto foundHoveredObject;
                            }
                        }
                    }
                }
            }
        foundHoveredObject:

            if (!Input.GetKey(KeyCode.RightBracket)) hovered = null;

            _dupeDebug.isVisible = hovered.HasValue;
            if(hovered.HasValue)
            {
                EntityID id = hovered.Value;
                int inRoom = 0;
                List<UpdatableAndDeletable> list = hoveredChunk.owner.room.updateList;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i] is PhysicalObject po && po.abstractPhysicalObject.ID == id)
                        inRoom++;
                }
                _dupeDebug.text = "ID: " + id.ToString()
                    + "\nAbstract: " + (_abstractItems.TryGetValue(id, out var absList) ? absList.Count : 0)
                    + "\nRealized: " + (_realItems.TryGetValue(id, out var realList) ? realList.Count : 0)
                    + "\nIn Room: " + inRoom;
                _dupeDebug.MoveToFront();
                _dupeDebug.color = (hoveredChunk.owner == hoveredChunk.owner.abstractPhysicalObject.realizedObject) ? Color.green : Color.magenta;
            }
        }

        private void AbstractPhysicalObject_ctor(On.AbstractPhysicalObject.orig_ctor orig, AbstractPhysicalObject self, World world, AbstractPhysicalObject.AbstractObjectType type, PhysicalObject realizedObject, WorldCoordinate pos, EntityID ID)
        {
            orig(self, world, type, realizedObject, pos, ID);
            if(!_abstractItems.TryGetValue(ID, out List<WeakReference> list))
            {
                list = new List<WeakReference>();
                _abstractItems[ID] = list;
            }
            list.Add(new WeakReference(self));
        }

        private void PhysicalObject_ctor(On.PhysicalObject.orig_ctor orig, PhysicalObject self, AbstractPhysicalObject abstractPhysicalObject)
        {
            orig(self, abstractPhysicalObject);
            if (!_realItems.TryGetValue(abstractPhysicalObject.ID, out List<WeakReference> list))
            {
                list = new List<WeakReference>();
                _realItems[abstractPhysicalObject.ID] = list;
            }
            list.Add(new WeakReference(self));
        }

        // Consume input events from the TasUI instead of controller
        private bool _useTasUIInput = false;
        private bool _record = false;
        private void Player_checkInput(On.Player.orig_checkInput orig, Player self)
        {
            if (ui.Play)
                _useTasUIInput = true;
            if (ui.Record && !ui.Play)
                _record = true;
            orig(self);
            _record = false;
            _useTasUIInput = false;
        }

        private Player.InputPackage RWInput_PlayerInput(On.RWInput.orig_PlayerInput orig, int playerNumber, Options options, RainWorldGame.SetupValues setup)
        {
            if (_useTasUIInput)
            {
                Player.InputPackage inputs = ui.ConsumeInputs();
                if(ui.AddInputs)
                {
                    Player.InputPackage live = orig(playerNumber, options, setup);
                    if(Vector2.SqrMagnitude(live.analogueDir) > Vector2.SqrMagnitude(inputs.analogueDir))
                        inputs.analogueDir = live.analogueDir;
                    if (inputs.x == 0) inputs.x = live.x;
                    if (inputs.y == 0) inputs.y = live.y;
                    if (live.jmp) inputs.jmp = true;
                    if (live.mp) inputs.mp = true;
                    if (live.pckp) inputs.pckp = true;
                    if (live.thrw) inputs.thrw = true;
                    if (Vector2.SqrMagnitude(inputs.analogueDir) > 0f) inputs.gamePad = true;
                }
                TasUI.CorrectInputs(ref inputs);
                return inputs;
            }
            if(_record)
            {
                Player.InputPackage ip = orig(playerNumber, options, setup);

                TasUI.queueLock.EnterWriteLock();
                try
                {
                    // Repeat the last input frame if the properties are the same
                    if (ui.inputQueue.Count > 0)
                    {
                        TasUI.TasInputPackage lastIp = ui.inputQueue[ui.inputQueue.Count - 1];
                        if (TasUI.InputsEqual(lastIp.plyInputs, ip))
                        {
                            lastIp.repetitions++;
                            ui.Seek(ui.inputQueue.Count - 1, lastIp.repetitions - 1);
                            return ip;
                        }
                    }

                    // Otherwise, add a new frame
                    ui.inputQueue.Add(new TasUI.TasInputPackage(ip));
                    ui.Seek(ui.inputQueue.Count - 1, 0);
                } finally
                {
                    TasUI.queueLock.ExitWriteLock();
                }
                return ip;
            }
            return orig(playerNumber, options, setup);
        }

        private void RainWorld_Start(On.RainWorld.orig_Start orig, RainWorld self)
        {
            orig(self);

            ui = new TasUI(self);
        }

        private bool InputAdvance => Input.GetKeyDown(KeyCode.Keypad0) || Input.GetKeyDown(KeyCode.Insert);
        private bool InputPause => Input.GetKeyDown(KeyCode.KeypadPeriod) || Input.GetKeyDown(KeyCode.Delete);
        private bool InputDelayedAdvance => Input.GetKeyDown(KeyCode.Keypad1) || Input.GetKeyDown(KeyCode.End);

        private void RainWorld_Update(On.RainWorld.orig_Update orig, RainWorld self)
        {
            if (InputPause || (!paused && InputAdvance))
                paused = !paused;
            else if (InputAdvance)
                _frameAdvance = true;
            if (InputDelayedAdvance)
                _frameAdvanceTimer = 0.75f;
            if (_frameAdvanceTimer > 0f)
            {
                _frameAdvanceTimer -= Time.deltaTime;
                if(_frameAdvanceTimer <= 0f)
                {
                    _frameAdvanceTimer = 0f;
                    _frameAdvance = true;
                }
            }
            orig(self);
            ui.Update();
        }
    }
}
