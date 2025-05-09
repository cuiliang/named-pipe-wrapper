﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using NamedPipeWrapper.IO;
using NamedPipeWrapper.Native;
using NamedPipeWrapper.Threading;

namespace NamedPipeWrapper
{
    /// <summary>
    /// Wraps a <see cref="NamedPipeClientStream"/>.
    /// </summary>
    /// <typeparam name="TReadWrite">Reference type to read from and write to the named pipe</typeparam>
    public class NamedPipeClient<TReadWrite> : NamedPipeClient<TReadWrite, TReadWrite> where TReadWrite : class
    {
        /// <summary>
        /// Constructs a new <c>NamedPipeClient</c> to connect to the <see cref="NamedPipeNamedPipeServer{TReadWrite}"/> specified by <paramref name="pipeName"/>.
        /// </summary>
        /// <param name="pipeName">Name of the server's pipe</param>
        /// <param name="serverName">server name default is local.</param>
        public NamedPipeClient(string pipeName,string serverName=".") : base(pipeName, serverName)
        {
        }
    }

    /// <summary>
    /// Wraps a <see cref="NamedPipeClientStream"/>.
    /// </summary>
    /// <typeparam name="TRead">Reference type to read from the named pipe</typeparam>
    /// <typeparam name="TWrite">Reference type to write to the named pipe</typeparam>
    public class NamedPipeClient<TRead, TWrite>
        where TRead : class
        where TWrite : class
    {
        /// <summary>
        /// Gets or sets whether the client should attempt to reconnect when the pipe breaks
        /// due to an error or the other end terminating the connection.
        /// Default value is <c>true</c>.
        /// </summary>
        public bool AutoReconnect { get; set; }

		/// <summary>
		/// 连接时的最大重试次数。
		/// -1 表示持续重试，适合持续性连接。
		/// 其它表示临时性连接。
		/// </summary>
		public int MaxConnectRetryCount { get; set; } = -1;

        /// <summary>
        /// 取消连接
        /// </summary>
        public CancellationTokenSource ConnectionCancellationTokenSource { get; set; }

		/// <summary>
		/// Invoked whenever a message is received from the server.
		/// </summary>
		public event ConnectionMessageEventHandler<TRead, TWrite> ServerMessage;

        /// <summary>
        /// Invoked when the client disconnects from the server (e.g., the pipe is closed or broken).
        /// </summary>
        public event ConnectionEventHandler<TRead, TWrite> Disconnected;


        /// <summary>
        /// Invoked when the client connected to the server
        /// </summary>
        public event ConnectionEventHandler<TRead, TWrite> Connected;

        /// <summary>
        /// Invoked whenever an exception is thrown during a read or write operation on the named pipe.
        /// </summary>
        public event PipeExceptionEventHandler Error;

        private readonly string _pipeName;
        private NamedPipeConnection<TRead, TWrite> _connection;

        private readonly AutoResetEvent _connected = new AutoResetEvent(false);
        private readonly AutoResetEvent _disconnected = new AutoResetEvent(false);

        private volatile bool _closedExplicitly;
        /// <summary>
        /// the server name, which client will connect to.
        /// </summary>
        private string _serverName { get; set; }

        /// <summary>
        /// Constructs a new <c>NamedPipeClient</c> to connect to the <see cref="NamedPipeServer{TRead, TWrite}"/> specified by <paramref name="pipeName"/>.
        /// </summary>
        /// <param name="pipeName">Name of the server's pipe</param>
        /// <param name="serverName">the Name of the server, default is  local machine</param>
        public NamedPipeClient(string pipeName,string serverName)
        {
            _pipeName = pipeName;
            _serverName = serverName;
            AutoReconnect = true;
        }

        /// <summary>
        /// Connects to the named pipe server asynchronously.
        /// This method returns immediately, possibly before the connection has been established.
        /// </summary>
        public void Start()
        {
	        if (ConnectionCancellationTokenSource != null)
	        {
                ConnectionCancellationTokenSource.Cancel();
			}
            ConnectionCancellationTokenSource = new CancellationTokenSource();

			_closedExplicitly = false;
            var worker = new Worker();
            worker.Error += OnError;
            worker.DoWork(ListenSync);
        }

        /// <summary>
        ///     Sends a message to the server over a named pipe.
        /// </summary>
        /// <param name="message">Message to send to the server.</param>
        public void PushMessage(TWrite message)
        {
            if (_connection != null)
                _connection.PushMessage(message);
        }

        /// <summary>
        /// Closes the named pipe.
        /// </summary>
        public void Stop()
        {
            _closedExplicitly = true;
            if (_connection != null)
                _connection.Close();

            if (ConnectionCancellationTokenSource != null)
			{
				ConnectionCancellationTokenSource.Cancel();
			}
		}

        /// <summary>
        /// 是否连接
        /// </summary>
        /// <returns></returns>
        public bool IsConnected()
        {
            return _connection?.IsConnected == true;
        }

        #region Wait for connection/disconnection

        public void WaitForConnection()
        {
            _connected.WaitOne();
        }

        public void WaitForConnection(int millisecondsTimeout)
        {
            _connected.WaitOne(millisecondsTimeout);
        }

        public void WaitForConnection(TimeSpan timeout)
        {
            _connected.WaitOne(timeout);
        }

        public void WaitForDisconnection()
        {
            _disconnected.WaitOne();
        }

        public void WaitForDisconnection(int millisecondsTimeout)
        {
            _disconnected.WaitOne(millisecondsTimeout);
        }

        public void WaitForDisconnection(TimeSpan timeout)
        {
            _disconnected.WaitOne(timeout);
        }

        #endregion

        #region Private methods

        private void ListenSync()
        {

            /*
            // Get the name of the data pipe that should be used from now on by this NamedPipeClient
            var handshake = PipeClientFactory.Connect<string, string>(_pipeName,_serverName);
            var dataPipeName = handshake.ReadObject();

            // dataPipeName 可能为空？

            handshake.Close();

            // Connect to the actual data pipe
            var dataPipe = PipeClientFactory.CreateAndConnectPipe(dataPipeName,_serverName);
            */

            var dataPipe = PipeClientFactory.CreateAndConnectPipe(_pipeName, _serverName, MaxConnectRetryCount, ConnectionCancellationTokenSource);

            // Create a Connection object for the data pipe
            _connection = ConnectionFactory.CreateConnection<TRead, TWrite>(dataPipe);
            _connection.Disconnected += OnDisconnected;
            _connection.ReceiveMessage += OnReceiveMessage;
            _connection.Error += ConnectionOnError;
            _connection.Open();

            _connected.Set();

            Connected?.Invoke(_connection);


            Debug.WriteLine($"ListenSync结束了。");
        }

        private void OnDisconnected(NamedPipeConnection<TRead, TWrite> connection)
        {
            if (Disconnected != null)
                Disconnected(connection);

            _disconnected.Set();

            // Reconnect
            //if (AutoReconnect && !_closedExplicitly)
            if (AutoReconnect) // 20220610,其它断开情况也重连
                Start();
        }

        private void OnReceiveMessage(NamedPipeConnection<TRead, TWrite> connection, TRead message)
        {
            if (ServerMessage != null)
                ServerMessage(connection, message);
        }

        /// <summary>
        ///     Invoked on the UI thread.
        /// </summary>
        private void ConnectionOnError(NamedPipeConnection<TRead, TWrite> connection, Exception exception)
        {
            OnError(exception);
        }

        /// <summary>
        ///     Invoked on the UI thread.
        /// </summary>
        /// <param name="exception"></param>
        private void OnError(Exception exception)
        {
            if (Error != null)
                Error(exception);
        }

        #endregion
    }

