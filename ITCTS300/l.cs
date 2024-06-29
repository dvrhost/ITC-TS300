using System;
using System.Text;
using Crestron.SimplSharp;                          				// For Basic SIMPL# Classes
using Crestron.SimplSharp.CrestronSockets;
using System.Collections.Generic;


namespace ITCTS300M
{
    public class TS300Communications
    {
        #region Properties

        private static byte[] prefix = {0xAA,0xEE,0x08 };
        private static byte[] postfix = {0xEE,0xFC};
        private static byte[] TurnOnDelegate = { 0x05, 0x00 };
        private static byte[] TurnOffDelegate = { 0x06, 0x00 };
        private static byte[] TurnOnChairman = { 0x07, 0x00 };
        private static byte[] TurnOffChairman = { 0x08, 0x00 };

        
        private TCPClient TcpClient { get; set; }
        private bool initialized { get; set; }
        protected string IpAddress { get; set; }

        private bool debug { get; set; }
        private CTimer reconnectTimer;
        protected int Port { get; set; }
        private bool manualDisconnect { get; set; }
        private bool connected { get; set; }

        private enum ErrorLevel { Notice, Warning, Error, None }

        #region SocketStatus Dictionary

        private Dictionary<SocketStatus, ushort> sockStatusDict = new Dictionary<SocketStatus, ushort>()
        {
            {SocketStatus.SOCKET_STATUS_NO_CONNECT, 0},
            {SocketStatus.SOCKET_STATUS_WAITING, 1},
            {SocketStatus.SOCKET_STATUS_CONNECTED, 2},
            {SocketStatus.SOCKET_STATUS_CONNECT_FAILED, 3},
            {SocketStatus.SOCKET_STATUS_BROKEN_REMOTELY, 4},
            {SocketStatus.SOCKET_STATUS_BROKEN_LOCALLY, 5},
            {SocketStatus.SOCKET_STATUS_DNS_LOOKUP, 6},
            {SocketStatus.SOCKET_STATUS_DNS_FAILED, 7},
            {SocketStatus.SOCKET_STATUS_DNS_RESOLVED, 8},
            {SocketStatus.SOCKET_STATUS_LINK_LOST,9},
            {SocketStatus.SOCKET_STATUS_SOCKET_NOT_EXIST,10}
        };

        #endregion

        #endregion

        #region Delegates Simpl+

        public delegate void ReceiveDataHandler(SimplSharpString data);
        public ReceiveDataHandler ReceiveData { get; set; }

        public delegate void ConnectionStatusHandler(SimplSharpString serialStatus, ushort analogStatus);
        public ConnectionStatusHandler ConnectionStatus { get; set; }

        public delegate void InitializedStatusHandler(ushort status);
        public InitializedStatusHandler InitializedStatus { get; set; }

        public delegate void TS300StatusHandler();
        public TS300StatusHandler TS300Status { get; set; }


        #endregion

        #region Debug Function

