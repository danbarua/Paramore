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
using System.Threading.Tasks;
using FakeItEasy;
using Machine.Specifications;
using paramore.brighter.commandprocessor.Logging;
using Polly;
using TinyIoC;
using paramore.brighter.commandprocessor;
using paramore.brighter.commandprocessor.messaginggateway.rmq;
using paramore.brighter.serviceactivator;
using paramore.brighter.serviceactivator.TestHelpers;
using paramore.commandprocessor.tests.CommandProcessors.TestDoubles;
using paramore.commandprocessor.tests.MessageDispatch.TestDoubles;

namespace paramore.commandprocessor.tests.MessageDispatch
{
    public class When_a_message_dispatcher_is_asked_to_connect_a_channel_and_handler
    {
        static Dispatcher dispatcher;
        static FakeChannel channel;
        static IAmACommandProcessor commandProcessor;

        Establish context = () =>
            {
                channel = new FakeChannel();
                commandProcessor = new SpyCommandProcessor();

                var logger = LogProvider.For<Dispatcher>();  

                var messageMapperRegistry = new MessageMapperRegistry(new TestMessageMapperFactory(() => new MyEventMessageMapper()));
                messageMapperRegistry.Register<MyEvent, MyEventMessageMapper>();

                var connection = new Connection(name: new ConnectionName("test"), channel: channel, dataType: typeof(MyEvent), noOfPerformers: 1, timeoutInMilliseconds: 1000);
                dispatcher = new Dispatcher(commandProcessor, messageMapperRegistry, new List<Connection>{connection}, logger);

                var @event = new MyEvent();
                var message = new MyEventMessageMapper().MapToMessage(@event);
                channel.Send(message);

                dispatcher.State.ShouldEqual(DispatcherState.DS_AWAITING);
                dispatcher.Receive();
            };


            Because of = () =>
                {
                    Task.Delay(1000).Wait();
                    dispatcher.End().Wait();
                };

            It should_have_consumed_the_messages_in_the_channel = () => channel.Length.ShouldEqual(0);
            It should_have_a_stopped_state = () => dispatcher.State.ShouldEqual(DispatcherState.DS_STOPPED);
    }

    public class When_a_message_dispatcher_starts_multiple_performers
    {
        private static Dispatcher dispatcher;
        private static FakeChannel channel;
        private static IAmACommandProcessor commandProcessor;

        Establish context = () =>
            {
                channel = new FakeChannel();
                commandProcessor = new SpyCommandProcessor();

                var logger = LogProvider.For<Dispatcher>();  

                var messageMapperRegistry = new MessageMapperRegistry(new TestMessageMapperFactory(() => new MyEventMessageMapper()));
                messageMapperRegistry.Register<MyEvent, MyEventMessageMapper>();

                var connection = new Connection(name: new ConnectionName("test"), channel: channel, dataType: typeof (MyEvent), noOfPerformers: 3, timeoutInMilliseconds: 1000);
                dispatcher = new Dispatcher(commandProcessor, messageMapperRegistry, new List<Connection>{connection}, logger);

                var @event = new MyEvent();
                var message = new MyEventMessageMapper().MapToMessage(@event);
                for (var i =0; i < 6; i++)
                    channel.Send(message);

                dispatcher.State.ShouldEqual(DispatcherState.DS_AWAITING);
                dispatcher.Receive();
            };


        Because of = () =>
            {
                Task.Delay(1000).Wait();
                dispatcher.End().Wait();
            };

        It should_have_consumed_the_messages_in_the_channel = () => channel.Length.ShouldEqual(0);
        It should_have_a_stopped_state = () => dispatcher.State.ShouldEqual(DispatcherState.DS_STOPPED);
    }

     public class When_a_message_dispatcher_starts_different_types_of_performers
    {
        private static Dispatcher dispatcher;
        private static FakeChannel eventChannel;
        private static FakeChannel commandChannel;
        private static IAmACommandProcessor commandProcessor;

