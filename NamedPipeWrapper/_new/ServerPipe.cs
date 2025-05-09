﻿/* The MIT License (MIT)
* 
* Copyright (c) 2015 Marc Clifton
* 
* Permission is hereby granted, free of charge, to any person obtaining a copy
* of this software and associated documentation files (the "Software"), to deal
* in the Software without restriction, including without limitation the rights
* to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
* copies of the Software, and to permit persons to whom the Software is
* furnished to do so, subject to the following conditions:
* 
* The above copyright notice and this permission notice shall be included in all
* copies or substantial portions of the Software.
* 
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
* IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
* FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
* AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
* LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
* OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
* SOFTWARE.
*/

// From: http://stackoverflow.com/questions/34478513/c-sharp-full-duplex-asynchronous-named-pipes-net
// See Eric Frazer's Q and self answer

using System;
using System.IO;
using System.IO.Pipes;

namespace Clifton.Core.Pipes
{
    public class ServerPipe : BasicPipe
    {
        private readonly PipeSecurity _security;
        public event EventHandler<EventArgs> Connected;

        protected NamedPipeServerStream serverPipeStream;
        protected string PipeName { get; set; }

        public bool IsConnected => serverPipeStream?.IsConnected == true;

		public ServerPipe(string pipeName, Action<BasicPipe> asyncReaderStart, PipeSecurity security)
        {
            _security = security;
            this.asyncReaderStart = asyncReaderStart;
            PipeName = pipeName;

            //serverPipeStream = new NamedPipeServerStream(
            //    pipeName,
            //    PipeDirection.InOut,
            //    NamedPipeServerStream.MaxAllowedServerInstances,
            //    PipeTransmissionMode.Message,
            //    PipeOptions.Asynchronous | PipeOptions.WriteThrough, 0,0, security /*,HandleInheritability.Inheritable*/);
#if NETFRAMEWORK
            serverPipeStream = new NamedPipeServerStream(pipeName,
	            PipeDirection.InOut,
	            NamedPipeServerStream.MaxAllowedServerInstances,
	            PipeTransmissionMode.Byte,
	            PipeOptions.Asynchronous | PipeOptions.WriteThrough, 0, 0, security);
#else
			serverPipeStream = new NamedPipeServerStream(pipeName,
			            PipeDirection.InOut,
			            NamedPipeServerStream.MaxAllowedServerInstances,
			            PipeTransmissionMode.Byte,
			            PipeOptions.Asynchronous | PipeOptions.WriteThrough, 0, 0);
            serverPipeStream.SetAccessControl(security);
#endif


			pipeStream = serverPipeStream;
            serverPipeStream.BeginWaitForConnection(new AsyncCallback(PipeConnected), null);
        }

        protected void PipeConnected(IAsyncResult ar)
        {
            serverPipeStream.EndWaitForConnection(ar);
            Connected?.Invoke(this, new EventArgs());
			asyncReaderStart(this);
        }
    }
}