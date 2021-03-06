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
using System.Text;
using System.Threading.Tasks;
using Machine.Specifications;
using paramore.brighter.commandprocessor.Logging;
using paramore.commandprocessor.tests.MessagingGateway.TestDoubles;
using Polly.CircuitBreaker;
using RabbitMQ.Client;
using paramore.brighter.commandprocessor;
using paramore.brighter.commandprocessor.messaginggateway.rmq;
using paramore.brighter.commandprocessor.messaginggateway.rmq.MessagingGatewayConfiguration;
using RabbitMQ.Client.Exceptions;

namespace paramore.commandprocessor.tests.MessagingGateway.rmq
{
    [Subject("Messaging Gateway")]
    [Tags("Requires", new[] { "RabbitMQ" })]
    public class When_posting_a_message_via_the_messaging_gateway
    {
        static IAmAMessageProducer messageProducer;
        static IAmAMessageConsumer messageConsumer;
        static Message message;
        static TestRMQListener client;
        static string messageBody;

        private Establish context = () =>
        {
            var logger = LogProvider.For<RmqMessageConsumer>();
            
            message = new Message(header: new MessageHeader(Guid.NewGuid(), "test1", MessageType.MT_COMMAND), body: new MessageBody("test content"));

            messageProducer = new RmqMessageProducer(logger);
            messageConsumer = new RmqMessageConsumer(message.Header.Topic, message.Header.Topic, logger);

            client = new TestRMQListener(message.Header.Topic);
            messageConsumer.Purge();
        };

        Because of = () =>
        {
            messageProducer.Send(message);
            messageBody = client.Listen();
        };

        It should_send_a_message_via_rmq_with_the_matching_body = () => messageBody.ShouldEqual(message.Body.Value);

        Cleanup tearDown = () =>
        {
            messageConsumer.Purge();
            messageProducer.Dispose();
        };
    }

    internal class TestRMQListener
    {
        readonly string channelName;
        readonly ConnectionFactory connectionFactory;
        readonly IConnection connection;
        readonly IModel channel;

        public TestRMQListener(string channelName)
        {
            this.channelName = channelName;
            var configuration = RMQMessagingGatewayConfigurationSection.GetConfiguration();
            connectionFactory = new ConnectionFactory { Uri = configuration.AMPQUri.Uri.ToString() };
            connection = connectionFactory.CreateConnection();
            channel = connection.CreateModel();
            channel.ExchangeDeclare(configuration.Exchange.Name, ExchangeType.Direct, false);
            channel.QueueDeclare(this.channelName, false, false, false, null);
            channel.QueueBind(this.channelName, configuration.Exchange.Name, this.channelName);
        }

        public string Listen()
        {
            try
            {
                var result = channel.BasicGet(this.channelName, true);
                if (result != null)
                {
                    channel.BasicAck(result.DeliveryTag, false);
                    var message = Encoding.UTF8.GetString(result.Body);
                    return message;
                }
            }
            finally
            {
                //Added wait as rabbit needs some time to sort it self out and the close and dispose was happening to quickly
                Task.Delay(200).Wait();
                channel.Dispose();
                if (connection.IsOpen) connection.Dispose();
            }
            return null;
        }
    }

    [Subject("Messaging Gateway")]
    [Tags("Requires", new[] { "RabbitMQ" })]
    public class When_reading_a_message_via_the_messaging_gateway
    {
        static IAmAMessageProducer sender;
        static IAmAMessageConsumer receiver;
        static Message sentMessage;
        static Message receivedMessage;

        private Establish context = () =>
        {
            var testGuid = Guid.NewGuid();
            var logger = LogProvider.For<RmqMessageConsumer>();
            
            var messageHeader = new MessageHeader(Guid.NewGuid(), "test2", MessageType.MT_COMMAND);

            messageHeader.UpdateHandledCount();
            sentMessage = new Message(header: messageHeader, body: new MessageBody("test content"));

            sender = new RmqMessageProducer(logger);
            receiver = new RmqMessageConsumer(sentMessage.Header.Topic, sentMessage.Header.Topic, logger);

            receiver.Purge();
            
            //create queue if missing
            receiver.Receive(1);
        };

        Because of = () =>
        {
            sender.Send(sentMessage);
            receivedMessage = receiver.Receive(2000);
            receiver.Acknowledge(receivedMessage);
        };

        It should_send_a_message_via_rmq_with_the_matching_body = () => receivedMessage.ShouldEqual(sentMessage);

        Cleanup teardown = () =>
        {
            receiver.Purge();
            sender.Dispose();
            receiver.Dispose();
        };
    }

    [Subject("Messaging Gateway")]
    [Tags("Requires", new[] { "RabbitMQ" })]
    public class When_a_message_consumer_throws_an_already_closed_exception_when_connecting_should_retry_until_circuit_breaks
    {
        static IAmAMessageProducer sender;
        static IAmAMessageConsumer receiver;
        static IAmAMessageConsumer badReceiver;
        static Message sentMessage;
        static Exception expectedException;
        static Exception firstException;

