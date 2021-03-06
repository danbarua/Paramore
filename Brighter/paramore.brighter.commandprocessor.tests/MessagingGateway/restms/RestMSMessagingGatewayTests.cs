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
using Machine.Specifications;
using paramore.brighter.commandprocessor;
using paramore.brighter.commandprocessor.Logging;
using paramore.brighter.commandprocessor.messaginggateway.restms;
using paramore.brighter.commandprocessor.messaginggateway.rmq;

namespace paramore.commandprocessor.tests.MessagingGateway.restms
{
    [Tags("Requires", new[] { "RestMS" })]
    public class When_posting_a_message_via_the_messaging_gateway
    {
        const string TOPIC = "test";
        static IAmAMessageProducer messageProducer;
        static IAmAMessageConsumer messageConsumer;
        static Message message;
        static Message sentMessage;
        static string messageBody;
        const string QUEUE_NAME = "test";

        Establish context = () =>
        {
            var logger = LogProvider.For<RmqMessageConsumer>();
            messageProducer = new RestMsMessageProducer(logger);
            messageConsumer = new RestMsMessageConsumer(QUEUE_NAME, TOPIC, logger);
            message = new Message(
                header: new MessageHeader(Guid.NewGuid(), TOPIC, MessageType.MT_COMMAND),
                body: new MessageBody("test content")
                );

        };

        Because of = () =>
        {
            messageConsumer.Receive(30000); //Need to receive to subscribe to feed, before we send a message. This returns an empty message we discard
            messageProducer.Send(message);
            sentMessage = messageConsumer.Receive(30000);
            messageBody = sentMessage.Body.Value;
            messageConsumer.Acknowledge(sentMessage);
        };

        It should_send_a_message_via_restms_with_the_matching_body = () => messageBody.ShouldEqual(message.Body.Value);
        It should_have_an_empty_pipe_after_acknowledging_the_message = () => ((RestMsMessageConsumer)messageConsumer).NoOfOutstandingMessages(30000).ShouldEqual(0);

        Cleanup tearDown = () =>
        {
            messageConsumer.Purge();
            messageProducer.Dispose();
        };

    }

}