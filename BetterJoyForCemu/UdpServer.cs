﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO.Hashing;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using BetterJoyForCemu.Memory;

namespace BetterJoyForCemu
{
    internal class UdpServer
    {
        private enum MessageType
        {
            DSUC_VersionReq = 0x100000,
            DSUS_VersionRsp = 0x100000,
            DSUC_ListPorts = 0x100001,
            DSUS_PortInfo = 0x100001,
            DSUC_PadDataReq = 0x100002,
            DSUS_PadDataRsp = 0x100002
        }

        private const ushort MaxProtocolVersion = 1001;
        private const int PacketSize = 1024;
        private const int ReportSize = 100;
        private const int ControllerTimeoutSeconds = 5;
        private readonly Dictionary<IPEndPoint, ClientRequestTimes> _clients;

        private readonly IList<Joycon> _controllers;
        private Task _receiveTask;
        private bool _running;
        private uint _serverId;

        private Socket _udpSock;

        public MainForm Form;

        public UdpServer(IList<Joycon> p)
        {
            _controllers = p;
            _clients = new Dictionary<IPEndPoint, ClientRequestTimes>();
        }

        private int BeginPacket(Span<byte> packetBuffer, ushort reqProtocolVersion = MaxProtocolVersion)
        {
            var currIdx = 0;
            packetBuffer[currIdx++] = (byte)'D';
            packetBuffer[currIdx++] = (byte)'S';
            packetBuffer[currIdx++] = (byte)'U';
            packetBuffer[currIdx++] = (byte)'S';

            BitConverter.TryWriteBytes(packetBuffer.Slice(currIdx, 2), reqProtocolVersion);
            currIdx += 2;

            BitConverter.TryWriteBytes(packetBuffer.Slice(currIdx, 2), (ushort)(packetBuffer.Length - 16));
            currIdx += 2;

            packetBuffer.Slice(currIdx, 4).Clear(); //place for crc
            currIdx += 4;

            BitConverter.TryWriteBytes(packetBuffer.Slice(currIdx, 4), _serverId);
            currIdx += 4;

            return currIdx;
        }

        private void FinishPacket(Span<byte> packetBuffer)
        {
            CalculateCrc32(packetBuffer, packetBuffer.Slice(8, 4));
        }

        private async Task SendPacket(
            IPEndPoint clientEP,
            byte[] usefulData,
            ushort reqProtocolVersion = MaxProtocolVersion
        )
        {
            var size = usefulData.Length + 16;
            using var packetDataBuffer = MemoryPool<byte>.Shared.RentCleared(size);
            var packetDataMem = packetDataBuffer.Memory;

            // needed to use span in async function
            void makePacket()
            {
                var packetData = packetDataMem.Span;

                var currIdx = BeginPacket(packetData, reqProtocolVersion);
                usefulData.AsSpan().CopyTo(packetData.Slice(currIdx));
                FinishPacket(packetData);
            }

            makePacket();

            try
            {
                await _udpSock.SendToAsync(clientEP, packetDataMem);
            }
            catch (SocketException /*e*/) { }
        }

        private static bool CheckIncomingValidity(Span<byte> localMsg, out int currIdx)
        {
            currIdx = 0;

            if (localMsg.Length < 28)
            {
                return false;
            }

            if (localMsg[0] != 'D' || localMsg[1] != 'S' || localMsg[2] != 'U' || localMsg[3] != 'C')
            {
                return false;
            }

            currIdx += 4;

            uint protocolVer = BitConverter.ToUInt16(localMsg.Slice(currIdx, 2));
            currIdx += 2;

            if (protocolVer > MaxProtocolVersion)
            {
                return false;
            }

            uint packetSize = BitConverter.ToUInt16(localMsg.Slice(currIdx, 2));
            currIdx += 2;

            packetSize += 16; //size of header
            if (packetSize > localMsg.Length)
            {
                return false;
            }

            var crcValue = BitConverter.ToUInt32(localMsg.Slice(currIdx, 4));

            //zero out the crc32 in the packet once we got it since that's whats needed for calculation
            localMsg[currIdx++] = 0;
            localMsg[currIdx++] = 0;
            localMsg[currIdx++] = 0;
            localMsg[currIdx++] = 0;

            var crcCalc = CalculateCrc32(localMsg.Slice(0, (int)packetSize));
            if (crcValue != crcCalc)
            {
                return false;
            }

            return true;
        }

