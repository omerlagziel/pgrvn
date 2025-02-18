﻿using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using PgRvn.Server.Exceptions;

namespace PgRvn.Server.Messages
{
    public class Describe : ExtendedProtocolMessage
    {
        /// <summary>
        /// Type of Postgres object to describe (Portal/Statement)
        /// </summary>
        public PgObjectType PgObjectType;
        public string ObjectName;

        protected override async Task<int> InitMessage(MessageReader messageReader, PipeReader reader, CancellationToken token, int msgLen)
        {
            var len = 0;

            var describeObjectType = await messageReader.ReadByteAsync(reader, token);
            len += sizeof(byte);

            var pgObjectType = describeObjectType switch
            {
                (byte)PgObjectType.Portal => PgObjectType.Portal,
                (byte)PgObjectType.PreparedStatement => PgObjectType.PreparedStatement,
                _ => throw new PgFatalException(PgErrorCodes.ProtocolViolation,
                    "Expected valid object type ('S' or 'P'), got: '" + describeObjectType)
            };

            var (describedName, describedNameLength) = await messageReader.ReadNullTerminatedString(reader, token);
            len += describedNameLength;

            PgObjectType = pgObjectType;
            ObjectName = describedName;

            return len;
        }

        protected override async Task HandleMessage(Transaction transaction, MessageBuilder messageBuilder, PipeWriter writer, CancellationToken token)
        {
            if (transaction.State == TransactionState.Idle)
                throw new PgErrorException(PgErrorCodes.NoActiveSqlTransaction,
                    "Describe message was received when no transaction is taking place.");

            if (!string.IsNullOrEmpty(ObjectName))
            {
                throw new PgErrorException(PgErrorCodes.FeatureNotSupported,
                    "Describe: Named statements/portals are not supported.");
            }

            var (schema, parameterDataTypes) = await transaction.Describe();

            if (PgObjectType == PgObjectType.PreparedStatement)
            {
                await writer.WriteAsync(messageBuilder.ParameterDescription(parameterDataTypes), token);
            }

            var response = schema.Count == 0 ? messageBuilder.NoData() : messageBuilder.RowDescription(schema);
            await writer.WriteAsync(response, token);
        }
    }
}