        Establish context = () =>
        {
            var logger = LogProvider.For<BrokerUnreachableRmqMessageConsumer>();

            var messageHeader = new MessageHeader(Guid.NewGuid(), "test2", MessageType.MT_COMMAND);

            messageHeader.UpdateHandledCount();
            sentMessage = new Message(header: messageHeader, body: new MessageBody("test content"));

            sender = new RmqMessageProducer(logger);
            receiver = new RmqMessageConsumer(sentMessage.Header.Topic, sentMessage.Header.Topic, logger);
            badReceiver = new AlreadyClosedRmqMessageConsumer(sentMessage.Header.Topic, sentMessage.Header.Topic, logger);

            receiver.Purge();
            sender.Send(sentMessage);
        };

        Because of = () =>
                     {
                         firstException = Catch.Exception(() => badReceiver.Receive(2000));
                     };

        It should_return_a_channel_failure_exception = () => firstException.ShouldBeOfExactType<ChannelFailureException>(); 
        It should_return_an_explainging_inner_exception = () => firstException.InnerException.ShouldBeOfExactType<AlreadyClosedException>(); 

        Cleanup teardown = () =>
        {
            receiver.Purge();
            sender.Dispose();
            receiver.Dispose();
        };

    }

    [Subject("Messaging Gateway")]
    [Tags("Requires", new[] { "RabbitMQ" })]
    public class When_a_message_consumer_throws_an_operation_interrupted_exception_when_connecting_should_retry_until_circuit_breaks
    {
        static IAmAMessageProducer sender;
        static IAmAMessageConsumer receiver;
        static IAmAMessageConsumer badReceiver;
        static Message sentMessage;
        static Exception expectedException;
        static Exception firstException;

        Establish context = () =>
        {
            var logger = LogProvider.For<BrokerUnreachableRmqMessageConsumer>();

            var messageHeader = new MessageHeader(Guid.NewGuid(), "test2", MessageType.MT_COMMAND);

            messageHeader.UpdateHandledCount();
            sentMessage = new Message(header: messageHeader, body: new MessageBody("test content"));

            sender = new RmqMessageProducer(logger);
            receiver = new RmqMessageConsumer(sentMessage.Header.Topic, sentMessage.Header.Topic, logger);
            badReceiver = new OperationInterruptedRmqMessageConsumer(sentMessage.Header.Topic, sentMessage.Header.Topic, logger);

            receiver.Purge();
            sender.Send(sentMessage);
        };

        Because of = () =>
                     {
                         firstException = Catch.Exception(() => badReceiver.Receive(2000));
                     };

        It should_return_a_channel_failure_exception = () => firstException.ShouldBeOfExactType<ChannelFailureException>(); 
        It should_return_an_explainging_inner_exception = () => firstException.InnerException.ShouldBeOfExactType<OperationInterruptedException>(); 

        Cleanup teardown = () =>
        {
            receiver.Purge();
            sender.Dispose();
            receiver.Dispose();
        };

    }

    [Subject("Messaging Gateway")]
    [Tags("Requires", new[] { "RabbitMQ" })]
    public class When_a_message_consumer_throws_an_not_supported_exception_when_connecting_should_retry_until_circuit_breaks
    {
        static IAmAMessageProducer sender;
        static IAmAMessageConsumer receiver;
        static IAmAMessageConsumer badReceiver;
        static Message sentMessage;
        static Exception expectedException;
        static Exception firstException;

        Establish context = () =>
        {
            var logger = LogProvider.For<BrokerUnreachableRmqMessageConsumer>();

            var messageHeader = new MessageHeader(Guid.NewGuid(), "test2", MessageType.MT_COMMAND);

            messageHeader.UpdateHandledCount();
            sentMessage = new Message(header: messageHeader, body: new MessageBody("test content"));

            sender = new RmqMessageProducer(logger);
            receiver = new RmqMessageConsumer(sentMessage.Header.Topic, sentMessage.Header.Topic, logger);
            badReceiver = new NotSupportedRmqMessageConsumer(sentMessage.Header.Topic, sentMessage.Header.Topic, logger);

            receiver.Purge();
            sender.Send(sentMessage);
        };

        Because of = () =>
                     {
                         firstException = Catch.Exception(() => badReceiver.Receive(2000));
                     };

        It should_return_a_channel_failure_exception = () => firstException.ShouldBeOfExactType<ChannelFailureException>(); 
        It should_return_an_explainging_inner_exception = () => firstException.InnerException.ShouldBeOfExactType<NotSupportedException>(); 

        Cleanup teardown = () =>
        {
            receiver.Purge();
            sender.Dispose();
            receiver.Dispose();
        };

    }
}