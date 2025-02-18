﻿// ---------------------------------------------------------------
// Copyright (c) North East London ICB. All rights reserved.
// ---------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Moq;
using NEL.MESH.Brokers.Mesh;
using NEL.MESH.Models.Foundations.Mesh;
using NEL.MESH.Models.Foundations.Mesh.Exceptions;
using NEL.MESH.Models.Foundations.Tokens.Exceptions;
using NEL.MESH.Services.Foundations.Chunks;
using NEL.MESH.Services.Foundations.Mesh;
using NEL.MESH.Services.Foundations.Tokens;
using NEL.MESH.Services.Orchestrations.Mesh;
using Tynamix.ObjectFiller;
using Xeptions;
using Xunit;

namespace NEL.MESH.Tests.Unit.Services.Orchestrations.Mesh
{
    public partial class MeshOrchestrationTests
    {
        private readonly Mock<ITokenService> tokenServiceMock;
        private readonly Mock<IMeshService> meshServiceMock;
        private readonly Mock<IChunkService> chunkServiceMock;
        private readonly IMeshOrchestrationService meshOrchestrationService;
        private readonly Mock<IMeshConfigurationBroker> meshConfigurationBrokerMock;

        public MeshOrchestrationTests()
        {
            this.tokenServiceMock = new Mock<ITokenService>();
            this.meshServiceMock = new Mock<IMeshService>();
            this.chunkServiceMock = new Mock<IChunkService>();
            this.meshConfigurationBrokerMock = new Mock<IMeshConfigurationBroker>();

            this.meshOrchestrationService = new MeshOrchestrationService(
                tokenService: this.tokenServiceMock.Object,
                meshService: this.meshServiceMock.Object,
                chunkService: this.chunkServiceMock.Object,
                meshConfigurationBroker: this.meshConfigurationBrokerMock.Object);
        }

        private static string GetRandomString(int wordCount = 0)
        {
            return new MnemonicString(
                wordCount: wordCount == 0 ? GetRandomNumber() : wordCount,
                wordMinLength: 1,
                wordMaxLength: GetRandomNumber()).GetValue();
        }

        private static int GetRandomNumber() =>
            new IntRange(min: 2, max: 10).GetValue();

        private static List<string> GetRandomMessages()
        {
            return Enumerable.Range(1, GetRandomNumber())
                .Select(item => GetRandomString())
                .ToList();
        }

        public static TheoryData InvalidMessageList()
        {
            return new TheoryData<List<Message>>
            {
                null,
                new List<Message>(),
            };
        }

        public static TheoryData MeshDependencyValidationExceptions()
        {
            string randomMessage = GetRandomString();
            string exceptionMessage = randomMessage;
            var innerException = new Xeption(exceptionMessage);

            return new TheoryData<Xeption>
            {
                new TokenValidationException(innerException),
                new TokenDependencyValidationException(innerException),
                new MeshValidationException(innerException),
                new MeshDependencyValidationException(innerException),
            };
        }

        public static TheoryData MeshDependencyExceptions()
        {
            string randomMessage = GetRandomString();
            string exceptionMessage = randomMessage;
            var innerException = new Xeption(exceptionMessage);

            return new TheoryData<Xeption>
            {
                new TokenDependencyException(innerException),
                new TokenServiceException(innerException),
                new MeshDependencyException(innerException),
                new MeshServiceException(innerException),
            };
        }

        private static List<Message> CreateRandomChunkedSendMessages(int messageChunkCount)
        {
            List<Message> messages = new List<Message>();

            for (int i = 0; i < messageChunkCount; i++)
            {
                var message = CreateRandomSendMessage();
                message.Headers["Mex-Chunk-Range"] = new List<string> { $"{{{i + 1}:{messageChunkCount}}}" };
                messages.Add(message);
            }

            return messages;
        }

        private static Message CreateRandomSendMessage()
        {
            var message = CreateMessageFiller().Create();
            message.Headers.Add("Mex-From", new List<string> { GetRandomString() });
            message.Headers.Add("Mex-To", new List<string> { GetRandomString() });
            message.Headers.Add("Mex-WorkflowID", new List<string> { GetRandomString() });
            message.Headers.Add("Mex-Chunk-Range", new List<string> { GetRandomString() });
            message.Headers.Add("Mex-Subject", new List<string> { GetRandomString() });
            message.Headers.Add("Mex-LocalID", new List<string> { GetRandomString() });
            message.Headers.Add("Mex-FileName", new List<string> { GetRandomString() });
            message.Headers.Add("Mex-Content-Checksum", new List<string> { GetRandomString() });
            message.Headers.Add("Content-Type", new List<string> { "text/plain" });
            message.Headers.Add("Content-Encoding", new List<string> { GetRandomString() });
            message.Headers.Add("Accept", new List<string> { "application/json" });
            message.MessageId = null;

            return message;
        }

        private static Filler<Message> CreateMessageFiller()
        {
            var filler = new Filler<Message>();
            filler.Setup()
                .OnProperty(message => message.Headers).Use(new Dictionary<string, List<string>>());

            return filler;
        }
    }
}