        Establish context = () =>
            {
                eventChannel = new FakeChannel();
                commandChannel = new FakeChannel();
                commandProcessor = new SpyCommandProcessor();

                var logger = LogProvider.For<Dispatcher>();  
                var container = new TinyIoCContainer();
                container.Register<MyEventMessageMapper>();
                container.Register<MyCommandMessageMapper>();

                var messageMapperRegistry = new MessageMapperRegistry(new TinyIoCMessageMapperFactory(container));
                messageMapperRegistry.Register<MyEvent, MyEventMessageMapper>();
                messageMapperRegistry.Register<MyCommand, MyCommandMessageMapper>();


                var myEventConnection = new Connection(name: new ConnectionName("test"), channel: eventChannel, dataType: typeof (MyEvent), noOfPerformers: 1, timeoutInMilliseconds: 1000);
                var myCommandConnection = new Connection(name: new ConnectionName("anothertest"), channel: commandChannel, dataType: typeof (MyCommand), noOfPerformers: 1, timeoutInMilliseconds: 1000);
                dispatcher = new Dispatcher(commandProcessor, messageMapperRegistry, new List<Connection> {myEventConnection, myCommandConnection}, logger);

                var @event = new MyEvent();
                var eventMessage = new MyEventMessageMapper().MapToMessage(@event);
                eventChannel.Send(eventMessage);

                var command = new MyCommand();
                var commandMessage = new MyCommandMessageMapper().MapToMessage(command);
                commandChannel.Send(commandMessage);

                dispatcher.State.ShouldEqual(DispatcherState.DS_AWAITING);
                dispatcher.Receive();
            };


        Because of = () =>
            {
                Task.Delay(1000).Wait();
                numberOfConsumers = dispatcher.Consumers.Count();
                dispatcher.End().Wait();
            };

        It should_have_consumed_the_messages_in_the_event_channel = () => eventChannel.Length.ShouldEqual(0);
        It should_have_consumed_the_messages_in_the_command_channel = () => commandChannel.Length.ShouldEqual(0);
        It should_have_a_stopped_state = () => dispatcher.State.ShouldEqual(DispatcherState.DS_STOPPED);
        It should_have_no_consumers = () => dispatcher.Consumers.ShouldBeEmpty();
        It should_of_had_2_consumers_when_running = () => numberOfConsumers.ShouldEqual(2);
       
         private static int numberOfConsumers;
    }

    public class When_a_message_dispatcher_shuts_a_connection
    {
        private static Dispatcher dispatcher;
        private static FakeChannel channel;
        private static IAmACommandProcessor commandProcessor;
        private static Connection connection;

        Establish context = () =>
            {
                channel = new FakeChannel();
                commandProcessor = new SpyCommandProcessor();

                var logger = LogProvider.For<Dispatcher>();  

                var messageMapperRegistry = new MessageMapperRegistry(new TestMessageMapperFactory(() => new MyEventMessageMapper()));
                messageMapperRegistry.Register<MyEvent, MyEventMessageMapper>();

                connection = new Connection(name: new ConnectionName("test"), channel: channel, dataType: typeof (MyEvent), noOfPerformers: 3, timeoutInMilliseconds: 1000);
                dispatcher = new Dispatcher(commandProcessor, messageMapperRegistry, new List<Connection> {connection}, logger);

                var @event = new MyEvent();
                var message = new MyEventMessageMapper().MapToMessage(@event);
                for (var i =0; i < 6; i++)
                    channel.Send(message);

                dispatcher.State.ShouldEqual(DispatcherState.DS_AWAITING);
                dispatcher.Receive();
            };


        Because of = () =>
            {
                Task.Delay(1000).Wait();
                dispatcher.Shut(connection);
                dispatcher.End().Wait();
            };

        It should_have_consumed_the_messages_in_the_channel = () => dispatcher.Consumers.Any(consumer => (consumer.Name == connection.Name) && (consumer.State == ConsumerState.Open)).ShouldBeFalse(); 
        It should_have_a_stopped_state = () => dispatcher.State.ShouldEqual(DispatcherState.DS_STOPPED);
        It should_have_no_consumers = () => dispatcher.Consumers.ShouldBeEmpty();
    }

    public class When_a_message_dispatcher_restarts_a_connection
    {
        private static Dispatcher dispatcher;
        private static FakeChannel channel;
        private static IAmACommandProcessor commandProcessor;
        private static Connection connection;

        Establish context = () =>
            {
                channel = new FakeChannel();
                commandProcessor = new SpyCommandProcessor();

                var logger = LogProvider.For<Dispatcher>();  

                var messageMapperRegistry = new MessageMapperRegistry(new TestMessageMapperFactory(() => new MyEventMessageMapper()));
                messageMapperRegistry.Register<MyEvent, MyEventMessageMapper>();

                connection = new Connection(name: new ConnectionName("test"), channel: channel, dataType: typeof (MyEvent), noOfPerformers: 1, timeoutInMilliseconds: 1000);
                dispatcher = new Dispatcher(commandProcessor, messageMapperRegistry, new List<Connection> {connection}, logger);

                var @event = new MyEvent();
                var message = new MyEventMessageMapper().MapToMessage(@event);
                channel.Send(message);

                dispatcher.State.ShouldEqual(DispatcherState.DS_AWAITING);
                dispatcher.Receive();
                Task.Delay(1000).Wait();
                dispatcher.Shut(connection);
            };


