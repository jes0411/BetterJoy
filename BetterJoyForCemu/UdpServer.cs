using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Numerics;
using System.Threading.Tasks;

namespace BetterJoyForCemu {
    class UdpServer {
        private const ushort MaxProtocolVersion = 1001;
        private const int packetSize = 1024;
        private const int reportSize = 100;

        private Socket udpSock;
        private uint serverId;
        private bool running;
        private Task receiveTask;

        private byte[] bufferReport;

        IList<Joycon> controllers;

        public MainForm form;

        public UdpServer(IList<Joycon> p) {
            controllers = p;
            bufferReport = GC.AllocateArray<byte>(reportSize, true);
        }

        enum MessageType {
            DSUC_VersionReq = 0x100000,
            DSUS_VersionRsp = 0x100000,
            DSUC_ListPorts = 0x100001,
            DSUS_PortInfo = 0x100001,
            DSUC_PadDataReq = 0x100002,
            DSUS_PadDataRsp = 0x100002,
        };
        class ClientRequestTimes {
            DateTime allPads;
            DateTime[] padIds;
            Dictionary<PhysicalAddress, DateTime> padMacs;

            public DateTime AllPadsTime { get { return allPads; } }
            public DateTime[] PadIdsTime { get { return padIds; } }
            public Dictionary<PhysicalAddress, DateTime> PadMacsTime { get { return padMacs; } }

            public ClientRequestTimes() {
                allPads = DateTime.MinValue;
                padIds = new DateTime[4];

                for (int i = 0; i < padIds.Length; i++)
                    padIds[i] = DateTime.MinValue;

                padMacs = new Dictionary<PhysicalAddress, DateTime>();
            }

            public void RequestPadInfo(byte regFlags, byte idToReg, PhysicalAddress macToReg) {
                if (regFlags == 0)
                    allPads = DateTime.UtcNow;
                else {
                    if ((regFlags & 0x01) != 0) //id valid
                    {
                        if (idToReg < padIds.Length)
                            padIds[idToReg] = DateTime.UtcNow;
                    }
                    if ((regFlags & 0x02) != 0) //mac valid
                    {
                        padMacs[macToReg] = DateTime.UtcNow;
                    }
                }
            }
        }

        private Dictionary<IPEndPoint, ClientRequestTimes> clients = new Dictionary<IPEndPoint, ClientRequestTimes>();

        private int BeginPacket(Span<byte> packetBuffer, ushort reqProtocolVersion = MaxProtocolVersion) {
            int currIdx = 0;
            packetBuffer[currIdx++] = (byte)'D';
            packetBuffer[currIdx++] = (byte)'S';
            packetBuffer[currIdx++] = (byte)'U';
            packetBuffer[currIdx++] = (byte)'S';

            BitConverter.TryWriteBytes(packetBuffer.Slice(currIdx, 2), (ushort)reqProtocolVersion);
            currIdx += 2;

            BitConverter.TryWriteBytes(packetBuffer.Slice(currIdx, 2), (ushort)(packetBuffer.Length - 16));
            currIdx += 2;

            packetBuffer.Slice(currIdx, 4).Clear(); //place for crc
            currIdx += 4;

            BitConverter.TryWriteBytes(packetBuffer.Slice(currIdx, 4), (uint)serverId);
            currIdx += 4;

            return currIdx;
        }

        private void FinishPacket(Span<byte> packetBuffer) {
            uint crcCalc = calculateCrc32(packetBuffer);
            BitConverter.TryWriteBytes(packetBuffer.Slice(8, 4), crcCalc);
        }

        private async Task SendPacket(IPEndPoint clientEP, byte[] usefulData, ushort reqProtocolVersion = MaxProtocolVersion) {
            byte[] packetData = new byte[usefulData.Length + 16];
            int currIdx = BeginPacket(packetData, reqProtocolVersion);
            Array.Copy(usefulData, 0, packetData, currIdx, usefulData.Length);
            FinishPacket(packetData);

            try {
                await udpSock.SendToAsync(clientEP, packetData.AsMemory());
            } catch (SocketException /*e*/) { }
        }

