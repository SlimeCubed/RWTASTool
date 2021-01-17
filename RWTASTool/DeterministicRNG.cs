using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using System.Reflection;
using MonoMod.RuntimeDetour;
using Random = UnityEngine.Random;
using SRandom = System.Random;

namespace RWTASTool
{
    public class DeterministicRNG : IDisposable
    {
        public static SRandom nonCriticalRNG = new SRandom();
        //public static SRandom backupRng = new SRandom();

        // GrafUpdate should be called a predictable number of times per frame

        private static NativeDetour[] _detours;
        private static bool _detoursOn;
        
        public DeterministicRNG(RainWorldGame game, int initialSeed)
        {
            On.RainWorldGame.RawUpdate += RainWorldGame_RawUpdate;
            
            nonCriticalRNG = new SRandom(initialSeed + int.MaxValue);

            if(_detours != null)
                ToggleDetours(false);

            Random.seed = initialSeed;

            if (_detours == null)
            {
                BindingFlags bf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                Type rc = typeof(Random);
                Type dc = typeof(DeterministicRNG);
                _detours = new NativeDetour[]
                {
                    new NativeDetour(rc.GetProperty("value", bf).GetGetMethod(), dc.GetMethod("Random_value", bf)),
                    new NativeDetour(rc.GetProperty("seed", bf).GetGetMethod() , dc.GetProperty("Random_seed", bf).GetGetMethod()),
                    new NativeDetour(rc.GetProperty("seed", bf).GetSetMethod() , dc.GetProperty("Random_seed", bf).GetSetMethod()),
                    new NativeDetour(rc.GetMethod("RandomRangeInt", bf), dc.GetMethod("Random_RandomRangeInt", bf)),
                    new NativeDetour(rc.GetMethod("RandomRange", bf, null, new Type[] { typeof(float), typeof(float) }, null), dc.GetMethod("Random_RangeFloat"))
                };
                _detoursOn = true;
            }
            ToggleDetours(false);
        }

        // Change the game's behavior when lagging
        // Instead of performing the same number of physics updates, it just slows down physics
        // This ensures a predictable amount of graphics updates per frame
        private float _savedTimeStacker = -1f;
        private FieldInfo _MainLoopProcess_myTimeStacker = typeof(MainLoopProcess).GetField("myTimeStacker", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private void RainWorldGame_RawUpdate(On.RainWorldGame.orig_RawUpdate orig, RainWorldGame self, float dt)
        {
            // Do not allow RNG to advance when paused
            ToggleDetours(self.pauseMenu != null);

            // It is also important that the timeStacker remains unmodified
            if(self.pauseMenu == null)
            {
                if (_savedTimeStacker == -1f)
                    _savedTimeStacker = (float)_MainLoopProcess_myTimeStacker.GetValue(self);
            } else
            {
                if (_savedTimeStacker != -1f)
                    _MainLoopProcess_myTimeStacker.SetValue(self, _savedTimeStacker);
            }

            orig(self, 1f / Application.targetFrameRate);
        }

        public static float Random_RangeFloat(float min, float max)
        {
            return Random_value() * (max - min) + min;
        }

        public static int Random_RandomRangeInt(int min, int max)
        {
            return nonCriticalRNG.Next(min, max);
        }

        public static int Random_seed
        {
            get => nonCriticalRNG.Next();
            set => nonCriticalRNG = new SRandom(value);
        }

        public static float Random_value()
        {
            return (float)nonCriticalRNG.NextDouble();
        }

        private void ToggleDetours(bool on)
        {
            if (on == _detoursOn) return;
            if (on)
            {
                _detoursOn = true;
                for (int i = 0; i < _detours.Length; i++)
                    _detours[i].Apply();
            } else
            {
                _detoursOn = false;
                for (int i = 0; i < _detours.Length; i++)
                    _detours[i].Undo();
            }
        }

        public void Dispose()
        {
            On.RainWorldGame.RawUpdate -= RainWorldGame_RawUpdate;
            ToggleDetours(false);
        }
    }
}