    static class PipeClientFactory
    {
        //public static PipeStreamWrapper<TRead, TWrite> Connect<TRead, TWrite>(string pipeName,string serverName)
        //    where TRead : class
        //    where TWrite : class
        //{
        //    return new PipeStreamWrapper<TRead, TWrite>(CreateAndConnectPipe(pipeName,serverName));
        //}

        public static NamedPipeClientStream CreateAndConnectPipe(string pipeName, string serverName, int maxRetry, CancellationTokenSource cts)
        {
            var pipe = CreatePipe(pipeName, serverName);
            ConnectWithRetry(pipe, Path.GetFullPath($@"\\{serverName}\pipe\{pipeName}"), maxRetry, cts);
            return pipe;
        }

        private static NamedPipeClientStream CreatePipe(string pipeName,string serverName)
        {

            return new NamedPipeClientStream(serverName, pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.WriteThrough);
        }

        private static void ConnectWithRetry(NamedPipeClientStream pipe, string fullPath,  int maxRetry, CancellationTokenSource cts)
        {
            var connected = false;
            var resetEvent = new ManualResetEvent(false);

            var retryCount = 0; // 增加重试计数
			

			while (!connected
					&& (maxRetry < 0 || retryCount < maxRetry))
            {
				if (cts != null && cts.IsCancellationRequested)
				{
					return;
				}

				var watch = Stopwatch.StartNew();

				//if (!Kernel32.WaitNamedPipe(fullPath, 1000))
    //            {
    //                Debug.WriteLine($"等待命名管道："+watch.ElapsedMilliseconds);

    //                resetEvent.WaitOne(100); // prevent cpu spin

    //                Debug.WriteLine($"等待命名管道：" + watch.ElapsedMilliseconds);

				//}
				//else
                {
                    try
                    {
                        // NamedPipeClientStream.Connect() defaults to a timeout value of -1, which blocks and spins the CPU, provide a sensible timeout value for Connect().
                        pipe.Connect(1000);
                        connected = true;

                        
					}
                    catch (TimeoutException)
                    {
                        connected = false;
                    }
                    Debug.WriteLine($"等待命名管道：" + watch.ElapsedMilliseconds);
				}

                retryCount++;
			}
        }
    }
}