        private List<byte[]> ProcessIncoming(Span<byte> localMsg, IPEndPoint clientEP)
        {
            var replies = new List<byte[]>();

            if (!CheckIncomingValidity(localMsg, out var currIdx))
            {
                return replies;
            }

            // uint clientId = BitConverter.ToUInt32(localMsg.Slice(currIdx, 4));
            currIdx += 4;

            var messageType = BitConverter.ToUInt32(localMsg.Slice(currIdx, 4));
            currIdx += 4;

            switch (messageType)
            {
                case (uint)MessageType.DSUC_VersionReq:
                {
                    var outputData = new byte[8];
                    var outIdx = 0;
                    Array.Copy(BitConverter.GetBytes((uint)MessageType.DSUS_VersionRsp), 0, outputData, outIdx, 4);
                    outIdx += 4;
                    Array.Copy(BitConverter.GetBytes(MaxProtocolVersion), 0, outputData, outIdx, 2);
                    outIdx += 2;
                    outputData[outIdx++] = 0;
                    outputData[outIdx++] = 0;

                    replies.Add(outputData);
                    break;
                }
                case (uint)MessageType.DSUC_ListPorts:
                {
                    // Requested information on gamepads - return MAC address
                    var numPadRequests = BitConverter.ToInt32(localMsg.Slice(currIdx, 4));
                    currIdx += 4;
                    if (numPadRequests > 0)
                    {
                        var outputData = new byte[16];

                        lock (_controllers)
                        {
                            for (byte i = 0; i < numPadRequests; i++)
                            {
                                var currRequest = localMsg[currIdx + i];
                                if (currRequest >= _controllers.Count)
                                {
                                    continue;
                                }

                                var padData = _controllers[currRequest];

                                var outIdx = 0;
                                Array.Copy(
                                    BitConverter.GetBytes((uint)MessageType.DSUS_PortInfo),
                                    0,
                                    outputData,
                                    outIdx,
                                    4
                                );
                                outIdx += 4;

                                outputData[outIdx++] = (byte)padData.PadId;
                                outputData[outIdx++] = (byte)padData.constate;
                                outputData[outIdx++] = (byte)padData.model;
                                outputData[outIdx++] = (byte)padData.connection;

                                var addressBytes = padData.PadMacAddress.GetAddressBytes();
                                if (addressBytes.Length == 6)
                                {
                                    outputData[outIdx++] = addressBytes[0];
                                    outputData[outIdx++] = addressBytes[1];
                                    outputData[outIdx++] = addressBytes[2];
                                    outputData[outIdx++] = addressBytes[3];
                                    outputData[outIdx++] = addressBytes[4];
                                    outputData[outIdx++] = addressBytes[5];
                                }
                                else
                                {
                                    outputData[outIdx++] = 0;
                                    outputData[outIdx++] = 0;
                                    outputData[outIdx++] = 0;
                                    outputData[outIdx++] = 0;
                                    outputData[outIdx++] = 0;
                                    outputData[outIdx++] = 0;
                                }

                                outputData[outIdx++] = (byte)padData.battery;
                                outputData[outIdx++] = 0;

                                replies.Add(outputData);
                            }
                        }
                    }

                    break;
                }
                case (uint)MessageType.DSUC_PadDataReq:
                {
                    var regFlags = localMsg[currIdx++];
                    var idToReg = localMsg[currIdx++];
                    PhysicalAddress macToReg;
                    {
                        var macBytes = new byte[6];
                        localMsg.Slice(currIdx, macBytes.Length).CopyTo(macBytes);

                        macToReg = new PhysicalAddress(macBytes);
                    }

                    lock (_clients)
                    {
                        if (_clients.TryGetValue(clientEP, out var client))
                        {
                            client.RequestPadInfo(regFlags, idToReg, macToReg);
                        }
                        else
                        {
                            var clientTimes = new ClientRequestTimes();
                            clientTimes.RequestPadInfo(regFlags, idToReg, macToReg);
                            _clients[clientEP] = clientTimes;
                        }
                    }

                    break;
                }
            }

            return replies;
        }

