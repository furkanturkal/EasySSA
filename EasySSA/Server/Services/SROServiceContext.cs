﻿#region License
// ====================================================
// EasySSA Copyright(C) 2017 Furkan Türkal
// This program comes with ABSOLUTELY NO WARRANTY; This is free software,
// and you are welcome to redistribute it under certain conditions; See
// file LICENSE, which is part of this source code package, for details.
// ====================================================
#endregion

using System;
using System.Net.Sockets;
using System.Collections.Generic;

using EasySSA.SSA;
using EasySSA.Common;
using EasySSA.Services;
using EasySSA.Packets;
using EasySSA.Core.Network;

namespace EasySSA.Server.Services {
    public sealed class SROServiceContext : IDisposable {

        public bool IsRunning { get; private set; }

        public DateTime StartTime { get; private set; }

        private SROServiceComponent ServiceComponent;

        private Client Client;

        private Socket m_clientSocket;
        private Socket m_serviceSocket;

        private Security m_localSecurity;
        private Security m_serviceSecurity;

        private TransferBuffer m_localTransferBuffer;
        private TransferBuffer m_serviceTransferBuffer;

        private List<Packet> m_localRecevivePackets;
        private List<Packet> m_serviceRecevivePackets;

        private List<KeyValuePair<TransferBuffer, Packet>> m_localSendPackets;
        private List<KeyValuePair<TransferBuffer, Packet>> m_serviceSendPackets;

        private int TotalPacketCount = 0;

        byte[] m_localBuffer = new byte[8192];
        byte[] m_serviceBuffer = new byte[8192];

        private bool m_wasDisposed = false;

        internal protected readonly object LOCK = new object();

        public SROServiceContext(Client client, SROServiceComponent serviceComponent) {
            this.ServiceComponent = serviceComponent;
            this.Client = client;
            
            this.m_clientSocket = this.Client.Socket;

            this.m_localSecurity = this.Client.Security;
            this.m_serviceSecurity = new Security();

            this.m_localTransferBuffer = this.Client.TransferBuffer;
            this.m_serviceTransferBuffer = new TransferBuffer(0x10000, 0, 0);

            this.m_localSecurity.GenerateSecurity(true, true, true);

            this.IsRunning = false;
        }

        ~SROServiceContext() {
            Dispose(false);
        }

        public void DOBind(Action<SocketError> callback = null) {
            this.m_serviceSocket = NetworkHelper.TryConnect(this.ServiceComponent.ServiceEndPoint, this.ServiceComponent.RedirectTimeout, callback);
        }

        public bool Start() {
            if (m_serviceSocket == null) {
                Stop();
                return false;
            }

            this.IsRunning = true;

            this.DoRecvFromClient();
            this.DoRecvFromModule();

            return true;

        }

        public bool Stop() {
            bool flag1 = false;
            bool flag2 = false;

            try {
                this.Client.Disconnect();
                if (!this.Client.IsConnected()) {
                    flag1 = true;
                    this.Client.Dispose();
                }
            } catch { }

            try {
                this.m_serviceSocket.Close();
                if (!this.m_serviceSocket.Connected) {
                    flag2 = true;
                }
            } catch { }

            if (flag1 && flag2) {
                this.IsRunning = false;
                return true;
            }

            return false;
        }

        public void Disconnect(SROServiceContext server, ClientDisconnectType disconnectType) {
            if (this.ServiceComponent.OnClientDisconnected != null) {
                this.ServiceComponent.OnClientDisconnected(this.Client, disconnectType);
            }
        }