        Because of = () =>
            {
                dispatcher.Open(connection);

                var @event = new MyEvent();
                var message = new MyEventMessageMapper().MapToMessage(@event);
                channel.Send(message);

                dispatcher.End().Wait();
            };

        It should_have_consumed_the_messages_in_the_event_channel = () => channel.Length.ShouldEqual(0);
        It should_have_a_stopped_state = () => dispatcher.State.ShouldEqual(DispatcherState.DS_STOPPED);
    }

    public class When_a_message_dispatcher_restarts_a_connection_after_all_connections_have_stopped
    {
        private static Dispatcher dispatcher;
        private static FakeChannel channel;
        private static IAmACommandProcessor commandProcessor;
        private static Connection connection;
        private static Connection newConnection;

        Establish context = () =>
        {
            channel = new FakeChannel();
            commandProcessor = new SpyCommandProcessor();

            var logger = LogProvider.For<Dispatcher>();  

            var messageMapperRegistry = new MessageMapperRegistry(new TestMessageMapperFactory(() => new MyEventMessageMapper()));
            messageMapperRegistry.Register<MyEvent, MyEventMessageMapper>();

            connection = new Connection(name: new ConnectionName("test"), channel: channel, dataType: typeof(MyEvent), noOfPerformers: 1, timeoutInMilliseconds: 1000);
            newConnection = new Connection(name: new ConnectionName("newTest"), channel: channel, dataType: typeof(MyEvent), noOfPerformers: 1, timeoutInMilliseconds: 1000);
            dispatcher = new Dispatcher(commandProcessor, messageMapperRegistry, new List<Connection> { connection, newConnection }, logger);

            var @event = new MyEvent();
            var message = new MyEventMessageMapper().MapToMessage(@event);
            channel.Send(message);

            dispatcher.State.ShouldEqual(DispatcherState.DS_AWAITING);
            dispatcher.Receive();
            Task.Delay(1000).Wait();
            dispatcher.Shut("test");
            dispatcher.Shut("newTest");
            Task.Delay(3000).Wait();
            dispatcher.Consumers.Count.ShouldEqual(0); //sanity check
        };


        Because of = () =>
        {
            dispatcher.Open("newTest");
            var @event = new MyEvent();
            var message = new MyEventMessageMapper().MapToMessage(@event);
            channel.Send(message);
            Task.Delay(1000).Wait();
        };

        private Cleanup stop_dispatcher = () => dispatcher.End().Wait();

        It should_have_consumed_the_messages_in_the_event_channel = () => channel.Length.ShouldEqual(0);
        It should_have_a_running_state = () => dispatcher.State.ShouldEqual(DispatcherState.DS_RUNNING);
        It should_have_only_one_consumer = () => dispatcher.Consumers.Count.ShouldEqual(1);
    }




    public class When_building_a_dispatcher
    {
        static IAmADispatchBuilder builder;
        static Dispatcher dispatcher;

        Establish context = () =>
            {
                var logger = A.Fake<ILog>();
                var messageMapperRegistry = new MessageMapperRegistry(new TestMessageMapperFactory(() => new MyEventMessageMapper()));
                messageMapperRegistry.Register<MyEvent, MyEventMessageMapper>();

                var retryPolicy = Policy
                    .Handle<Exception>()
                    .WaitAndRetry(new[]
                        {
                            TimeSpan.FromMilliseconds(50),
                            TimeSpan.FromMilliseconds(100),
                            TimeSpan.FromMilliseconds(150)
                        });

                var circuitBreakerPolicy = Policy
                    .Handle<Exception>()
                    .CircuitBreaker(1, TimeSpan.FromMilliseconds(500));

                //var gateway = new RmqMessageConsumer(logger);
                var rmqMessageConsumerFactory = new RmqMessageConsumerFactory(logger);

                builder = DispatchBuilder.With()
                             .Logger(logger)
                             .CommandProcessor(CommandProcessorBuilder.With()
                                .Handlers(new HandlerConfiguration(new SubscriberRegistry(), new TinyIocHandlerFactory(new TinyIoCContainer())))
                                .Policies(new PolicyRegistry() {{CommandProcessor.RETRYPOLICY, retryPolicy},{CommandProcessor.CIRCUITBREAKER, circuitBreakerPolicy}})
                                .Logger(logger)
                                .NoTaskQueues()
                                 .RequestContextFactory(new InMemoryRequestContextFactory())
                                .Build()
                                )
                             .MessageMappers(messageMapperRegistry)
                             .ChannelFactory(new InputChannelFactory(rmqMessageConsumerFactory)) 
                             .ConnectionsFromConfiguration();

            };

        Because of = () => dispatcher = builder.Build(); 

        It should_build_a_dispatcher = () => dispatcher.ShouldNotBeNull();
    }
}
