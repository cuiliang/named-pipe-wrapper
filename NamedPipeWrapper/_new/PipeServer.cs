using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Clifton.Core.Pipes;

namespace NamedPipeWrapper._new
{
    /// <summary>
    /// NamedPipe服务
    /// </summary>
    public class PipeServer
    {
        public EventHandler<PipeEventArgs> MessageReceivedEvent;

        private readonly string _pipeName;
        private readonly PipeSecurity _security;

        //private List<ServerPipe> serverPipes = new List<ServerPipe>();
        private IDictionary<ServerPipe, string> serverPipes = new ConcurrentDictionary<ServerPipe, string>();

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="pipeName">要监听的NamedPipe名称</param>
        public PipeServer(string pipeName, PipeSecurity security)
        {
            _pipeName = pipeName;
            _security = security;
        }

        /// <summary>
        /// 开始接收连接
        /// </summary>
        public void Start()
        {
            CreateServer();
        }


        private ServerPipe CreateServer()
        {
            ServerPipe serverPipe = new ServerPipe(_pipeName, p => p.StartStringReaderAsync(), _security);
            serverPipes.Add(serverPipe, string.Empty);

            serverPipe.DataReceived += ServerPipeOnDataReceived;
            serverPipe.Connected += ServerPipeOnConnected;
            serverPipe.PipeClosed += ServerPipeOnPipeClosed;

            return serverPipe;
        }

        private void ServerPipeOnPipeClosed(object sender, EventArgs e)
        {
            var serverPipe = sender as ServerPipe;
            ClosePipe(serverPipe);
        }

        /// <summary>
        /// 关闭Pipie并将其移除
        /// </summary>
        /// <param name="serverPipe"></param>
        public void ClosePipe(ServerPipe serverPipe)
        {
            if (serverPipes.ContainsKey(serverPipe))
            {
                serverPipes.Remove(serverPipe);

                try
                {
                    serverPipe.DataReceived -= ServerPipeOnDataReceived;
                    serverPipe.Connected -= ServerPipeOnConnected;
                    serverPipe.PipeClosed -= ServerPipeOnPipeClosed;
                    serverPipe.Close();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("ClosePipe出错：" + ex.Message,ex);
                }
                
            }
        }

        private void ServerPipeOnConnected(object sender, EventArgs e)
        {
            CreateServer();
        }

        private void ServerPipeOnDataReceived(object sender, PipeEventArgs e)
        {
            MessageReceivedEvent?.Invoke(sender, e);
        }
    }


    
}