        private async Task RunReceive()
        {
            var buffer = GC.AllocateArray<byte>(PacketSize, true);
            var bufferMem = buffer.AsMemory();

            // Do processing, continually receiving from the socket
            while (_running)
            {
                try
                {
                    var receiveResult = await _udpSock.ReceiveFromAsync(bufferMem);
                    var client = (IPEndPoint)receiveResult.RemoteEndPoint;

                    var repliesData = ProcessIncoming(buffer.AsSpan(0, receiveResult.ReceivedBytes), client);
                    if (repliesData.Count <= 0)
                    {
                        continue;
                    }

                    // We don't care in which order the replies are sent to the client
                    var tasks = repliesData.Select(async reply => { await SendPacket(client, reply); });
                    await Task.WhenAll(tasks);
                }
                catch (SocketException)
                {
                    // We're done
                    break;
                }
            }
        }

        public void Start(IPAddress ip, int port = 26760)
        {
            if (_running)
            {
                return;
            }

            if (!bool.Parse(ConfigurationManager.AppSettings["MotionServer"]))
            {
                Form.AppendTextBox("Motion server is OFF.");
                return;
            }

            _udpSock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            try
            {
                _udpSock.Bind(new IPEndPoint(ip, port));
            }
            catch (SocketException /*e*/)
            {
                _udpSock.Close();

                Form.AppendTextBox(
                    "Could not start server. Make sure that only one instance of the program is running at a time and no other CemuHook applications are running."
                );
                return;
            }

            // Ignore ICMP
            var IOC_IN = 0x80000000;
            uint IOC_VENDOR = 0x18000000;
            var SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;
            _udpSock.IOControl((int)SIO_UDP_CONNRESET, new[] { Convert.ToByte(false) }, null);

            var randomBuf = new byte[4];
            new Random().NextBytes(randomBuf);
            _serverId = BitConverter.ToUInt32(randomBuf, 0);

            _running = true;
            Form.AppendTextBox($"Starting server on {ip}:{port}.");

            _receiveTask = Task.Run(RunReceive);
        }

        public void Stop()
        {
            if (!_running)
            {
                return;
            }

            _running = false;

            _udpSock.Close();
            _receiveTask.Wait(); // it rethrows exceptions
        }