        private void HandleAndTransferResult(Packet packet, PacketSocketType direction, PacketResult result) {

            Security security = (direction == PacketSocketType.CLIENT) ? this.m_localSecurity : this.m_serviceSecurity;

            PacketOperationType operation = result.ResultType;
            PacketResult.PacketResultInfo resultInfo = result.ResultInfo;

            switch (operation) {
                case PacketOperationType.DISCONNECT:
                    if (resultInfo != null) {
                        PacketResult.PacketDisconnectResultInfo disconnect = resultInfo as PacketResult.PacketDisconnectResultInfo;
                        if(disconnect != null) {
                            if (!string.IsNullOrEmpty(disconnect.Notice)) {
                                this.SendMessage(MessageType.NOTICE, string.Empty, disconnect.Notice.Trim());
                            }
                            if (disconnect.DisconnectReason != null) {
                                int id = Convert.ToInt32(disconnect.DisconnectReason);
                                //TODO: Complete
                            }

                            this.Stop();
                            this.Disconnect(this, ClientDisconnectType.PACKET_OPERATION_DISCONNECT);
                        }
                    }
                    break;
                case PacketOperationType.REPLACE:
                    if (resultInfo != null) {
                        PacketResult.PacketReplaceResultInfo replace = resultInfo as PacketResult.PacketReplaceResultInfo;
                        if (replace != null) {
                            if (replace.Packet == packet) {
                                replace.ReplaceWith.ForEach(packets =>
                                {
                                    security.Send(packets);
                                });
                            }
                        }
                    }
                    break;
                case PacketOperationType.INJECT:
                    if (resultInfo != null) {
                        PacketResult.PacketInjectResultInfo inject = resultInfo as PacketResult.PacketInjectResultInfo;
                        if (inject != null) {
                            if (inject.AfterPacket) {
                                security.Send(packet);
                            }

                            if (inject.Packet == packet) {
                                inject.InjectWith.ForEach(packets => {
                                    security.Send(packets);
                                });
                            }

                            if (!inject.AfterPacket) {
                                security.Send(packet);
                            }
                        }
                    }
                    break;
                case PacketOperationType.IGNORE:
                    break;
                case PacketOperationType.BLOCK_IP:
                    this.Stop();
                    this.Disconnect(this, ClientDisconnectType.PACKET_OPERATION_DISCONNECT);
                    break;
                case PacketOperationType.NOTHING:
                    security.Send(packet);
                    break;
                default:
                    security.Send(packet);
                    break;
            }

            if (direction == PacketSocketType.CLIENT) {
                TransferToClient();
            } else if (direction == PacketSocketType.SERVER) {
                TransferToService();
            }

        }

        #region Receivers

        private void OnRecvFromClient(IAsyncResult iar) {
            lock (this.LOCK) {
                try {
                    int recvCount = this.m_clientSocket.EndReceive(iar);

                    if (recvCount > 0) {
                        this.m_localSecurity.Recv(m_localBuffer, 0, recvCount);

                        m_localRecevivePackets = this.m_localSecurity.TransferIncoming();
                        int receiveCount = m_localRecevivePackets.Count;

                        if (m_localRecevivePackets != null) {

                            TotalPacketCount += receiveCount;

                            for (int i = 0; i < receiveCount; i++) {
                                Packet packet = m_localRecevivePackets[i];

                                int packetLenght = packet.GetBytes().Length;

                                if (packet.Opcode == 0x9000 || packet.Opcode == 0x5000 || packet.Opcode == 0x2001) {
                                    continue;
                                }

                                if(this.ServiceComponent.OnPacketReceived != null) {

                                    PacketResult result = this.ServiceComponent.OnPacketReceived(this.Client, new SROPacket(packet), PacketSocketType.CLIENT);

                                    HandleAndTransferResult(packet, PacketSocketType.SERVER, result);
                                   
                                }

                            }

                        }
                    } else {
                        this.Stop();
                        this.Disconnect(this, ClientDisconnectType.ONRECV_FROM_CLIENT_SIZE_ZERO);
                        return;
                    }

                    this.TransferToService();
                    this.DoRecvFromClient();
                } catch {
                    this.Stop();
                    this.Disconnect(this, ClientDisconnectType.ONRECV_FROM_CLIENT);
                }
            }
        }

        private void OnRecvFromService(IAsyncResult iar) {
            lock (this.LOCK) {
                try {
                    int recvCount = this.m_serviceSocket.EndReceive(iar);

                    if (recvCount > 0) {
                        List<Packet> packets = this.m_serviceSecurity.TransferIncoming();
                        if (packets != null) {
                            for (int i = 0; i < packets.Count; i++) {
                                Packet packet = packets[i];
                                if (packet.Opcode == 0x9000 || packet.Opcode == 0x5000) {
                                    continue;
                                }

                                if (this.ServiceComponent.OnPacketReceived != null) {

                                    PacketResult result = this.ServiceComponent.OnPacketReceived(this.Client, new SROPacket(packet), PacketSocketType.SERVER);

                                    HandleAndTransferResult(packet, PacketSocketType.CLIENT, result);

                                }
                            }

                        }
                    } else {
                        this.Stop();
                        this.Disconnect(this, ClientDisconnectType.ONRECV_FROM_SERVICE_SIZE_ZERO);
                        return;
                    }

                    this.TransferToClient();
                    this.DoRecvFromModule();
                } catch {
                    this.Stop();
                    this.Disconnect(this, ClientDisconnectType.ONRECV_FROM_SERVICE);
                }
            }
        }