        private void Debug(string msg, ErrorLevel errLevel)
        {
            if (debug)
            {
                CrestronConsole.PrintLine(msg);
                

                if (errLevel != ErrorLevel.None)
                {
                    switch (errLevel)
                    {
                        case ErrorLevel.Notice:
                            ErrorLog.Notice(msg);
                            break;
                        case ErrorLevel.Warning:
                            ErrorLog.Warn(msg);
                            break;
                        case ErrorLevel.Error:
                            ErrorLog.Error(msg);
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// enable logging to ErrorLog
        /// </summary>
        public void EnableDebug()
        {
            debug = true;
            CrestronConsole.PrintLine("Debug Enabled");
        }

        /// <summary>
        /// disable logging to ErrorLog
        /// </summary>
        public void DisableDebug()
        {
            debug = false;
            CrestronConsole.PrintLine("Debug Disabled");
        }

        #endregion


        #region TCP/IP functions

        /// <summary>
        /// SIMPL+ can only execute the default constructor. If you have variables that require initialization, please
        /// use an Initialize method
        /// </summary>
        public void Initialize(string ip, uint port, uint bufferSize)
        {
            TcpClient = new TCPClient(ip, (int)port, (int)bufferSize);
            TcpClient.SocketStatusChange += new TCPClientSocketStatusChangeEventHandler(ClientSocketStatusChange);

            if (TcpClient.PortNumber > 0 && TcpClient.AddressClientConnectedTo != string.Empty)
            {
                initialized = true;
                manualDisconnect = false;
                if (InitializedStatus != null) //Notify SIMPL+ Module
                    InitializedStatus(Convert.ToUInt16(initialized));
                Debug(string.Format("TCPClient to TS300 initialized: IP: {0}, Port: {1}",
                            TcpClient.AddressClientConnectedTo, TcpClient.PortNumber), ErrorLevel.Notice);
                
            }
            else
            {
                initialized = false;
                Debug("TCPClient not initialized, missing data", ErrorLevel.Notice);
            }
        }

        public void Connect()
        {
            SocketErrorCodes err = new SocketErrorCodes();
            if (initialized)
            {
                try
                {
                    manualDisconnect = false;
                    err = TcpClient.ConnectToServerAsync(ClientConnectCallBackFunction);
                    TcpClient.ReceiveDataAsync(SerialRecieveCallBack);
                    Debug(string.Format("Connection attempt: {0}, with error: {1}" ,TcpClient.AddressClientConnectedTo,err.ToString()), ErrorLevel.Notice);
                }
                catch (Exception e)
                {
                    Debug(string.Format("Exeption on connect with error: {0}",e.Message), ErrorLevel.Error);
                }
            }
            else
            {
                Debug("TCPClient not initialized, missing data", ErrorLevel.Notice);
            }
        }

        public void Disconnect()
        {
            SocketErrorCodes err = new SocketErrorCodes();
            if (connected)
            {
                try
                {
                    manualDisconnect = true;
                    TcpClient.Dispose();
                    err = TcpClient.DisconnectFromServer();
                    Debug(string.Format("Disconnect attempt: {0}, with error: {1}", TcpClient.AddressClientConnectedTo, err.ToString()), ErrorLevel.Notice);
                }
                catch (Exception e)
                {
                    Debug(string.Format("Exeption on connect with error: {0}", e.Message), ErrorLevel.Error);
                }
            }
        }

        public void SendString(SimplSharpString data)
        {
            SocketErrorCodes err = new SocketErrorCodes();
            err = TcpClient.SendData(Encoding.ASCII.GetBytes(data.ToString()), Encoding.ASCII.GetBytes(data.ToString()).Length);
            Debug(string.Format("String data transmitted: {0}, with code: {1}" ,data.ToString(),err), ErrorLevel.None);
        }

        private void SendByte(byte[] data)
        {
            SocketErrorCodes err = new SocketErrorCodes();
            err = TcpClient.SendData(data, data.Length);
            Debug(string.Format("Byte data transmitted: {0}, with code: {1}", data.ToString(), err), ErrorLevel.None);
        }



        private void TryReconnect()
        {
            if (!manualDisconnect)
            {
                Debug("Attempting to reconnect...", ErrorLevel.None);
                reconnectTimer = new CTimer(o => { TcpClient.ConnectToServerAsync(ClientConnectCallBackFunction); }, 10000);
            }
        }

        //Events
        private void ClientSocketStatusChange(TCPClient mytcpclient, SocketStatus clientsocketstatus)
        {
            // Check to see if it just connected or disconnected
            if (ConnectionStatus != null) //Notify  if subscribe
            {
                if (sockStatusDict.ContainsKey(clientsocketstatus))
                    ConnectionStatus(clientsocketstatus.ToString(), sockStatusDict[clientsocketstatus]);
            }
            if (clientsocketstatus == SocketStatus.SOCKET_STATUS_CONNECTED)
            {
                TcpClient.ReceiveDataAsync(SerialRecieveCallBack); 
            }
            else
            {
                TryReconnect();
            }
        }


        private void SerialRecieveCallBack(TCPClient myTcpClient, int numberOfBytesReceived)
        {
            
            byte[] rxBuffer;
            var rxToSplus = new SimplSharpString();//data to external subscribers

            if (numberOfBytesReceived > 0)
            {
                rxBuffer = myTcpClient.IncomingDataBuffer;
                rxToSplus = Encoding.Default.GetString(rxBuffer, 0, numberOfBytesReceived);
                if (ReceiveData != null)
                    ReceiveData(rxToSplus);
            }
            TcpClient.ReceiveDataAsync(SerialRecieveCallBack);
        }

        private void ClientConnectCallBackFunction(TCPClient TcpClient)
        {
            if (TcpClient.ClientStatus == SocketStatus.SOCKET_STATUS_CONNECTED)
                connected = true;
            else
            {
                connected = false;
                TryReconnect();
            }
        }
        #endregion

        #region TS-03 Commands

        public void SetWiredConferenceMode(ushort Mode, ushort DelegateNumMode) //Mode 1/2/3/4
        {
            byte[] command = { 0xFF, 0xE1 };
            int offset;
            
            byte[] unitID = { 0x82, 0x01 };
            byte[] reservedData = { 0x00, 0x00, 0x00 };
            byte[] message = new byte[13];
            if (Mode > 0 && Mode <= 4 && DelegateNumMode > 0 && DelegateNumMode <= 4)
            {
                Buffer.BlockCopy(prefix, 0, message, 0, prefix.Length);
                offset = prefix.Length - 1;
                Buffer.BlockCopy(command, 0, message, offset, command.Length);
                offset = offset + command.Length;
                Buffer.BlockCopy(unitID, 0, message, offset, unitID.Length);
                offset = offset + unitID.Length;
                switch (Mode)
                {
                    case 1://FIFO
                        {
                            Buffer.BlockCopy(BitConverter.GetBytes(1), 0, message, offset, BitConverter.GetBytes(1).Length);
                            offset = offset + BitConverter.GetBytes(1).Length;
                            break;
                        }
                    case 2://Normal
                        {
                            Buffer.BlockCopy(BitConverter.GetBytes(2), 0, message, offset, BitConverter.GetBytes(2).Length);
                            offset = offset + BitConverter.GetBytes(2).Length;
                            break;
                        }
                    case 3://Voice
                        {
                            Buffer.BlockCopy(BitConverter.GetBytes(3), 0, message, offset, BitConverter.GetBytes(3).Length);
                            offset = offset + BitConverter.GetBytes(3).Length;
                            break;
                        }
                    case 4://Apply
                        {
                            Buffer.BlockCopy(BitConverter.GetBytes(4), 0, message, offset, BitConverter.GetBytes(4).Length);
                            offset = offset + BitConverter.GetBytes(4).Length;
                            break;
                        }
                    default:
                        break;
                }
                switch (DelegateNumMode)
                {
                    case 1:
                        {
                            Buffer.BlockCopy(BitConverter.GetBytes(1), 0, message, offset, BitConverter.GetBytes(1).Length);
                            offset = offset + BitConverter.GetBytes(1).Length;
                            break;
                        }
                    case 2:
                        {
                            Buffer.BlockCopy(BitConverter.GetBytes(2), 0, message, offset, BitConverter.GetBytes(2).Length);
                            offset = offset + BitConverter.GetBytes(2).Length;
                            break;
                        }
                    case 3:
                        {
                            Buffer.BlockCopy(BitConverter.GetBytes(4), 0, message, offset, BitConverter.GetBytes(4).Length);
                            offset = offset + BitConverter.GetBytes(4).Length;
                            break;
                        }
                    case 4:
                        {
                            Buffer.BlockCopy(BitConverter.GetBytes(8), 0, message, offset, BitConverter.GetBytes(8).Length);
                            offset = offset + BitConverter.GetBytes(8).Length;
                            break;
                        }
                    default:
                        break;
                }
                
                Buffer.BlockCopy(reservedData, 0, message, offset, reservedData.Length);
                offset = offset + reservedData.Length;
                Buffer.BlockCopy(postfix, 0, message, offset, postfix.Length);
                SendByte(message);
                Debug(string.Format("Send message from wired conference mode switch to TS-03: {0}", message), ErrorLevel.None);
            }

        }

        public void SetWirelessConferenceMode(ushort Mode, ushort DelegateNumMode)
        {
            byte[] command = { 0xFF, 0xEA };
            int offset;

            byte[] unitID = { 0x82};
            byte[] reservedData = { 0x00, 0x00, 0x00 };
            byte[] message = new byte[13];
            if (Mode > 0 && Mode <= 4 && DelegateNumMode > 0 && DelegateNumMode<=4)
            {
                Buffer.BlockCopy(prefix, 0, message, 0, prefix.Length);
                offset = prefix.Length - 1;
                Buffer.BlockCopy(command, 0, message, offset, command.Length);
                offset = offset + command.Length;
                Buffer.BlockCopy(unitID, 0, message, offset, unitID.Length);
                offset = offset + unitID.Length;
                switch (Mode)
                {
                    case 1://FIFO
                        {
                            Buffer.BlockCopy(BitConverter.GetBytes(1), 0, message, offset, BitConverter.GetBytes(1).Length);
                            offset = offset + BitConverter.GetBytes(1).Length;
                            break;
                        }
                    case 2://Normal
                        {
                            Buffer.BlockCopy(BitConverter.GetBytes(2), 0, message, offset, BitConverter.GetBytes(2).Length);
                            offset = offset + BitConverter.GetBytes(2).Length;
                            break;
                        }
                    case 3://Voice
                        {
                            Buffer.BlockCopy(BitConverter.GetBytes(3), 0, message, offset, BitConverter.GetBytes(3).Length);
                            offset = offset + BitConverter.GetBytes(3).Length;
                            break;
                        }
                    case 4://Apply
                        {
                            Buffer.BlockCopy(BitConverter.GetBytes(4), 0, message, offset, BitConverter.GetBytes(4).Length);
                            offset = offset + BitConverter.GetBytes(4).Length;
                            break;
                        }
                    default:
                        break;
                }
                switch (DelegateNumMode)
                {
                    case 1:
                        {
                            Buffer.BlockCopy(BitConverter.GetBytes(1), 0, message, offset, BitConverter.GetBytes(1).Length);
                            offset = offset + BitConverter.GetBytes(1).Length;
                            break;
                        }
                    case 2:
                        {
                            Buffer.BlockCopy(BitConverter.GetBytes(2), 0, message, offset, BitConverter.GetBytes(2).Length);
                            offset = offset + BitConverter.GetBytes(2).Length;
                            break;
                        }
                    case 3:
                        {
                            Buffer.BlockCopy(BitConverter.GetBytes(4), 0, message, offset, BitConverter.GetBytes(4).Length);
                            offset = offset + BitConverter.GetBytes(4).Length;
                            break;
                        }
                    case 4:
                        {
                            Buffer.BlockCopy(BitConverter.GetBytes(6), 0, message, offset, BitConverter.GetBytes(6).Length);
                            offset = offset + BitConverter.GetBytes(6).Length;
                            break;
                        }
                    default:
                        break;
                }

                Buffer.BlockCopy(reservedData, 0, message, offset, reservedData.Length);
                offset = offset + reservedData.Length;
                Buffer.BlockCopy(postfix, 0, message, offset, postfix.Length);
                SendByte(message);
                Debug(string.Format("Send message from wireless conference mode switch to TS-03: {0}", message), ErrorLevel.None);
            }
        }

        public void SetMasterVolume(ushort Volume)
        {
            int offset;
            byte[] command = { 0xFF,0xE3};
            byte[] unitID = { 0x82, 0x01 };
            byte[] reservedData = { 0x00,0x00,0x00};
            byte[] message = new byte [13];
            if (Volume <= 31)
            {
                Buffer.BlockCopy(prefix, 0, message, 0, prefix.Length);
                offset = prefix.Length - 1;
                Buffer.BlockCopy(command, 0, message, offset, command.Length);
                offset = offset + command.Length;
                Buffer.BlockCopy(unitID, 0, message, offset, unitID.Length);
                offset = offset + unitID.Length;
                Buffer.BlockCopy(BitConverter.GetBytes(Volume), 0, message, offset, BitConverter.GetBytes(Volume).Length);
                offset = offset + BitConverter.GetBytes(Volume).Length;
                Buffer.BlockCopy(reservedData, 0, message, offset, reservedData.Length);
                offset = offset + reservedData.Length;
                Buffer.BlockCopy(postfix, 0, message, offset, postfix.Length);
                SendByte(message);
                Debug(string.Format("Send message from master volume section to TS-03: {0}", message), ErrorLevel.None);
            }
            else
                Debug("Wrong master volume value", ErrorLevel.None);
        }

        //SIMPL+ API Function

        public void MicCtrl(ushort MicID, ushort Operate, ushort MicConnectionType, ushort DelegateType)
        {
            bool _wireless;
            bool _delegate;
            bool _correctdata=false;

            if (MicConnectionType == 1)
            {
                _wireless = true;
                _correctdata = true;
            }
            else if (MicConnectionType == 0)
            {
                _wireless = false;
                _correctdata = true;
            }
            else
            {
                _wireless = false;
                _correctdata = false;
            }
            if (DelegateType == 1)
            {
                _delegate = true;
                _correctdata = true;
            }
            else if (DelegateType == 0)
            {
                _delegate = false;
                _correctdata = true;
            }
            else
            {
                _delegate = false;
                _correctdata = false;
            }
            if (_correctdata)
            {
                if (Operate == 1)//MicOn
                {
                    TurnOnMic(_wireless, _delegate, MicID);
                }
                else if (Operate == 0)//MicOff
                {
                    TurnOffMic(_wireless, _delegate, MicID);
                }
            }
        }
        
        private void TurnOnMic(bool isWireless, bool isDelegate, ushort ID)
        {
            // prefix + (HiBytemicID + LowByteMicID) + unitID+command switch mic + reservedbyte+postfix
            int offset;
            byte[] unitID = {0x80, 0x00};
                        
            byte[] wirelessHiByte = { 0x30};
            byte[] reservedData = { 0x00, 0x00 };
            byte[] message = new byte[13];

            if (ID > 0 && ID < 4096)
            {
                if (isWireless)
                {
                    if (ID <= 300)
                    {
                        var newHiLowBytes = (ushort)((wirelessHiByte[0] << 8) + ID);
                        byte[] HiLowBytes = BitConverter.GetBytes(newHiLowBytes);
                        if (!BitConverter.IsLittleEndian)
                            Array.Reverse(HiLowBytes);
                        Buffer.BlockCopy(prefix, 0, message, 0, prefix.Length);
                        offset = prefix.Length - 1;
                        Buffer.BlockCopy(HiLowBytes, 0, message, offset, HiLowBytes.Length);
                        offset = offset + HiLowBytes.Length;
                        Buffer.BlockCopy(unitID, 0, message, offset, unitID.Length);
                        offset = offset + unitID.Length;
                        if (isDelegate)
                        {
                            Buffer.BlockCopy(TurnOnDelegate, 0, message, offset, TurnOnDelegate.Length);
                            offset = offset + TurnOnDelegate.Length;
                        }
                        else
                        {
                            Buffer.BlockCopy(TurnOnChairman, 0, message, offset, TurnOnChairman.Length);
                            offset = offset + TurnOnChairman.Length;
                        }
                        Buffer.BlockCopy(reservedData, 0, message, offset, reservedData.Length);
                        offset = offset + reservedData.Length;
                        Buffer.BlockCopy(postfix, 0, message, offset, postfix.Length);
                        SendByte(message);
                        Debug(string.Format("Send message from Turn Mic On section to TS-03: {0}", message), ErrorLevel.None);
                    }
                    else
                        Debug("Error wrong wireless Mic ID given", ErrorLevel.Warning);
                }
                else
                {
                    
                    byte[] HiLowBytes = BitConverter.GetBytes(ID);
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(HiLowBytes);
                    Buffer.BlockCopy(prefix, 0, message, 0, prefix.Length);
                    offset = prefix.Length - 1;
                    Buffer.BlockCopy(HiLowBytes, 0, message, offset, HiLowBytes.Length);
                    offset = offset + HiLowBytes.Length;
                    Buffer.BlockCopy(unitID, 0, message, offset, unitID.Length);
                    offset = offset + unitID.Length;
                    if (isDelegate)
                    {
                        Buffer.BlockCopy(TurnOnDelegate, 0, message, offset, TurnOnDelegate.Length);
                        offset = offset + TurnOnDelegate.Length;
                    }
                    else
                    {
                        Buffer.BlockCopy(TurnOnChairman, 0, message, offset, TurnOnChairman.Length);
                        offset = offset + TurnOnChairman.Length;
                    }
                    Buffer.BlockCopy(reservedData, 0, message, offset, reservedData.Length);
                    offset = offset + reservedData.Length;
                    Buffer.BlockCopy(postfix, 0, message, offset, postfix.Length);
                    SendByte(message);
                    Debug(string.Format("Send message from Turn Mic On section to TS-03: {0}", message), ErrorLevel.None);
                }
            }

        }

        private void TurnOffMic(bool isWireless, bool isDelegate, ushort ID)
        {
            // prefix + (HiBytemicID + LowByteMicID) + unitID+command switch mic + reservedbyte+postfix
            int offset;
            byte[] unitID = { 0x80, 0x00 };

            byte[] wirelessHiByte = { 0x30 };
            byte[] reservedData = { 0x00, 0x00 };
            byte[] message = new byte[13];

            if (ID > 0 && ID < 4096)
            {
                if (isWireless)
                {
                    if (ID <= 300)
                    {
                        var newHiLowBytes = (ushort)((wirelessHiByte[0] << 8) + ID);
                        byte[] HiLowBytes = BitConverter.GetBytes(newHiLowBytes);
                        if (!BitConverter.IsLittleEndian)
                            Array.Reverse(HiLowBytes);
                        Buffer.BlockCopy(prefix, 0, message, 0, prefix.Length);
                        offset = prefix.Length - 1;
                        Buffer.BlockCopy(HiLowBytes, 0, message, offset, HiLowBytes.Length);
                        offset = offset + HiLowBytes.Length;
                        Buffer.BlockCopy(unitID, 0, message, offset, unitID.Length);
                        offset = offset + unitID.Length;
                        if (isDelegate)
                        {
                            Buffer.BlockCopy(TurnOffDelegate, 0, message, offset, TurnOffDelegate.Length);
                            offset = offset + TurnOffDelegate.Length;
                        }
                        else
                        {
                            Buffer.BlockCopy(TurnOffChairman, 0, message, offset, TurnOffChairman.Length);
                            offset = offset + TurnOffChairman.Length;
                        }
                        Buffer.BlockCopy(reservedData, 0, message, offset, reservedData.Length);
                        offset = offset + reservedData.Length;
                        Buffer.BlockCopy(postfix, 0, message, offset, postfix.Length);
                        SendByte(message);
                        Debug(string.Format("Send message from Turn Mic On section to TS-03: {0}", message), ErrorLevel.None);
                    }
                    else
                        Debug("Error wrong wireless Mic ID given", ErrorLevel.Warning);
                }
                else
                {

                    byte[] HiLowBytes = BitConverter.GetBytes(ID);
                    if (!BitConverter.IsLittleEndian)
                        Array.Reverse(HiLowBytes);
                    Buffer.BlockCopy(prefix, 0, message, 0, prefix.Length);
                    offset = prefix.Length - 1;
                    Buffer.BlockCopy(HiLowBytes, 0, message, offset, HiLowBytes.Length);
                    offset = offset + HiLowBytes.Length;
                    Buffer.BlockCopy(unitID, 0, message, offset, unitID.Length);
                    offset = offset + unitID.Length;
                    if (isDelegate)
                    {
                        Buffer.BlockCopy(TurnOffDelegate, 0, message, offset, TurnOffDelegate.Length);
                        offset = offset + TurnOffDelegate.Length;
                    }
                    else
                    {
                        Buffer.BlockCopy(TurnOffChairman, 0, message, offset, TurnOffChairman.Length);
                        offset = offset + TurnOffChairman.Length;
                    }
                    Buffer.BlockCopy(reservedData, 0, message, offset, reservedData.Length);
                    offset = offset + reservedData.Length;
                    Buffer.BlockCopy(postfix, 0, message, offset, postfix.Length);
                    SendByte(message);
                    Debug(string.Format("Send message from Turn Mic On section to TS-03: {0}", message), ErrorLevel.None);
                }
            }

        }

        public void AllWirelessUnitTurnOff()
        {
            byte[] data = {0xAA, 0xEE, 0x08, 0xFF,0xF0,0x80,0x04,0xE2,0x00,0x00,0x00,0xEE,0xFC};
            SendByte(data);
            Debug(string.Format("Send message TurnOff All wireless Mics to TS-03: {0}", data), ErrorLevel.None);
        }

        #endregion

    }
}
