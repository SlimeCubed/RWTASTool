using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.IO.Pipes;
using UnityEngine;
using System.Threading;

namespace RWTASTool
{
    public class TasIPC : IDisposable
    {
        public static string pipeName = "RWTasTool";
        private Thread _ipcThread;

        public enum ServerboundCode : byte
        {
            RequestInputs,
            SetInputs
        }

        // Currently unused, since the mod will reply immediately when possible
        public enum ClientboundCode : byte
        {
        }

        public TasIPC()
        {
            _ipcThread = new Thread(ServerThread);
        }
        
        public void ServerThread()
        {
            using (NamedPipeServerStream pipe = new NamedPipeServerStream(pipeName))
            {
                while (true)
                {
                    try
                    {
                        pipe.WaitForConnection();
                        Log("Client connected");

                        // Handshake!
                        // The mod writes first, if the external editor can handle this version then it can send this string back
                        WriteString(pipe, TasMod.version);
                        string ipcVersion = ReadString(pipe);

                        if (ipcVersion != TasMod.version) throw new FormatException("External editor protocol has a mismatched version.");

                        int code = pipe.ReadByte();
                        if (code >= 0)
                        {
                            switch ((ServerboundCode)code)
                            {
                                case ServerboundCode.RequestInputs:
                                    ClientGetInputs(pipe);
                                    break;
                                case ServerboundCode.SetInputs:
                                    ClientSetInputs(pipe);
                                    break;
                                default:
                                    throw new InvalidOperationException("Invalid packet code (" + code + ") sent to server.");
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError(new Exception("Failed", e));
                    }

                    Thread.Sleep(1);
                }
            }
        }

        public void ClientGetInputs(NamedPipeServerStream pipe)
        {
            TasUI.InputFrame f = new TasUI.InputFrame();
            TasUI.queueLock.EnterReadLock();
            try
            {
                pipe.Write(BitConverter.GetBytes(TasUI.main.inputQueue.Count), 0, 4);
                for (int readIndex = 0; readIndex < TasUI.main.inputQueue.Count; readIndex++)
                {
                    f.Load(TasUI.main.inputQueue[readIndex]);
                    f.ToStream(pipe);
                }
            }
            finally
            {
                TasUI.queueLock.ExitReadLock();
            }
        }

        public void ClientSetInputs(NamedPipeServerStream pipe)
        {
            TasUI.queueLock.EnterWriteLock();
            try
            {
                TasUI.main.inputQueue.Clear();
                TasUI.main.queueIndex = 0;
                TasUI.main.repeatIndex = 0;
                TasUI.InputFrame f = new TasUI.InputFrame();
                List<TasUI.TasInputPackage> inputBuffer = new List<TasUI.TasInputPackage>();
                byte[] buffer = new byte[4];
                pipe.Read(buffer, 0, 4);
                int len = BitConverter.ToInt32(buffer, 0);
                for (int i = 0; i < len; i++)
                    if (f.FromStream(pipe))
                        inputBuffer.Add(f.ToInputs());
                    else
                        throw new EndOfStreamException("End of stream encountered where input sequence was expected.");
                TasUI.main.inputQueue.AddRange(inputBuffer);
                TasUI.main.UpdateFrameLabels();
            } finally
            {
                TasUI.queueLock.ExitWriteLock();
            }
        }
        
        private string ReadString(NamedPipeServerStream pipe)
        {
            int len = pipe.ReadByte();
            if (len == -1) throw new EndOfStreamException("End of stream encountered where string data was expected.");
            byte[] buffer = new byte[len];
            if (pipe.Read(buffer, 0, len) != len) throw new EndOfStreamException("End of stream encountered where string data was expected.");
            return Encoding.UTF8.GetString(buffer, 0, len);
        }

        private int WriteString(NamedPipeServerStream pipe, string str)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(str);
            if (buffer.Length >= 255) throw new ArgumentException("String data must be less than 256 bytes in length.");
            pipe.WriteByte((byte)buffer.Length);
            pipe.Write(buffer, 0, buffer.Length);
            return buffer.Length;
        }

        public static void Log(string msg)
        {
            Debug.Log("[RWTAS IPC] " + msg);
        }

        public void Dispose()
        {
            _ipcThread.Abort();
        }
    }
}