        private bool checkIncomingValidity(Span<byte> localMsg, out int currIdx) {
            currIdx = 0;

            if (localMsg.Length < 28)
                return false;

            if (localMsg[0] != 'D' || localMsg[1] != 'S' || localMsg[2] != 'U' || localMsg[3] != 'C')
                return false;

            currIdx += 4;

            uint protocolVer = BitConverter.ToUInt16(localMsg.Slice(currIdx, 2));
            currIdx += 2;

            if (protocolVer > MaxProtocolVersion)
                return false;

            uint packetSize = BitConverter.ToUInt16(localMsg.Slice(currIdx, 2));
            currIdx += 2;

            if (packetSize < 0)
                return false;

            packetSize += 16; //size of header
            if (packetSize > localMsg.Length)
                return false;

            uint crcValue = BitConverter.ToUInt32(localMsg.Slice(currIdx, 4));
            //zero out the crc32 in the packet once we got it since that's whats needed for calculation
            localMsg[currIdx++] = 0;
            localMsg[currIdx++] = 0;
            localMsg[currIdx++] = 0;
            localMsg[currIdx++] = 0;

            uint crcCalc = calculateCrc32(localMsg.Slice(0, (int)packetSize));
            if (crcValue != crcCalc)
                return false;

            return true;
        }