        private void DoRecvFromClient() {
            try {
                this.m_clientSocket.BeginReceive(m_localBuffer, 0, m_localBuffer.Length, SocketFlags.None, new AsyncCallback(OnRecvFromClient), null);
            } catch {
                this.Stop();
                this.Disconnect(this, ClientDisconnectType.DORECV_FROM_CLIENT);
            }
        }

        private void DoRecvFromModule() {
            try {
                this.m_serviceSocket.BeginReceive(m_serviceBuffer, 0, m_serviceBuffer.Length, SocketFlags.None, new AsyncCallback(OnRecvFromService), null);
            } catch {
                this.Stop();
                this.Disconnect(this, ClientDisconnectType.DORECV_FROM_SERVICE);
            }
        }


        private void TransferToClient() {
            try {
                this.m_serviceSendPackets = this.m_localSecurity.TransferOutgoing();
                if (this.m_serviceSendPackets != null) {
                    this.m_serviceSendPackets.ForEach(item => {
                        DoSendToClient(item.Key.Buffer, item.Key.Buffer.Length);
                    });
                }
            } catch {
                this.Stop();
                this.Disconnect(this, ClientDisconnectType.TRANSFERTO_CLIENT_OUTGOING);
            }
        }

        private void TransferToService() {
            try {
                this.m_localSendPackets = this.m_serviceSecurity.TransferOutgoing();
                if (this.m_localSendPackets != null) {
                    this.m_localSendPackets.ForEach(item => {
                        DoSendToService(item.Key.Buffer, item.Key.Buffer.Length);
                    });
                }
            } catch {
                this.Stop();
                this.Disconnect(this, ClientDisconnectType.TRANSFERTO_SERVICE_OUTGOING);
            }
        }

        private void DoSendToClient(byte[] buffer, int len) {
            try {
                this.m_clientSocket.BeginSend(buffer, 0, len, SocketFlags.None,
                  (iar) => {
                      try {
                          int sentCount = this.m_clientSocket.EndSend(iar);
                      } catch (Exception ex) {
                          this.Stop();
                          this.Disconnect(this, ClientDisconnectType.SENDTO_CLIENT_ENDSEND);
                      }
                  }, null);
            } catch {
                this.Stop();
                this.Disconnect(this, ClientDisconnectType.SENDTO_CLIENT_BEGINSEND);
            }
        }

        private void DoSendToService(byte[] buffer, int len) {
            try {
                this.m_serviceSocket.BeginSend(buffer, 0, len, SocketFlags.None,
                    (iar) => {
                        try {
                            int sentCount = this.m_serviceSocket.EndSend(iar);
                        } catch {
                            this.Stop();
                            this.Disconnect(this, ClientDisconnectType.SENDTO_SERVICE_ENDSEND);
                        }
                    }, null);
            } catch {
                this.Stop();
                this.Disconnect(this, ClientDisconnectType.SENDTO_SERVICE_BEGINSEND);
            }
        }

        #endregion

        public void SendPacket(Packet packet) {
            this.SendPacket(packet, PacketSocketType.CLIENT, null);
        }

        public void SendPacket(Packet packet, PacketSocketType direction) {
            this.SendPacket(packet, direction, null);
        }

        public void SendPacket(Packet packet, PacketSocketType direction, Action<bool> callback) {
            try {
                if (direction == PacketSocketType.CLIENT) {
                    this.m_localSecurity.Send(packet);
                    this.TransferToClient();
                } else if (direction == PacketSocketType.SERVER) {
                    this.m_serviceSecurity.Send(packet);
                    this.TransferToService();
                }
                if (callback != null) callback(true);
            } catch {
                if (callback != null) callback(false);
            }
            
        }

        public void Dispose() {
            this.Dispose();
        }

        private void Dispose(bool disposing) {
            if (!m_wasDisposed) {
                if (disposing) {
                    //
                }
                if (this.m_clientSocket != null) {
                    this.m_clientSocket.Dispose();
                    this.m_clientSocket = null;
                }
            }
            this.Dispose(disposing);
        }

        public void SendMessage(MessageType type, string sender, string message) {
            Packet packet = new Packet(0x3026);
            switch (type) {
                case MessageType.PM:
                    packet.WriteUInt8(2);
                    packet.WriteAscii(sender);
                    break;
                case MessageType.GLOBAL:
                    packet.WriteUInt8(6);
                    packet.WriteAscii(sender);
                    break;
                case MessageType.NOTICE:
                    packet.WriteUInt8(7);
                    break;
                case MessageType.ACADEMY:
                    packet.WriteUInt8(0x10);
                    packet.WriteAscii(sender);
                    break;
                default:
                    break;
            }
            
            packet.WriteAscii(message);
            this.SendPacket(packet, PacketSocketType.CLIENT);
        }

    }
}