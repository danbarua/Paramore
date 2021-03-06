﻿#region Licence
/* The MIT License (MIT)
Copyright © 2014 Ian Cooper <ian_hammond_cooper@yahoo.co.uk>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the “Software”), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE. */

#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using paramore.brighter.commandprocessor;

namespace paramore.brighter.serviceactivator.ServiceActivatorConfiguration
{
    internal class ConnectionFactory
    {
        private readonly IAmAChannelFactory channelFactory;

        public ConnectionFactory(IAmAChannelFactory channelFactory)
        {
            this.channelFactory = channelFactory;
        }

        public IEnumerable<Connection> Create(IEnumerable<ConnectionElement> connectionElements)
        {
            var connections = 
            (
               from ConnectionElement connectionElement in connectionElements 
               select new Connection(
                   name: new ConnectionName(connectionElement.ConnectionName), 
                   channel: channelFactory.CreateInputChannel(connectionElement.ChannelName, connectionElement.RoutingKey), 
                   dataType: GetType(connectionElement.DataType), 
                   noOfPerformers: connectionElement.NoOfPerformers, 
                   timeoutInMilliseconds: connectionElement.TimeoutInMiliseconds,
                   requeueCount: connectionElement.RequeueCount)
             ).ToList();


            return connections;
        }

        public static Type GetType(string typeName)
        {
            var dataType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(typeName)).FirstOrDefault(type => type != null);
            return dataType;
        }
    }
}