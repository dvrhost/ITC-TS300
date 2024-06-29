using System;
using System.Text;
using Crestron.SimplSharp;                          				// For Basic SIMPL# Classes
using Crestron.SimplSharp.CrestronSockets;

namespace ITCTS300
{
    public class TS300Communications
    {
        #region Properties

        private TCPClient TcpClient { get; set; }
        private bool initialized { get; set; }
        protected string IpAddress { get; set; }

        private bool debug { get; set; }
        private CTimer reconnectTimer;
        protected int Port { get; set; }
        private bool manualDisconnect { get; set; }

        private enum ErrorLevel { Notice, Warning, Error, None }

        #endregion

        #region Delegates Simple+

        public delegate void ReceiveDataHandler(SimplSharpString data);
        public ReceiveDataHandler ReceiveData { get; set; }

        public delegate void ConnectionStatusHandler(SimplSharpString serialStatus, ushort analogStatus);
        public ConnectionStatusHandler ConnectionStatus { get; set; }

        public delegate void InitializedStatusHandler(ushort status);
        public InitializedStatusHandler InitializedStatus { get; set; }


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

        public void Send(string data)
        {
            this.TcpClient.SendData(Encoding.ASCII.GetBytes(data), Encoding.ASCII.GetBytes(data).Length);
        }

        public void Send(byte[] data)
        {
            this.TcpClient.SendData(data, data.Length);
        }

        //Events
        private void ClientSocketStatusChange(TCPClient mytcpclient, SocketStatus clientsocketstatus)
        {
            // Check to see if it just connected or disconnected
        }

        private void SerialSendDataCallBack(TCPClient myTcpClient, int numberOfBytesSent)
        {

        }

        private void SerialRecieveCallBack(TCPClient myTcpClient, int numberOfBytesReceived)
        {
            var stringdataReceived = Encoding.ASCII.GetString(myTcpClient.IncomingDataBuffer, 0, numberOfBytesReceived);
            this.TcpClient.ReceiveDataAsync(this.SerialRecieveCallBack);
        }

        private void ClientConnectCallBackFunction(TCPClient myTcpClient)
        {

        }
    }
}
