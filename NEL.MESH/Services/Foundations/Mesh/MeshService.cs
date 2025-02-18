﻿// ---------------------------------------------------------------
// Copyright (c) North East London ICB. All rights reserved.
// ---------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using NEL.MESH.Brokers.Mesh;
using NEL.MESH.Models.Foundations.Mesh;
using NEL.MESH.Models.Foundations.Mesh.ExternalModels;
using Newtonsoft.Json;

namespace NEL.MESH.Services.Foundations.Mesh
{
    internal partial class MeshService : IMeshService
    {
        private readonly IMeshBroker meshBroker;

        public MeshService(IMeshBroker meshBroker)
        {
            this.meshBroker = meshBroker;
        }

        public ValueTask<bool> HandshakeAsync(string authorizationToken) =>
            TryCatch(async () =>
            {
                ValidateOnHandshake(authorizationToken);
                HttpResponseMessage response = await this.meshBroker.HandshakeAsync(authorizationToken);
                ValidateResponse(response);

                return response.IsSuccessStatusCode;
            });

        public ValueTask<Message> SendMessageAsync(Message message, string authorizationToken) =>
            TryCatch(async () =>
            {
                ValidateMeshMessageOnSendMessage(message, authorizationToken);

                string chunkRange = GetKeyStringValue("Mex-Chunk-Range", message.Headers)
                    .Replace("{", string.Empty)
                        .Replace("}", string.Empty)
                            .Trim();

                if (string.IsNullOrWhiteSpace(chunkRange))
                {
                    chunkRange = "1";
                }

                string chunkPart = (chunkRange.Split(':'))[0];
                int chunkNumber;

                if (!int.TryParse(chunkPart, out chunkNumber))
                {
                    chunkNumber = 1;
                }

                HttpResponseMessage responseMessage;

                if (chunkNumber <= 1)
                {
                    responseMessage = await this.meshBroker.SendMessageAsync(
                        authorizationToken,
                        mexFrom: GetKeyStringValue("Mex-From", message.Headers),
                        mexTo: GetKeyStringValue("Mex-To", message.Headers),
                        mexWorkflowId: GetKeyStringValue("Mex-WorkflowID", message.Headers),
                        mexChunkRange: GetKeyStringValue("Mex-Chunk-Range", message.Headers),
                        mexSubject: GetKeyStringValue("Mex-Subject", message.Headers),
                        mexLocalId: GetKeyStringValue("Mex-LocalID", message.Headers),
                        mexFileName: GetKeyStringValue("Mex-FileName", message.Headers),
                        mexContentChecksum: GetKeyStringValue("Mex-Content-Checksum", message.Headers),
                        contentType: GetKeyStringValue("Content-Type", message.Headers),
                        contentEncoding: GetKeyStringValue("Content-Encoding", message.Headers),
                        accept: GetKeyStringValue("Accept", message.Headers),
                        fileContents: message.FileContent);
                }
                else
                {
                    ValidateMessageId(message.MessageId);
                    ValidateMexChunkRangeOnMultiPartFile(message);

                    responseMessage = await this.meshBroker.SendMessageAsync(
                        authorizationToken,
                        mexFrom: GetKeyStringValue("Mex-From", message.Headers),
                        mexTo: GetKeyStringValue("Mex-To", message.Headers),
                        mexWorkflowId: GetKeyStringValue("Mex-WorkflowID", message.Headers),
                        mexChunkRange: GetKeyStringValue("Mex-Chunk-Range", message.Headers),
                        mexSubject: GetKeyStringValue("Mex-Subject", message.Headers),
                        mexLocalId: GetKeyStringValue("Mex-LocalID", message.Headers),
                        mexFileName: GetKeyStringValue("Mex-FileName", message.Headers),
                        mexContentChecksum: GetKeyStringValue("Mex-Content-Checksum", message.Headers),
                        contentType: GetKeyStringValue("Content-Type", message.Headers),
                        contentEncoding: GetKeyStringValue("Content-Encoding", message.Headers),
                        accept: GetKeyStringValue("Accept", message.Headers),
                        fileContents: message.FileContent,
                        messageId: message.MessageId,
                        chunkNumber: chunkNumber.ToString());
                }

                ValidateResponse(responseMessage);
                string responseMessageBody = responseMessage.Content.ReadAsStringAsync().Result;

                Message outputMessage = new Message
                {
                    MessageId = (JsonConvert.DeserializeObject<SendMessageResponse>(responseMessageBody)).MessageId,
                    FileContent = message.FileContent,
                };

                GetHeaderValues(responseMessage, outputMessage);

                return outputMessage;
            });

        public ValueTask<Message> TrackMessageAsync(string messageId, string authorizationToken) =>
            TryCatch(async () =>
            {
                ValidateTrackMessageArguments(messageId, authorizationToken);

                HttpResponseMessage responseMessage =
                    await this.meshBroker.TrackMessageAsync(messageId, authorizationToken);

                ValidateResponse(responseMessage);
                string responseMessageBody = responseMessage.Content.ReadAsStringAsync().Result;

                Message outputMessage = new Message
                {
                    MessageId = messageId,
                    TrackingInfo = MapTrackMessageResponseToTrackingInfo(
                        JsonConvert.DeserializeObject<TrackMessageResponse>(responseMessageBody)),
                };

                GetHeaderValues(responseMessage, outputMessage);

                return outputMessage;
            });