        private List<byte[]> ProcessIncoming(Span<byte> localMsg, IPEndPoint clientEP) {
            var replies = new List<byte[]>();

            int currIdx;
            if (!checkIncomingValidity(localMsg, out currIdx)) {
                return replies;
            }

            // uint clientId = BitConverter.ToUInt32(localMsg.Slice(currIdx, 4));
            currIdx += 4;

            uint messageType = BitConverter.ToUInt32(localMsg.Slice(currIdx, 4));
            currIdx += 4;

            if (messageType == (uint)MessageType.DSUC_VersionReq) {
                byte[] outputData = new byte[8];
                int outIdx = 0;
                Array.Copy(BitConverter.GetBytes((uint)MessageType.DSUS_VersionRsp), 0, outputData, outIdx, 4);
                outIdx += 4;
                Array.Copy(BitConverter.GetBytes((ushort)MaxProtocolVersion), 0, outputData, outIdx, 2);
                outIdx += 2;
                outputData[outIdx++] = 0;
                outputData[outIdx++] = 0;

                replies.Add(outputData);
            } else if (messageType == (uint)MessageType.DSUC_ListPorts) {
                // Requested information on gamepads - return MAC address
                int numPadRequests = BitConverter.ToInt32(localMsg.Slice(currIdx, 4));
                currIdx += 4;
                if (numPadRequests > 0) {
                    byte[] outputData = new byte[16];

                    lock (controllers) {
                        for (byte i = 0; i < numPadRequests; i++) {
                            byte currRequest = localMsg[currIdx + i];
                            if (currRequest < 0 || currRequest >= controllers.Count) {
                                continue;
                            }
                            Joycon padData = controllers[currRequest];

                            int outIdx = 0;
                            Array.Copy(BitConverter.GetBytes((uint)MessageType.DSUS_PortInfo), 0, outputData, outIdx, 4);
                            outIdx += 4;

                            outputData[outIdx++] = (byte)padData.PadId;
                            outputData[outIdx++] = (byte)padData.constate;
                            outputData[outIdx++] = (byte)padData.model;
                            outputData[outIdx++] = (byte)padData.connection;

                            var addressBytes = padData.PadMacAddress.GetAddressBytes();
                            if (addressBytes.Length == 6) {
                                outputData[outIdx++] = addressBytes[0];
                                outputData[outIdx++] = addressBytes[1];
                                outputData[outIdx++] = addressBytes[2];
                                outputData[outIdx++] = addressBytes[3];
                                outputData[outIdx++] = addressBytes[4];
                                outputData[outIdx++] = addressBytes[5];
                            } else {
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
            } else if (messageType == (uint)MessageType.DSUC_PadDataReq) {
                byte regFlags = localMsg[currIdx++];
                byte idToReg = localMsg[currIdx++];
                PhysicalAddress macToReg = null;
                {
                    byte[] macBytes = new byte[6];
                    localMsg.Slice(currIdx, macBytes.Length).CopyTo(macBytes);

                    macToReg = new PhysicalAddress(macBytes);
                }

                lock (clients) {
                    if (clients.ContainsKey(clientEP))
                        clients[clientEP].RequestPadInfo(regFlags, idToReg, macToReg);
                    else {
                        var clientTimes = new ClientRequestTimes();
                        clientTimes.RequestPadInfo(regFlags, idToReg, macToReg);
                        clients[clientEP] = clientTimes;
                    }
                }
            }
            return replies;
        }

        private async Task RunReceive() {
            var buffer = GC.AllocateArray<byte>(packetSize, true);
            var bufferMem = buffer.AsMemory();

            // Do processing, continually receiving from the socket
            while (running) {
                try {
                    var result = await udpSock.ReceiveFromAsync(bufferMem);

                    if (result is SocketReceiveFromResult recvResult) {
                        var receivedData = bufferMem.Slice(0, recvResult.ReceivedBytes);
                        IPEndPoint client = (IPEndPoint) recvResult.RemoteEndPoint;

                        List<byte[]> repliesData = ProcessIncoming(receivedData.Span, client);
                        if (repliesData.Count > 0) {
                            // We don't care in which order the replies are sent to the client
                            var tasks = repliesData.Select(async reply => {
                                await SendPacket(client, reply, 1001);
                            });
                            await Task.WhenAll(tasks);
                        }
                    } else {
                        break;
                    }
                } catch (SocketException) {
                    // We're done
                    break;
                }
            }
        }

        public void Start(IPAddress ip, int port = 26760) {
            if (running) {
                return;
            }

            if (!Boolean.Parse(ConfigurationManager.AppSettings["MotionServer"])) {
                form.AppendTextBox("Motion server is OFF.\r\n");
                return;
            }

            udpSock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            try {
                udpSock.Bind(new IPEndPoint(ip, port));
            } catch (SocketException /*e*/) {
                udpSock.Close();

                form.AppendTextBox("Could not start server. Make sure that only one instance of the program is running at a time and no other CemuHook applications are running.\r\n");
                return;
            }

            // Ignore ICMP
            uint IOC_IN = 0x80000000;
            uint IOC_VENDOR = 0x18000000;
            uint SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;
            udpSock.IOControl((int)SIO_UDP_CONNRESET, new byte[] { Convert.ToByte(false) }, null);

            byte[] randomBuf = new byte[4];
            new Random().NextBytes(randomBuf);
            serverId = BitConverter.ToUInt32(randomBuf, 0);

            running = true;
            form.AppendTextBox(String.Format("Starting server on {0}:{1}\r\n", ip.ToString(), port));

            receiveTask = Task.Run(RunReceive);
        }

        public void Stop() {
            if (!running) {
                return;
            }
            running = false;

            udpSock.Close();
            receiveTask.Wait(); // it rethrows exceptions
        }

        private bool ReportToBuffer(Joycon hidReport, Span<byte> outputData, ref int outIdx) {
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
            {
                var accel = hidReport.GetAccel();
                if (accel != Vector3.Zero) {
                    BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), accel.Y);
                    outIdx += 4;
                    BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), -accel.Z);
                    outIdx += 4;
                    BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), accel.X);
                    outIdx += 4;
                } else {
                    outIdx += 12;
                    Console.WriteLine("No accelerometer reported.");
                }
            }