        private void ReportToBuffer(Joycon hidReport, Span<byte> outputData, ref int outIdx)
        {
            /* Commented because we only care about the gyroscope and accelerometer
            var ds4 = Joycon.MapToDualShock4Input(hidReport);

            outputData[outIdx] = 0;

            if (ds4.dPad == Controller.DpadDirection.West || ds4.dPad == Controller.DpadDirection.Northwest || ds4.dPad == Controller.DpadDirection.Southwest) outputData[outIdx] |= 0x80;
            if (ds4.dPad == Controller.DpadDirection.South || ds4.dPad == Controller.DpadDirection.Southwest || ds4.dPad == Controller.DpadDirection.Southeast) outputData[outIdx] |= 0x40;
            if (ds4.dPad == Controller.DpadDirection.East || ds4.dPad == Controller.DpadDirection.Northeast || ds4.dPad == Controller.DpadDirection.Southeast) outputData[outIdx] |= 0x20;
            if (ds4.dPad == Controller.DpadDirection.North || ds4.dPad == Controller.DpadDirection.Northwest || ds4.dPad == Controller.DpadDirection.Northeast) outputData[outIdx] |= 0x10;

            if (ds4.options) outputData[outIdx] |= 0x08;
            if (ds4.thumb_right) outputData[outIdx] |= 0x04;
            if (ds4.thumb_left) outputData[outIdx] |= 0x02;
            if (ds4.share) outputData[outIdx] |= 0x01;

            outputData[++outIdx] = 0;

            if (ds4.square) outputData[outIdx] |= 0x80;
            if (ds4.cross) outputData[outIdx] |= 0x40;
            if (ds4.circle) outputData[outIdx] |= 0x20;
            if (ds4.triangle) outputData[outIdx] |= 0x10;

            if (ds4.shoulder_right) outputData[outIdx] |= 0x08;
            if (ds4.shoulder_left) outputData[outIdx] |= 0x04;
            if (ds4.trigger_right_value == Byte.MaxValue) outputData[outIdx] |= 0x02;
            if (ds4.trigger_left_value == Byte.MaxValue) outputData[outIdx] |= 0x01;

            outputData[++outIdx] = ds4.ps ? (byte)1 : (byte)0;
            outputData[++outIdx] = ds4.touchpad ? (byte)1 : (byte)0;

            outputData[++outIdx] = ds4.thumb_left_x;
            outputData[++outIdx] = ds4.thumb_left_y;
            outputData[++outIdx] = ds4.thumb_right_x;
            outputData[++outIdx] = ds4.thumb_right_y;

            //we don't have analog buttons so just use the Button enums (which give either 0 or 0xFF)
            outputData[++outIdx] = (ds4.dPad == Controller.DpadDirection.West || ds4.dPad == Controller.DpadDirection.Northwest || ds4.dPad == Controller.DpadDirection.Southwest) ? (byte)0xFF : (byte)0;
            outputData[++outIdx] = (ds4.dPad == Controller.DpadDirection.South || ds4.dPad == Controller.DpadDirection.Southwest || ds4.dPad == Controller.DpadDirection.Southeast) ? (byte)0xFF : (byte)0;
            outputData[++outIdx] = (ds4.dPad == Controller.DpadDirection.East || ds4.dPad == Controller.DpadDirection.Northeast || ds4.dPad == Controller.DpadDirection.Southeast) ? (byte)0xFF : (byte)0;
            outputData[++outIdx] = (ds4.dPad == Controller.DpadDirection.North || ds4.dPad == Controller.DpadDirection.Northwest || ds4.dPad == Controller.DpadDirection.Northeast) ? (byte)0xFF : (byte)0; ;

            outputData[++outIdx] = ds4.square ? (byte)0xFF : (byte)0;
            outputData[++outIdx] = ds4.cross ? (byte)0xFF : (byte)0;
            outputData[++outIdx] = ds4.circle ? (byte)0xFF : (byte)0;
            outputData[++outIdx] = ds4.triangle ? (byte)0xFF : (byte)0;

            outputData[++outIdx] = ds4.shoulder_right ? (byte)0xFF : (byte)0;
            outputData[++outIdx] = ds4.shoulder_left ? (byte)0xFF : (byte)0;

            outputData[++outIdx] = ds4.trigger_right_value;
            outputData[++outIdx] = ds4.trigger_left_value;

            outIdx++;

            //DS4 only: touchpad points
            for (int i = 0; i < 2; i++) {
                outIdx += 6;
            }
            */

            outIdx += 32;

            //motion timestamp
            BitConverter.TryWriteBytes(outputData.Slice(outIdx, 8), hidReport.Timestamp);
            outIdx += 8;

            //accelerometer
            var accel = hidReport.GetAccel();
            if (accel != Vector3.Zero)
            {
                BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), accel.Y);
                outIdx += 4;
                BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), -accel.Z);
                outIdx += 4;
                BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), accel.X);
                outIdx += 4;
            }
            else
            {
                outIdx += 12;
                Console.WriteLine("No accelerometer reported.");
            }

            //gyroscope
            var gyro = hidReport.GetGyro();
            if (gyro != Vector3.Zero)
            {
                BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), gyro.Y);
                outIdx += 4;
                BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), gyro.Z);
                outIdx += 4;
                BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), gyro.X);
                outIdx += 4;
            }
            else
            {
                outIdx += 12;
                Console.WriteLine("No gyroscope reported.");
            }
        }

        private static bool IsControllerTimedout(DateTime current, DateTime last)
        {
            return (current - last).TotalSeconds >= ControllerTimeoutSeconds;
        }

        public void NewReportIncoming(Joycon hidReport)
        {
            if (!_running)
            {
                return;
            }

            var nbClients = 0;
            var now = DateTime.UtcNow;

            Monitor.Enter(_clients);
            using var relevantClientsBuffer = MemoryPool<IPEndPoint>.Shared.RentCleared(_clients.Count);
            var relevantClientsMemory = relevantClientsBuffer.Memory;
            var relevantClients = relevantClientsMemory.Span;

            try
            {
                foreach (var client in _clients)
                {
                    if (!IsControllerTimedout(now, client.Value.AllPadsTime))
                    {
                        relevantClients[nbClients++] = client.Key;
                    }
                    else if (hidReport.PadId is >= 0 and <= 3 &&
                             !IsControllerTimedout(now, client.Value.PadIdsTime[(byte)hidReport.PadId]))
                    {
                        relevantClients[nbClients++] = client.Key;
                    }
                    else if (client.Value.PadMacsTime.ContainsKey(hidReport.PadMacAddress) &&
                             !IsControllerTimedout(now, client.Value.PadMacsTime[hidReport.PadMacAddress]))
                    {
                        relevantClients[nbClients++] = client.Key;
                    }
                    else
                    {
                        //check if this client is totally dead, and remove it if so
                        var clientOk = false;
                        foreach (var padIdTime in client.Value.PadIdsTime)
                        {
                            if (!IsControllerTimedout(now, padIdTime))
                            {
                                clientOk = true;
                                break;
                            }
                        }

                        if (clientOk)
                        {
                            continue;
                        }

                        foreach (var dict in client.Value.PadMacsTime)
                        {
                            if (!IsControllerTimedout(now, dict.Value))
                            {
                                clientOk = true;
                                break;
                            }
                        }

                        if (!clientOk)
                        {
                            _clients.Remove(client.Key);
                        }
                    }
                }
            }
            finally
            {
                Monitor.Exit(_clients);
            }

            if (nbClients <= 0)
            {
                return;
            }

            relevantClients = relevantClients.Slice(0, nbClients);

            using var reportBuffer = MemoryPool<byte>.Shared.RentCleared(ReportSize);
            var reportBufferMem = reportBuffer.Memory;
            var outputData = reportBufferMem.Span;

            var outIdx = BeginPacket(outputData);
            BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), (uint)MessageType.DSUS_PadDataRsp);
            outIdx += 4;

            outputData[outIdx++] = (byte)hidReport.PadId;
            outputData[outIdx++] = (byte)hidReport.constate;
            outputData[outIdx++] = (byte)hidReport.model;
            outputData[outIdx++] = (byte)hidReport.connection;
            {
                ReadOnlySpan<byte> padMac = hidReport.PadMacAddress.GetAddressBytes();
                foreach (var number in padMac)
                {
                    outputData[outIdx++] = number;
                }
            }

            outputData[outIdx++] = (byte)hidReport.battery;
            outputData[outIdx++] = 1;

            BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), hidReport.packetCounter);
            outIdx += 4;

            ReportToBuffer(hidReport, outputData, ref outIdx);
            FinishPacket(outputData);

            foreach (var client in relevantClients)
            {
                _udpSock.SendTo(outputData, client);
            }
        }

        private static int CalculateCrc32(ReadOnlySpan<byte> data, Span<byte> crc)
        {
            return Crc32.Hash(data, crc);
        }

        private static uint CalculateCrc32(ReadOnlySpan<byte> data)
        {
            Span<byte> crc = stackalloc byte[4];
            Crc32.Hash(data, crc);
            return BitConverter.ToUInt32(crc);
        }

        private class ClientRequestTimes
        {
            public ClientRequestTimes()
            {
                AllPadsTime = DateTime.MinValue;
                PadIdsTime = new DateTime[4];

                for (var i = 0; i < PadIdsTime.Length; i++)
                {
                    PadIdsTime[i] = DateTime.MinValue;
                }

                PadMacsTime = new Dictionary<PhysicalAddress, DateTime>();
            }

            public DateTime AllPadsTime { get; private set; }
            public DateTime[] PadIdsTime { get; }
            public Dictionary<PhysicalAddress, DateTime> PadMacsTime { get; }

            public void RequestPadInfo(byte regFlags, byte idToReg, PhysicalAddress macToReg)
            {
                var now = DateTime.UtcNow;

                if (regFlags == 0)
                {
                    AllPadsTime = now;
                }
                else
                {
                    //id valid
                    if ((regFlags & 0x01) != 0 && idToReg < PadIdsTime.Length)
                    {
                        PadIdsTime[idToReg] = now;
                    }

                    //mac valid
                    if ((regFlags & 0x02) != 0)
                    {
                        PadMacsTime[macToReg] = now;
                    }
                }
            }
        }
    }
}