        public ValueTask<List<string>> RetrieveMessagesAsync(string authorizationToken) =>
            TryCatch(async () =>
            {
                ValidateRetrieveMessagesArguments(authorizationToken);

                HttpResponseMessage responseMessage = await this.meshBroker
                    .GetMessagesAsync(authorizationToken);

                ValidateResponse(responseMessage);

                string responseMessageBody = responseMessage.Content
                    .ReadAsStringAsync().Result;

                GetMessagesResponse getMessagesResponse =
                    JsonConvert.DeserializeObject<GetMessagesResponse>(responseMessageBody);

                return getMessagesResponse.Messages;
            });

        public ValueTask<Message> RetrieveMessageAsync(string messageId, string authorizationToken) =>
            TryCatch(async () =>
            {
                ValidateRetrieveMessageArguments(messageId, authorizationToken);

                HttpResponseMessage initialResponse =
                    await this.meshBroker.GetMessageAsync(messageId, authorizationToken);

                ValidateReceivedResponse(initialResponse);

                byte[] fileBody = initialResponse.Content.ReadAsByteArrayAsync().Result;

                Message firstMessage = new Message
                {
                    MessageId = messageId,
                    FileContent = fileBody,
                };

                foreach (var header in initialResponse.Headers)
                {
                    firstMessage.Headers.Add(header.Key, header.Value.ToList());
                }

                foreach (var header in initialResponse.Content.Headers)
                {
                    firstMessage.Headers.Add(header.Key, header.Value.ToList());
                }

                if (initialResponse.StatusCode == HttpStatusCode.OK)
                {
                    return firstMessage;
                }

                var chunks = initialResponse.Content.Headers
                    .FirstOrDefault(h => h.Key == "Mex-Chunk-Range")
                        .Value.FirstOrDefault();

                string chunkRange = chunks.Replace("{", string.Empty).Replace("}", string.Empty);
                string[] parts = chunkRange.Split(":");
                int totalChunks = int.Parse(parts[1]);

                for (int chunkId = 1; chunkId < totalChunks; chunkId++)
                {
                    HttpResponseMessage responseMessage =
                        await this.meshBroker.GetMessageAsync(messageId, (chunkId + 1)
                            .ToString(), authorizationToken);

                    ValidateResponse(responseMessage);

                    byte[] fileContent = responseMessage.Content.ReadAsByteArrayAsync().Result;
                    firstMessage.FileContent = firstMessage.FileContent.Concat(fileContent).ToArray();

                }

                return firstMessage;
            });

        public ValueTask<bool> AcknowledgeMessageAsync(string messageId, string authorizationToken) =>
            TryCatch(async () =>
            {
                ValidateAcknowledgeMessageArguments(messageId, authorizationToken);

                HttpResponseMessage response =
                    await this.meshBroker.AcknowledgeMessageAsync(messageId, authorizationToken);

                ValidateResponse(response);

                return response.IsSuccessStatusCode;
            });

        private static void GetHeaderValues(HttpResponseMessage responseMessage, Message outputMessage)
        {
            foreach (var header in responseMessage.Content.Headers)
            {
                outputMessage.Headers
                    .Add(header.Key, header.Value.ToList());
            }
        }

        private TrackingInfo MapTrackMessageResponseToTrackingInfo(TrackMessageResponse trackMessageResponse)
        {
            TrackingInfo trackingInfo = new TrackingInfo
            {
                AddressType = trackMessageResponse.AddressType,
                Checksum = trackMessageResponse.Checksum,
                ChunkCount = trackMessageResponse.ChunkCount,
                CompressFlag = trackMessageResponse.CompressFlag,
                DownloadTimestamp = trackMessageResponse.DownloadTimestamp,
                DtsId = trackMessageResponse.DtsId,
                EncryptedFlag = trackMessageResponse.EncryptedFlag,
                ExpiryTime = trackMessageResponse.ExpiryTime,
                FileName = trackMessageResponse.FileName,
                FileSize = trackMessageResponse.FileSize,
                IsCompressed = trackMessageResponse.IsCompressed,
                LocalId = trackMessageResponse.LocalId,
                MeshRecipientOdsCode = trackMessageResponse.MeshRecipientOdsCode,
                MessageId = trackMessageResponse.MessageId,
                MessageType = trackMessageResponse.MessageType,
                PartnerId = trackMessageResponse.PartnerId,
                Recipient = trackMessageResponse.Recipient,
                RecipientName = trackMessageResponse.RecipientName,
                RecipientOrgCode = trackMessageResponse.RecipientOrgCode,
                RecipientSmtp = trackMessageResponse.RecipientSmtp,
                Sender = trackMessageResponse.Sender,
                SenderName = trackMessageResponse.SenderName,
                SenderOdsCode = trackMessageResponse.SenderOdsCode,
                SenderOrgCode = trackMessageResponse.SenderOrgCode,
                SenderSmtp = trackMessageResponse.SenderSmtp,
                Status = trackMessageResponse.Status,
                StatusSuccess = trackMessageResponse.StatusSuccess,
                UploadTimestamp = trackMessageResponse.UploadTimestamp,
                Version = trackMessageResponse.Version,
                WorkflowId = trackMessageResponse.WorkflowId
            };

            return trackingInfo;
        }

        private static string GetKeyStringValue(string key, Dictionary<string, List<string>> dictionary)
        {
            string value = dictionary.ContainsKey(key)
                ? dictionary[key]?.First()
                : string.Empty;

            return value;
        }

        private static string GetKeyStringValue(string key, HttpContentHeaders headers)
        {
            return headers.Contains(key)
                ? headers.GetValues(key)?.FirstOrDefault() ?? string.Empty
                : string.Empty;
        }
    }
}