            //gyroscope
            {
                var gyro = hidReport.GetGyro();
                if (gyro != Vector3.Zero) {
                    BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), gyro.Y);
                    outIdx += 4;
                    BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), gyro.Z);
                    outIdx += 4;
                    BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), gyro.X);
                    outIdx += 4;
                } else {
                    outIdx += 12;
                    Console.WriteLine("No gyroscope reported.");
                }
            }

            return true;
        }

        public void NewReportIncoming(Joycon hidReport) {
            if (!running) {
                return;
            }
            var clientsList = new List<IPEndPoint>();
            var now = DateTime.UtcNow;
            lock (clients) {
                var clientsToDelete = new List<IPEndPoint>();

                foreach (var cl in clients) {
                    const double TimeoutLimit = 5;

                    if ((now - cl.Value.AllPadsTime).TotalSeconds < TimeoutLimit)
                        clientsList.Add(cl.Key);
                    else if ((hidReport.PadId >= 0 && hidReport.PadId <= 3) &&
                             (now - cl.Value.PadIdsTime[(byte)hidReport.PadId]).TotalSeconds < TimeoutLimit)
                        clientsList.Add(cl.Key);
                    else if (cl.Value.PadMacsTime.ContainsKey(hidReport.PadMacAddress) &&
                             (now - cl.Value.PadMacsTime[hidReport.PadMacAddress]).TotalSeconds < TimeoutLimit)
                        clientsList.Add(cl.Key);
                    else //check if this client is totally dead, and remove it if so
                    {
                        bool clientOk = false;
                        for (int i = 0; i < cl.Value.PadIdsTime.Length; i++) {
                            var dur = (now - cl.Value.PadIdsTime[i]).TotalSeconds;
                            if (dur < TimeoutLimit) {
                                clientOk = true;
                                break;
                            }
                        }
                        if (!clientOk) {
                            foreach (var dict in cl.Value.PadMacsTime) {
                                var dur = (now - dict.Value).TotalSeconds;
                                if (dur < TimeoutLimit) {
                                    clientOk = true;
                                    break;
                                }
                            }

                            if (!clientOk)
                                clientsToDelete.Add(cl.Key);
                        }
                    }
                }

                foreach (var delCl in clientsToDelete) {
                    clients.Remove(delCl);
                }
                clientsToDelete.Clear();
                clientsToDelete = null;
            }

            if (clientsList.Count <= 0)
                return;

            var outputData = bufferReport.AsSpan();
            outputData.Clear();

            int outIdx = BeginPacket(outputData, 1001);
            BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), (uint)MessageType.DSUS_PadDataRsp);
            outIdx += 4;

            outputData[outIdx++] = (byte)hidReport.PadId;
            outputData[outIdx++] = (byte)hidReport.constate;
            outputData[outIdx++] = (byte)hidReport.model;
            outputData[outIdx++] = (byte)hidReport.connection;
            {
                ReadOnlySpan<byte> padMac = hidReport.PadMacAddress.GetAddressBytes();
                for (int i = 0; i < padMac.Length; ++i) {
                    outputData[outIdx++] = padMac[i];
                }
            }

            outputData[outIdx++] = (byte)hidReport.battery;
            outputData[outIdx++] = 1;

            BitConverter.TryWriteBytes(outputData.Slice(outIdx, 4), hidReport.packetCounter);
            outIdx += 4;

            if (!ReportToBuffer(hidReport, outputData, ref outIdx))
                return;

            FinishPacket(outputData);

            // Send in parallel to all clients
            ReadOnlyMemory<byte> bufferMem = bufferReport.AsMemory();
            var tasks = clientsList.Select(async client => {
                try {
                    await udpSock.SendToAsync(client, bufferMem);
                } catch (SocketException) { }
            });
            Task.WhenAll(tasks).Wait();
        }

        private static uint calculateCrc32(ReadOnlySpan<byte> data) {
            byte[] crc = System.IO.Hashing.Crc32.Hash(data);
            return BitConverter.ToUInt32(crc);
        }
    }
}
